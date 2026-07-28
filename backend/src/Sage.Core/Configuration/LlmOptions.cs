namespace Sage.Core.Configuration;

public class LlmOptions
{
    public string Provider { get; set; } = "GitHubModels";
    public string ModelId { get; set; } = "codestral-2501";
    public string Endpoint { get; set; } = "https://models.inference.ai.azure.com";
    public string ApiKey { get; set; } = string.Empty;
}