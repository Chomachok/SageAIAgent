using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Sage.Core.Abstractions;
using Sage.Core.Configuration;
using Sage.Core.DTOs;
using Sage.Core.Entities;
using Sage.Core.Events;
using Sage.Infrastructure.Filters;
using Sage.Infrastructure.Plugins;

namespace Sage.Infrastructure.Agents;

public class SemanticKernelAgent : ICodingAgent
{
    private readonly ISessionRepository _sessionRepository;
    private readonly Kernel _kernel;
    private readonly Channel<AgentEvent> _toolEventChannel;
    private readonly string _systemPrompt;
    private readonly int _maxHistoryMessages;
    private readonly int _maxTokens;
    private readonly double _temperature;
    private const int MaxRetries = 5;
    private const int DefaultMaxTokens = 800;
    private readonly ILogger<SemanticKernelAgent> _logger;

    public SemanticKernelAgent(
        ISessionRepository sessionRepository,
        LlmConfig llmConfig,
        ToolCallObserverFilter observerFilter,
        Channel<AgentEvent> toolEventChannel,
        ILoggerFactory loggerFactory)
    {
        _sessionRepository = sessionRepository;
        _logger = loggerFactory.CreateLogger<SemanticKernelAgent>();
        _toolEventChannel = toolEventChannel;

        _maxHistoryMessages = llmConfig.MaxHistoryMessages;
        _maxTokens = llmConfig.MaxTokens > 0 ? llmConfig.MaxTokens : DefaultMaxTokens;
        _temperature = llmConfig.Temperature;

        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(
            modelId: llmConfig.ModelId,
            endpoint: new Uri(llmConfig.Endpoint),
            apiKey: llmConfig.ApiKey);

        builder.Services.AddSingleton<IFunctionInvocationFilter>(observerFilter);

        _kernel = builder.Build();

        var filePlugin = new FileSystemPlugin(
            loggerFactory.CreateLogger<FileSystemPlugin>(), llmConfig.RootPath);
        _kernel.Plugins.AddFromObject(filePlugin, "FileSystem");

        _systemPrompt = """
            You are Sage, an AI coding assistant with these tools:
            - read_file(path)
            - write_file(path, content)
            - list_files(path)

            When the user asks about files, call the appropriate tool.
            Do not explain — return the result.
            Keep answers concise.
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

        while (_toolEventChannel.Reader.TryRead(out _)) { }

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
            MaxTokens = _maxTokens,
            Temperature = _temperature,
            TopP = 0.9,
            FrequencyPenalty = 0.0,
            PresencePenalty = 0.0
        };

        var chatService = _kernel.GetRequiredService<IChatCompletionService>();
        var answerBuilder = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();
        var responseChannel = Channel.CreateUnbounded<AgentEvent>();

        _ = Task.Run(async () =>
        {
            try
            {
                for (var attempt = 0; attempt < MaxRetries; attempt++)
                {
                    try
                    {
                        await foreach (var chunk in chatService.GetStreamingChatMessageContentsAsync(
                                           chatHistory, settings, _kernel, cancellationToken))
                        {
                            while (_toolEventChannel.Reader.TryRead(out var toolEvt))
                                await responseChannel.Writer.WriteAsync(toolEvt, cancellationToken);

                            if (string.IsNullOrEmpty(chunk.Content)) continue;

                            answerBuilder.Append(chunk.Content);
                            await responseChannel.Writer.WriteAsync(
                                new TextChunkReceived(chunk.Content), cancellationToken);
                        }

                        while (_toolEventChannel.Reader.TryRead(out var toolEvt))
                            await responseChannel.Writer.WriteAsync(toolEvt, cancellationToken);

                        responseChannel.Writer.Complete();
                        return;
                    }
                    catch (HttpOperationException ex) when (ex.Message.Contains("429"))
                    {
                        var delay = (int)Math.Pow(2, attempt + 1) * 1000;
                        _logger.LogWarning(
                            "Rate limit (429). Retry {Attempt}/{Max} in {Delay}ms",
                            attempt + 1, MaxRetries, delay);

                        await responseChannel.Writer.WriteAsync(
                            new StatusEvent($"Retry {attempt + 1}/{MaxRetries} in {delay / 1000}s..."),
                            cancellationToken);

                        await Task.Delay(delay, cancellationToken);
                    }
                }

                responseChannel.Writer.TryComplete(
                    new Exception("Rate limit exceeded after retries."));
            }
            catch (OperationCanceledException)
            {
                responseChannel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Streaming failed");
                responseChannel.Writer.TryComplete(ex);
            }
        }, cancellationToken);

        await foreach (var evt in responseChannel.Reader.ReadAllAsync(cancellationToken))
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