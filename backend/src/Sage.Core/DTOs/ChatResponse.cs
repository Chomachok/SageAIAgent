namespace Sage.Core.DTOs;

public class ChatResponse
{
    public Guid SessionId { get; set; }
    public string Message { get; set; } = string.Empty;
}
