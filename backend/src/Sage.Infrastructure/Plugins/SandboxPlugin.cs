using System.ComponentModel;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;

namespace Sage.Infrastructure.Plugins;

public class SandboxPlugin
{
    private readonly ILogger<SandboxPlugin> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _pistonBaseUrl;

    public SandboxPlugin(ILogger<SandboxPlugin> logger)
    {
        _logger = logger;
        _pistonBaseUrl = Environment.GetEnvironmentVariable("PISTON_BASE_URL") ?? "http://piston:2000";
        _httpClient = new HttpClient { BaseAddress = new Uri(_pistonBaseUrl) };
        _httpClient.Timeout = TimeSpan.FromSeconds(30);
    }

    [KernelFunction("execute_code")]
    [Description("Executes code in an isolated sandbox using Piston. Supports many languages: python, csharp, javascript, java, cpp, go, ruby, and more.")]
    public async Task<string> ExecuteCodeAsync(
        [Description("The source code to execute.")] string code,
        [Description("Programming language: python, csharp, javascript, java, cpp, go, ruby, etc.")] string language,
        [Description("Optional: version of the language (e.g., '3.10.0' for Python). If not specified, the latest will be used.")] string? version = null)
    {
        _logger.LogInformation("Executing {Language} code", language);

        try
        {
            // 1. Получаем актуальную версию языка, если не указана
            if (string.IsNullOrEmpty(version))
            {
                version = await GetLatestVersionAsync(language);
                if (version == null)
                    return $"Language '{language}' is not supported by Piston.";
            }

            // 2. Формируем запрос
            var request = new
            {
                language = language,
                version = version,
                files = new[]
                {
                    new { content = code }
                }
            };

            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            // 3. Отправляем запрос на выполнение
            var response = await _httpClient.PostAsync("/api/v2/execute", content);
            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<PistonResponse>(jsonResponse);

            if (result?.Run == null)
                return "Invalid response from Piston.";

            // 4. Формируем результат
            var output = result.Run.Stdout ?? "";
            var error = result.Run.Stderr ?? "";
            var exitCode = result.Run.Code ?? 0;

            var builder = new StringBuilder();
            if (!string.IsNullOrEmpty(output))
                builder.AppendLine("Output:").AppendLine(output);
            if (!string.IsNullOrEmpty(error))
                builder.AppendLine("Errors:").AppendLine(error);
            builder.AppendLine($"Exit code: {exitCode}");

            return builder.ToString().Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing code via Piston");
            return $"Piston error: {ex.Message}";
        }
    }

    private async Task<string?> GetLatestVersionAsync(string language)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/v2/runtimes");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var runtimes = JsonSerializer.Deserialize<List<PistonRuntime>>(json);

            var runtime = runtimes?.FirstOrDefault(r => r.Language == language);
            return runtime?.Version;
        }
        catch
        {
            // Если не удалось получить список, используем заглушку для некоторых языков
            return language.ToLower() switch
            {
                "python" => "3.10.0",
                "csharp" or "c#" => "6.12.0",
                "javascript" or "js" => "17.0.0",
                "java" => "17.0.0",
                "cpp" or "c++" => "11.2.0",
                "go" => "1.18.0",
                "ruby" => "3.1.0",
                _ => null
            };
        }
    }

    // DTO-классы для Piston
    private class PistonResponse
    {
        public PistonRun? Run { get; set; }
    }

    private class PistonRun
    {
        public string? Stdout { get; set; }
        public string? Stderr { get; set; }
        public int? Code { get; set; }
    }

    private class PistonRuntime
    {
        public string Language { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
    }
}