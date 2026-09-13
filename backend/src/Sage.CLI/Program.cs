using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sage.Core.Abstractions;
using Sage.Core.DTOs;
using Sage.Core.Events;
using Sage.Infrastructure.Extensions;
using Sage.CLI;
using Sage.CLI.Repositories;
using Sage.CLI.Rendering;
using Sage.CLI.UI;
using Sage.CLI.UI.Components;
using Spectre.Console;

var llmConfig = ConfigLoader.LoadConfig(args);

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        services.AddInfrastructure(llmConfig,
            context.Configuration.GetConnectionString("DefaultConnection"));
        services.AddScoped<ISessionRepository, InMemorySessionRepository>();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.SetMinimumLevel(LogLevel.Warning);
        });
    })
    .Build();

var agent = host.Services.GetRequiredService<ICodingAgent>();

var statusBar = new StatusBar(
    model: llmConfig?.ModelId ?? "unknown",
    mode: "Auto",
    workingDir: Directory.GetCurrentDirectory());
var toolPanel = new ToolPanel();
var chatStream = new ChatStream();
var appShell = new AppShell(statusBar, toolPanel, chatStream);
var composer = new Composer();
var markdownRenderer = new SpectreMarkdownRenderer();

// ─── Single-shot mode ───
if (args.Length > 0)
{
    var query = string.Join(" ", args);
    try
    {
        var stopwatch = Stopwatch.StartNew();
        var fullResponse = "";
        string? errorMessage = null;

        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync("Sage is thinking...", async ctx =>
            {
                try
                {
                    await foreach (var evt in agent.AskStreamingAsync(
                        new ChatRequest { SessionId = null, Message = query }))
                    {
                        switch (evt)
                        {
                            case TextChunkReceived chunk:
                                fullResponse += chunk.Text;
                                break;
                            case StatusEvent status:
                                ctx.Status($"{status.Message}");
                                break;
                            case ToolCallStarted started:
                                ctx.Status($"{started.ToolName}...");
                                break;
                            case AgentFailed failed:
                                errorMessage = failed.Reason;
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                }
            });

        stopwatch.Stop();

        if (!string.IsNullOrEmpty(errorMessage))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.Write(new Panel(new Markup($"[red]{Markup.Escape(errorMessage)}[/]"))
                .Header("[red] ✗ Error [/]")
                .BorderColor(Color.Red)
                .RoundedBorder()
                .Expand());
            return;
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[grey]⏱ Time: {stopwatch.Elapsed.TotalSeconds:F2}s[/]");
        AnsiConsole.WriteLine();
        markdownRenderer.Render(fullResponse);
    }
    catch (Exception ex)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Write(new Panel(new Markup($"[red]{Markup.Escape(ex.Message)}[/]"))
            .Header("[red] ✗ Error [/]")
            .BorderColor(Color.Red)
            .RoundedBorder()
            .Expand());
    }
    return;
}

// ─── Interactive mode ───
AnsiConsole.Clear();
AnsiConsole.Write(new FigletText("Sage").Color(Color.Cyan1).LeftJustified());
AnsiConsole.MarkupLine("[grey]AI coding assistant. Type [cyan]/help[/] for commands.[/]");
AnsiConsole.WriteLine();

Guid? sessionId = null;

while (true)
{
    var input = composer.Prompt();
    if (input == null) break;

    if (input.Equals("/exit", StringComparison.OrdinalIgnoreCase))
    {
        AnsiConsole.MarkupLine("[grey]Goodbye![/]");
        break;
    }
    if (input.Equals("/clear", StringComparison.OrdinalIgnoreCase))
    {
        sessionId = null;
        appShell.ClearChat();
        AnsiConsole.Clear();
        AnsiConsole.MarkupLine("[grey]Conversation cleared.[/]");
        continue;
    }
    if (input.Equals("/help", StringComparison.OrdinalIgnoreCase))
    {
        AnsiConsole.MarkupLine("[bold]Commands:[/]");
        AnsiConsole.MarkupLine("  [cyan]/exit[/]   — quit");
        AnsiConsole.MarkupLine("  [cyan]/clear[/]  — reset conversation");
        AnsiConsole.MarkupLine("  [cyan]/help[/]   — show this help");
        continue;
    }
    if (string.IsNullOrWhiteSpace(input)) continue;

    sessionId = await appShell.RunQueryAsync(agent, sessionId, input);
}