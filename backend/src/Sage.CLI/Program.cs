// Program.cs
// This is the main entry point of the Sage CLI application.
// It sets up the dependency injection container, configuration,
// logging and hosts the interactive chat loop.
//
// The file is organized into several logical blocks:
// 1. Helper to locate a .env file.
// 2. Loading environment variables.
// 3. Building the generic host.
// 4. Executing a single command if arguments are supplied.
// 5. Interactive console mode.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sage.Core.Abstractions;
using Sage.Core.DTOs;
using Sage.Infrastructure.Extensions;
using Sage.CLI.Repositories;
using DotNetEnv;

// -----------------------------------------------------------------------------
// 1. Find a .env file based on environment variable or conventional locations.
// -----------------------------------------------------------------------------
string? FindEnvFile()
{
    var envVar = Environment.GetEnvironmentVariable("SAGE_ENV");
    if (!string.IsNullOrEmpty(envVar) && File.Exists(envVar))
        return envVar;

    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir != null)
    {
        var envPath = Path.Combine(dir.FullName, ".env");
        if (File.Exists(envPath))
            return envPath;
        dir = dir.Parent;
    }

    var homeEnv = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".sage.env");
    if (File.Exists(homeEnv))
        return homeEnv;

    return null;
}

// -----------------------------------------------------------------------------
// 2. Load environment variables and notify the user.
// -----------------------------------------------------------------------------
var envFile = FindEnvFile();
if (envFile != null)
{
    Env.Load(envFile);
    Console.WriteLine($"[Sage] Loaded env from: {envFile}");
}
else
{
    Console.WriteLine("[Sage] No .env found. Set SAGE_ENV or create ~/.sage.env");
}

// -----------------------------------------------------------------------------
// 3. Configure the host. This includes configuration sources, services and logging.
// -----------------------------------------------------------------------------
var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((context, config) =>
    {
        var basePath = AppContext.BaseDirectory;
        config.SetBasePath(basePath)
              .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
              .AddEnvironmentVariables(); // переменные LLM__ApiKey и т.д.
    })
    .ConfigureServices((context, services) =>
    {
        services.AddInfrastructure(context.Configuration);
        services.AddScoped<ISessionRepository, InMemorySessionRepository>();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });
    })
    .Build();

// Resolve required services.
var agent = host.Services.GetRequiredService<ICodingAgent>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

// -----------------------------------------------------------------------------
// 4. If command line arguments are provided, treat them as a single request.
// -----------------------------------------------------------------------------
if (args.Length > 0)
{
    var query = string.Join(" ", args);
    try
    {
        var response = await agent.AskAsync(new ChatRequest { SessionId = null, Message = query });
        Console.WriteLine(response.Message);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error");
        Console.WriteLine($"❌ Error: {ex.Message}");
    }
    return;
}

// -----------------------------------------------------------------------------
// 5. Interactive console mode.
// -----------------------------------------------------------------------------
Console.WriteLine($"🧙 Sage v0.1.0 (working dir: {Directory.GetCurrentDirectory()})");
Console.WriteLine("Type /exit to quit, /clear to reset conversation.");

string? sessionId = null;
while (true)
{
    Console.Write("\n> ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) continue;

    if (input == "/exit")
    {
        Console.WriteLine("Goodbye!");
        break;
    }
    if (input == "/clear")
    {
        sessionId = null;
        Console.WriteLine("Conversation cleared.");
        continue;
    }

    try
    {
        var request = new ChatRequest
        {
            SessionId = sessionId != null ? Guid.Parse(sessionId) : null,
            Message = input
        };
        var response = await agent.AskAsync(request);
        sessionId = response.SessionId.ToString();
        Console.WriteLine($"\n{response.Message}");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error");
        Console.WriteLine($"❌ Error: {ex.Message}");
    }
}
