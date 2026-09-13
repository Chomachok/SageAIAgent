using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Sage.Core.Abstractions;
using Sage.Core.Configuration;
using Sage.Core.DTOs;
using Sage.Core.Entities;
using Sage.Core.Events;
using Sage.Infrastructure.Plugins;

namespace Sage.Infrastructure.Agents;

public class SemanticKernelAgent : ICodingAgent
{
    private readonly ISessionRepository _sessionRepository;
    private readonly Kernel _kernel;
    private readonly string _systemPrompt;
    private readonly int _maxHistoryMessages;
    private readonly int _maxTokens;
    private readonly double _temperature;
    private const int MaxRetries = 5;
    private readonly ILogger<SemanticKernelAgent> _logger;

    public SemanticKernelAgent(
        ISessionRepository sessionRepository,
        LlmConfig llmConfig,           
        ILoggerFactory loggerFactory)
    {
        _sessionRepository = sessionRepository;
        _logger = loggerFactory.CreateLogger<SemanticKernelAgent>();

        _maxHistoryMessages = llmConfig.MaxHistoryMessages;
        _maxTokens = llmConfig.MaxTokens;
        _temperature = llmConfig.Temperature;

        if (string.IsNullOrWhiteSpace(llmConfig.Endpoint))
            throw new InvalidOperationException("Endpoint is not configured in sage.config.json.");
        if (string.IsNullOrWhiteSpace(llmConfig.ApiKey))
            throw new InvalidOperationException("ApiKey is not configured in sage.config.json.");
        if (string.IsNullOrWhiteSpace(llmConfig.ModelId))
            throw new InvalidOperationException("ModelId is not configured in sage.config.json.");

        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(
            modelId: llmConfig.ModelId,
            endpoint: new Uri(llmConfig.Endpoint),
            apiKey: llmConfig.ApiKey
        );

        _kernel = builder.Build();
        
        var fileLogger = loggerFactory.CreateLogger<FileSystemPlugin>();
        var filePlugin = new FileSystemPlugin(fileLogger, llmConfig.RootPath);
        _kernel.Plugins.AddFromObject(filePlugin, "FileSystem");

        _systemPrompt = """
            Ты — Sage, агент с функциями:
            read_file, write_file, list_files.
            Если пользователь просит что-то с файлами — вызывай функцию. Не объясняй. 
            Отвечай только результатом функции.
            """;
    }

    public async IAsyncEnumerable<AgentEvent> AskStreamingAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Session session;
        if (request.SessionId.HasValue)
        {
            session = await _sessionRepository.GetByIdAsync(request.SessionId.Value, cancellationToken)
                      ?? throw new ArgumentException("Session not found");
        }
        else
        {
            session = new Session
            {
                Title = request.Message.Length > 30 ? request.Message[..30] : request.Message
            };
            await _sessionRepository.CreateAsync(session, cancellationToken);
        }

        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(_systemPrompt);

        var lastMessages = session.Messages
            .OrderBy(m => m.CreatedAt)
            .TakeLast(_maxHistoryMessages);

        foreach (var msg in lastMessages)
        {
            if (msg.Role == "user") chatHistory.AddUserMessage(msg.Content);
            else if (msg.Role == "assistant") chatHistory.AddAssistantMessage(msg.Content);
        }
        chatHistory.AddUserMessage(request.Message);

        var settings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            MaxTokens = _maxTokens > 0 ? _maxTokens : null,
            Temperature = _temperature,
            TopP = 0.9,
            FrequencyPenalty = 0.0,
            PresencePenalty = 0.0
        };

        var chatService = _kernel.GetRequiredService<IChatCompletionService>();
        var answerBuilder = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();

        var channel = Channel.CreateUnbounded<AgentEvent>();

        _ = Task.Run(async () =>
        {
            for (var attempt = 0; attempt < MaxRetries; attempt++)
            {
                try
                {
                    await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
                        chatHistory, settings, _kernel, cancellationToken))
                    {
                        if (string.IsNullOrEmpty(chunk.Content)) continue;

                        answerBuilder.Append(chunk.Content);
                        await channel.Writer.WriteAsync(
                            new TextChunkReceived(chunk.Content), cancellationToken);
                    }

                    channel.Writer.Complete();
                    return;
                }
                catch (HttpOperationException ex) when (ex.Message.Contains("429"))
                {
                    var delay = (int)Math.Pow(2, attempt + 1) * 1000;
                    _logger.LogWarning(
                        "Rate limit (429). Retry {Attempt}/{Max} in {Delay}ms",
                        attempt + 1, MaxRetries, delay);

                    await channel.Writer.WriteAsync(
                        new StatusEvent($"⏳ Retry {attempt + 1}/{MaxRetries} in {delay / 1000}s..."),
                        cancellationToken);

                    await Task.Delay(delay, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Streaming failed");
                    await channel.Writer.WriteAsync(new AgentFailed(ex.Message), cancellationToken);
                    channel.Writer.Complete();
                    return;
                }
            }

            await channel.Writer.WriteAsync(
                new AgentFailed("Rate limit exceeded after retries."), cancellationToken);
            channel.Writer.Complete();
        }, cancellationToken);

        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }

        stopwatch.Stop();

        var answer = answerBuilder.ToString();

        await _sessionRepository.AddMessageAsync(new Message
        {
            SessionId = session.Id,
            Role = "user",
            Content = request.Message
        }, cancellationToken);

        await _sessionRepository.AddMessageAsync(new Message
        {
            SessionId = session.Id,
            Role = "assistant",
            Content = answer
        }, cancellationToken);

        session.UpdatedAt = DateTime.UtcNow;
        await _sessionRepository.UpdateAsync(session, cancellationToken);

        yield return new AgentCompleted(session.Id, answer, stopwatch.Elapsed);
    }
}