namespace Sage.Core.Events;

public record ToolCallCompleted(
    Guid CallId,
    string? Result,
    TimeSpan Duration,
    bool Success) : AgentEvent;