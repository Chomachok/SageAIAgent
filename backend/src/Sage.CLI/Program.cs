using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sage.Core.Abstractions;
using Sage.Core.DTOs;
using Sage.Infrastructure.Extensions;
using Sage.CLI.Repositories;
using Spectre.Console;
using BoxOfYellow.ConsoleMarkdownRenderer.Spectre;
using Microsoft.Extensions.Configuration;
using Sage.CLI;

var llmConfig = ConfigLoader.LoadConfig(args);

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        if (llmConfig != null)
            services.AddInfrastructure(llmConfig, context.Configuration.GetConnectionString("DefaultConnection"));
        services.AddScoped<ISessionRepository, InMemorySessionRepository>();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });
    })
    .Build();

var agent = host.Services.GetRequiredService<ICodingAgent>();
var logger = host.Services.GetRequiredService<ILogger<Program>>();

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

        var mdRenderer = new MarkdownRenderer();
        var rendered = mdRenderer.Render(response.Message);
        if (rendered.Root != null)
            AnsiConsole.Write(rendered.Root);
        else
            Console.WriteLine(response.Message);
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error");
        Console.WriteLine($"❌ Error: {ex.Message}");
    }
}