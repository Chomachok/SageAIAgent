namespace Sage.Core.Events;

public abstract record AgentEvent
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}