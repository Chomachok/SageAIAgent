namespace Sage.Core.Events;

public record AgentFailed(string Reason) : AgentEvent;