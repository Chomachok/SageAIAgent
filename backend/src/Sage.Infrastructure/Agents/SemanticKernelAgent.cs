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

    public SemanticKernelAgent(
        ISessionRepository sessionRepository,
        IOptions<LlmOptions> llmOptions)
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
            logger: null,
            rootPath: "/app"
        );
        _kernel.Plugins.AddFromObject(filePlugin, "FileSystem");

        _systemPrompt = """

                        Ты — Sage, агент, который умеет читать файлы с помощью функции read_file.

                        Жёсткие правила:
                        1. Если пользователь просит прочитать файл — ты ОБЯЗАН вызвать функцию read_file.
                        2. НЕ ПРЕДЛАГАЙ код для чтения файла, НЕ ОТВЕЧАЙ текстом.
                        3. Используй результат функции и перескажи содержимое пользователю.
                        4. Если функция вернула ошибку — сообщи её текст пользователю.

                        Никогда не отказывайся от вызова функции.

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

        foreach (var msg in session.Messages.OrderBy(m => m.CreatedAt))
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
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        var chatService = _kernel.GetRequiredService<IChatCompletionService>();

        // 4. Вызов с автоматическим вызовом функций
        var result = await chatService.GetChatMessageContentAsync(
            chatHistory,
            settings,
            _kernel,
            cancellationToken
        );

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