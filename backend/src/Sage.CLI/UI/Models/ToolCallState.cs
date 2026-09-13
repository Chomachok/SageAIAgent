namespace Sage.CLI.UI.Models;

public class ToolCallState
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Parameters { get; set; } = string.Empty;
    public string? Result { get; set; }
    public bool IsActive { get; set; }
    public bool Success { get; set; }
    public DateTime StartedAt { get; set; }
    public TimeSpan Duration { get; set; }
}