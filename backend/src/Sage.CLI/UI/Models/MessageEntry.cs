namespace Sage.CLI.UI.Models;

public class MessageEntry(string role, string content, string color)
{
    public string Role { get; set; } = role;
    public string Content { get; set; } = content;
    public string Color { get; set; } = color;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}