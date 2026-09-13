namespace Sage.Core.Events;

public record ToolCallStarted(
    string ToolName, 
    string ParametersJson,
    Guid CallId) : AgentEvent;