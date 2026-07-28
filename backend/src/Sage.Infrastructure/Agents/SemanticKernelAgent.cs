using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Agents;
using Sage.Core.Abstractions;
using Sage.Core.Configuration;
using Sage.Core.DTOs;
using Sage.Core.Entities;
using System.Text;

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

        // Настройка Kernel
        var builder = Kernel.CreateBuilder();
        var options = llmOptions.Value;

        builder.AddOpenAIChatCompletion(
            modelId: options.ModelId,
            endpoint: new Uri(options.Endpoint),
            apiKey: options.ApiKey
        );

        _kernel = builder.Build();

        // Системный промпт (можно вынести в настройки)
        _systemPrompt = @"
Ты — Sage, мудрый наставник по программированию.
Ты помогаешь писать код на любых языках: C#, Python, JavaScript, Go, Rust, SQL и других.
Отвечай на том языке, на котором задан вопрос (русский, английский и т.д.).
Давай примеры кода, объясняй их построчно.
Если вопрос не относится к программированию, вежливо предложи вернуться к теме.
Будь дружелюбным, терпеливым и вдохновляющим.
";
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

        // 2. Формируем историю из БД (последние N сообщений, чтобы не перегружать контекст)
        //    Здесь мы используем всё, что есть, но можно ограничить.
        var chatHistory = new ChatHistory();
        chatHistory.AddSystemMessage(_systemPrompt);

        // Загружаем сообщения (они уже подгружены через Include в репозитории)
        foreach (var msg in session.Messages.OrderBy(m => m.CreatedAt))
        {
            if (msg.Role == "user")
                chatHistory.AddUserMessage(msg.Content);
            else if (msg.Role == "assistant")
                chatHistory.AddAssistantMessage(msg.Content);
        }

        // Добавляем новый вопрос пользователя
        chatHistory.AddUserMessage(request.Message);

        // 3. Создаём агента (ChatCompletionAgent) с текущим Kernel и историей
        var agent = new ChatCompletionAgent
        {
            Kernel = _kernel,
            Instructions = _systemPrompt // можно переопределить, но мы уже добавили системное сообщение
        };

        // 4. Получаем ответ (потоковый, но мы собираем в строку)
        var responseStream = agent.InvokeAsync(chatHistory, cancellationToken: cancellationToken);
        var fullResponse = new StringBuilder();

        await foreach (var item in responseStream)
        {
            fullResponse.Append(item.Message?.Content ?? "");
        }

        var answer = fullResponse.ToString();

        // 5. Сохраняем сообщения в БД
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

        // Обновляем время последнего изменения сессии
        session.UpdatedAt = DateTime.UtcNow;
        // Так как у нас нет метода Update, можно сделать через репозиторий, но мы не сохраняем сессию отдельно.
        // Можно добавить метод UpdateSessionAsync в репозиторий, но пока просто сохраним через контекст (через Unit of Work).
        // Но у нас нет Unit of Work, упростим: в репозитории есть только Create и AddMessage.
        // Для обновления UpdatedAt нужно либо добавить метод, либо использовать DbContext напрямую.
        // Пока оставим как есть, а позже добавим метод Update.

        // Возвращаем ответ
        return new ChatResponse
        {
            SessionId = session.Id,
            Message = answer
        };
    }
}