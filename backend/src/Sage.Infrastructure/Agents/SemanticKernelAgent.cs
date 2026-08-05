using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using Microsoft.Extensions.Options;
using Sage.Core.Abstractions;
using Sage.Core.Configuration;
using Sage.Core.DTOs;
using Sage.Core.Entities;
using Sage.Infrastructure.Plugins;

namespace Sage.Infrastructure.Agents;

public class SemanticKernelAgent : ICodingAgent
{
    private readonly ISessionRepository _sessionRepository;
    private readonly Kernel _kernel;
    private readonly string _systemPrompt;
    private const int MaxRetries = 5;

    public SemanticKernelAgent(
        ISessionRepository sessionRepository,
        IOptions<LlmOptions> llmOptions,
        ILoggerFactory loggerFactory)
    {
        _sessionRepository = sessionRepository;

        var options = llmOptions.Value;
        var builder = Kernel.CreateBuilder();
        builder.AddOpenAIChatCompletion(
            modelId: options.ModelId,
            endpoint: new Uri(options.Endpoint),
            apiKey: options.ApiKey
        );

        _kernel = builder.Build();

        // Регистрируем плагин FileSystem
        var filePlugin = new FileSystemPlugin(
            logger: loggerFactory.CreateLogger<FileSystemPlugin>(),
            rootPath: "/app"
        );
        _kernel.Plugins.AddFromObject(filePlugin, "FileSystem");
        
        // Регистрируем плагин Sandbox
        var sandboxLogger = loggerFactory.CreateLogger<SandboxPlugin>();
        var sandboxPlugin = new SandboxPlugin(sandboxLogger);
        _kernel.Plugins.AddFromObject(sandboxPlugin, "Sandbox");

        _systemPrompt = """

                        Ты — Sage, агент с функциями:
                        - read_file(path)
                        - write_file(path, content)
                        - execute_code(code, language)

                        Всегда используй функции. Не давай текстовых примеров. Отвечай результатами функций.

                        """;
    }

    public async Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Получаем или создаём сессию
        Session session;
        if (request.SessionId.HasValue)
        {
            session = await _sessionRepository.GetByIdAsync(request.SessionId.Value, cancellationToken);
            if (session == null)
                throw new ArgumentException("Session not found");
        }
        else
        {
            session = new Session { Title = request.Message.Length > 50 ? request.Message[..50] : request.Message };
            await _sessionRepository.CreateAsync(session, cancellationToken);
        }

        // 2. Формируем историю
        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(_systemPrompt);

        var recentMessages = session.Messages
            .OrderBy(m => m.CreatedAt)
            .TakeLast(3)
            .ToList();

        foreach (var msg in recentMessages)
        {
            if (msg.Role == "user")
                chatHistory.AddUserMessage(msg.Content);
            else if (msg.Role == "assistant")
                chatHistory.AddAssistantMessage(msg.Content);
        }

        chatHistory.AddUserMessage(request.Message);

        // 3. Настройки с автовызовом функций
        var settings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions,
            MaxTokens = 500
        };

        var chatService = _kernel.GetRequiredService<IChatCompletionService>();

        var attempt = 0;
        ChatMessageContent result;

        while (true)
        {
            try
            {
                // 4. Вызов с автоматическим вызовом функций
                result = await chatService.GetChatMessageContentAsync(
                    chatHistory,
                    settings,
                    _kernel,
                    cancellationToken
                );
                break;
            }
            catch (HttpOperationException ex) when (ex.Message.Contains("429"))
            {
                attempt++;
                if (attempt > MaxRetries) throw;
                var delayMs = (int)Math.Pow(2, attempt) * 1000;
                await Task.Delay(delayMs, cancellationToken);
            }
        }

        var answer = result.Content ?? "No response from model.";

        // 5. Сохраняем сообщения
        var userMessage = new Message
        {
            SessionId = session.Id,
            Role = "user",
            Content = request.Message
        };
        await _sessionRepository.AddMessageAsync(userMessage, cancellationToken);

        var assistantMessage = new Message
        {
            SessionId = session.Id,
            Role = "assistant",
            Content = answer
        };
        await _sessionRepository.AddMessageAsync(assistantMessage, cancellationToken);

        session.UpdatedAt = DateTime.UtcNow;
        await _sessionRepository.UpdateAsync(session, cancellationToken);

        return new ChatResponse
        {
            SessionId = session.Id,
            Message = answer
        };
    }
}