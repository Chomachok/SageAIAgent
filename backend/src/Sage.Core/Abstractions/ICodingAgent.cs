using Sage.Core.DTOs;
using Sage.Core.Events;

namespace Sage.Core.Abstractions;

public interface ICodingAgent
{
    IAsyncEnumerable<AgentEvent> AskStreamingAsync(
        ChatRequest request,
        CancellationToken cancellationToken = default);
}
