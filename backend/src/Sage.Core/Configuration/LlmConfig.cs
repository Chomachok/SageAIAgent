namespace Sage.Core.Configuration;
 
public class LlmConfig
{
    public string Provider { get; set; } = string.Empty;
    public string ModelId { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = "";
    public string RootPath { get; set; } = ".";
    public int MaxTokens { get; set; } = 500;
    public double Temperature { get; set; } = 0.1;
    public int MaxHistoryMessages { get; set; } = 3;
}