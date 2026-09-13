namespace Sage.Core.Events;

public record AgentCompleted(
    Guid SessionId,
    string FullResponse,
    TimeSpan TotalDuration) : AgentEvent;