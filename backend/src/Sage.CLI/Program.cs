using System.Diagnostics;
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

// ─── Режим одного запроса ───
if (args.Length > 0)
{
    var query = string.Join(" ", args);
    try
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await AnsiConsole.Status()
            .StartAsync("🧠 Sage is thinking...", async ctx =>
            {
                ctx.Spinner(Spinner.Known.Dots);
                ctx.SpinnerStyle(Style.Parse("cyan"));
                return await agent.AskAsync(new ChatRequest { SessionId = null, Message = query });
            });
        stopwatch.Stop();

        var elapsed = stopwatch.Elapsed;
        string timeStr = elapsed.TotalSeconds < 1 
            ? $"{elapsed.TotalMilliseconds:F0} ms" 
            : $"{elapsed.TotalSeconds:F2} s";
        Console.WriteLine($"⏱️ Time: {timeStr}");
        Console.WriteLine(response.Message);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error");
        Console.WriteLine($"❌ Error: {ex.Message}");
    }
    return;
}

Console.WriteLine($"🧙 Sage (working dir: {Directory.GetCurrentDirectory()})");
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

        var stopwatch = Stopwatch.StartNew();
        var response = await AnsiConsole.Status()
            .StartAsync("🧠 Sage is thinking...", async ctx =>
            {
                ctx.Spinner(Spinner.Known.Dots);
                ctx.SpinnerStyle(Style.Parse("cyan"));
                return await agent.AskAsync(request);
            });
        stopwatch.Stop();

        sessionId = response.SessionId.ToString();

        var elapsed = stopwatch.Elapsed;
        string timeStr = elapsed.TotalSeconds < 1 
            ? $"{elapsed.TotalMilliseconds:F0} ms" 
            : $"{elapsed.TotalSeconds:F2} s";
        Console.WriteLine($"⏱️ Time: {timeStr}");

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