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
        
        var fileLogger = loggerFactory.CreateLogger<FileSystemPlugin>();
        var filePlugin = new FileSystemPlugin(fileLogger, options.RootPath);
        _kernel.Plugins.AddFromObject(filePlugin, "FileSystem");

        _systemPrompt = """

                        Ты — Sage, агент, который умеет взаимодействовать с файловой системой через функции.

                        Доступные функции:
                        - read_file(path) — читает содержимое файла.
                        - write_file(path, content) — создаёт или перезаписывает файл с указанным содержимым.
                        - list_files(path) — показывает список файлов и папок.

                        ВАЖНО: Если пользователь просит что-то записать в файл, ты ОБЯЗАН вызвать функцию write_file.
                        Например, если пользователь говорит: "Запиши в файл /app/test-files/hello.txt текст 'Привет'", ты должен вызвать write_file с параметрами path=''/app/test-files/hello.txt'' и content=''Привет''.

                        НЕ ИСПОЛЬЗУЙ текстовый ответ для имитации записи. Только реальный вызов функции.
                        После вызова функции ты можешь сообщить пользователю о результате.

                        """;
    }

    public async Task<ChatResponse> AskAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        Session? session;
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

        var settings = new OpenAIPromptExecutionSettings
        {
            ToolCallBehavior = ToolCallBehavior.AutoInvokeKernelFunctions
        };

        var chatService = _kernel.GetRequiredService<IChatCompletionService>();

        var result = await chatService.GetChatMessageContentAsync(
            chatHistory,
            settings,
            _kernel,
            cancellationToken
        );

        var answer = result.Content ?? "No response from model.";

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