using System.Text;
using System.Text.Json;
using Sage.Core.Configuration;

namespace Sage.CLI;

public static class ConfigLoader
{
    public static LlmConfig? LoadConfig(string[] args)
    {
        string? configPath = null;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] == "--config" || args[i] == "-c")
            {
                if (i + 1 < args.Length)
                    configPath = args[i + 1];
                break;
            }
        }

        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
        {
            var env = Environment.GetEnvironmentVariable("SAGE_CONFIG");
            if (!string.IsNullOrEmpty(env) && File.Exists(env))
                configPath = env;
        }

        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
        {
            var exeDir = AppContext.BaseDirectory;
            var projectRoot = Path.GetFullPath(Path.Combine(exeDir, "..", "..", ".."));
            var candidate = Path.Combine(projectRoot, "sage.config.json");
            if (File.Exists(candidate))
                configPath = candidate;
        }

        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "sage.config.json");
                if (File.Exists(candidate))
                {
                    configPath = candidate;
                    break;
                }
                dir = dir.Parent;
            }
        }

        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
        {
            var home = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".sage", "config.json");
            if (File.Exists(home))
                configPath = home;
        }

        if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            return null;

        try
        {
            var json = File.ReadAllText(configPath, Encoding.UTF8);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("Llm", out var llmObj))
                root = llmObj;

            return new LlmConfig
            {
                Provider = GetString(root, "Provider") ?? "Groq",
                ModelId = GetString(root, "ModelId") ?? "openai/gpt-oss-20b",
                Endpoint = GetString(root, "Endpoint") ?? "https://api.groq.com/openai/v1",
                ApiKey = GetString(root, "ApiKey") ?? "",
                RootPath = GetString(root, "RootPath") ?? ".",
                MaxTokens = GetInt(root, "MaxTokens", 500),
                Temperature = GetDouble(root, "Temperature", 0.1),
                MaxHistoryMessages = GetInt(root, "MaxHistoryMessages", 3)
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? GetString(JsonElement root, string name)
    {
        foreach (var prop in root.EnumerateObject())
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                return prop.Value.GetString();
        return null;
    }

    private static int GetInt(JsonElement root, string name, int def)
    {
        foreach (var prop in root.EnumerateObject())
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) && prop.Value.TryGetInt32(out var v))
                return v;
        return def;
    }

    private static double GetDouble(JsonElement root, string name, double def)
    {
        foreach (var prop in root.EnumerateObject())
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase) && prop.Value.TryGetDouble(out var v))
                return v;
        return def;
    }
}