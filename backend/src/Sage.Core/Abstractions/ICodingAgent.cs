using Sage.Core.DTOs;

namespace Sage.Core.Abstractions;

public interface ICodingAgent
{
    Task<ChatResponse> AskAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default
    );
}
