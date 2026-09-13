using System.Diagnostics;
using Sage.Core.Abstractions;
using Sage.Core.DTOs;
using Sage.Core.Events;
using Sage.CLI.Rendering;
using Sage.CLI.UI.Components;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Sage.CLI.UI;

public class AppShell
{
    private readonly StatusBar _statusBar;
    private readonly ToolPanel _toolPanel;
    private readonly ChatStream _chatStream;
    private readonly SpectreMarkdownRenderer _markdownRenderer;
    private string _currentStream = "";

    public AppShell(StatusBar statusBar, ToolPanel toolPanel, ChatStream chatStream)
    {
        _statusBar = statusBar;
        _toolPanel = toolPanel;
        _chatStream = chatStream;
        _markdownRenderer = new SpectreMarkdownRenderer();
    }

    public void AddUserMessage(string message) => _chatStream.AddUser(message);
    public void AddAssistantMessage(string message) => _chatStream.AddAssistant(message);
    public void ClearChat() => _chatStream.Clear();

    /// <summary>
    /// Запускает запрос к агенту с живым отображением (спиннер + стриминг).
    /// Ошибки показываются ПОСЛЕ Live-цикла, чтобы пользователь мог их прочитать.
    /// </summary>
    public async Task<Guid?> RunQueryAsync(
        ICodingAgent agent,
        Guid? sessionId,
        string input,
        CancellationToken ct = default)
    {
        _currentStream = "";
        _chatStream.AddUser(input);

        Guid? newSessionId = sessionId;
        string? errorMessage = null;
        var spinnerChars = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
        var spinnerIndex = 0;
        var isThinking = true;
        var stopwatch = Stopwatch.StartNew();

        AnsiConsole.Clear();

        await AnsiConsole.Live(BuildLiveContent("thinking", spinnerChars[0]))
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .StartAsync(async ctx =>
            {
                // Стриминг в фоне — исключения складываем в errorMessage
                var streamTask = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var evt in agent.AskStreamingAsync(
                            new ChatRequest { SessionId = sessionId, Message = input }, ct))
                        {
                            switch (evt)
                            {
                                case TextChunkReceived chunk:
                                    isThinking = false;
                                    _currentStream += chunk.Text;
                                    break;

                                case ToolCallStarted started:
                                    isThinking = false;
                                    _toolPanel.OnStarted(started);
                                    break;

                                case ToolCallCompleted completed:
                                    _toolPanel.OnCompleted(completed);
                                    break;

                                case StatusEvent:
                                    // статусы не отображаем в Live, чтобы не мешать
                                    break;

                                case AgentCompleted done:
                                    newSessionId = done.SessionId;
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
                    finally
                    {
                        isThinking = false;
                    }
                }, ct);

                // Обновляем Live, пока стриминг не завершится
                while (!streamTask.IsCompleted || isThinking)
                {
                    spinnerIndex = (spinnerIndex + 1) % spinnerChars.Length;
                    var state = isThinking ? "thinking" : "streaming";
                    ctx.UpdateTarget(BuildLiveContent(state, spinnerChars[spinnerIndex]));
                    ctx.Refresh();
                    await Task.Delay(80, ct);
                }

                await streamTask;
                stopwatch.Stop();
            });

        // Сохраняем частичный ответ, если он был
        if (!string.IsNullOrEmpty(_currentStream))
        {
            _chatStream.AddAssistant(_currentStream);
            _currentStream = "";
        }

        // ─── ФИНАЛЬНЫЙ РЕНДЕР (ошибка остаётся видимой) ───
        AnsiConsole.Clear();
        AnsiConsole.Write(_statusBar.Build());
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        AnsiConsole.Write(_chatStream.Build());
        AnsiConsole.WriteLine();

        if (!string.IsNullOrEmpty(errorMessage))
        {
            // Постоянная панель с ошибкой — не стирается
            AnsiConsole.Write(new Panel(
                    new Markup($"[red]{Markup.Escape(errorMessage)}[/]"))
                .Header("[red] ✗ Error [/]")
                .BorderColor(Color.Red)
                .RoundedBorder()
                .Expand());

            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[grey]Press Enter to continue...[/]");
            Console.ReadLine();
        }
        else
        {
            Footer.ShowSuccess($"Done in {stopwatch.Elapsed.TotalSeconds:F1}s");
        }

        return newSessionId;
    }

    private IRenderable BuildLiveContent(string state, string? spinner, TimeSpan? elapsed = null)
    {
        var rows = new List<IRenderable>
        {
            _statusBar.Build(),
            new Rule().RuleStyle("grey"),
            _chatStream.Build()
        };

        if (!string.IsNullOrEmpty(_currentStream))
        {
            rows.Add(new Panel(new Markup(Markup.Escape(_currentStream)))
                .Header("[green]Sage[/] [yellow]streaming...[/]")
                .BorderColor(Color.Green)
                .RoundedBorder());
        }

        if (state == "thinking" && spinner != null)
        {
            rows.Add(new Markup($"[cyan]{spinner}[/] [grey]Sage is thinking...[/]"));
        }

        var tools = _toolPanel.Build();
        if (tools is not Markup)
        {
            rows.Add(new Rule().RuleStyle("grey"));
            rows.Add(tools);
        }

        if (state == "done" && elapsed.HasValue)
        {
            rows.Add(new Markup($"[grey]⏱ Time: {elapsed.Value.TotalSeconds:F2}s[/]"));
        }

        return new Rows(rows);
    }

    public void OnAgentEvent(AgentEvent evt)
    {
        switch (evt)
        {
            case TextChunkReceived chunk:
                _currentStream += chunk.Text;
                break;
            case ToolCallStarted started:
                _toolPanel.OnStarted(started);
                break;
            case ToolCallCompleted completed:
                _toolPanel.OnCompleted(completed);
                break;
            case StatusEvent status:
                Footer.ShowStatus(status.Message);
                break;
            case AgentCompleted:
                _chatStream.AddAssistant(_currentStream);
                _currentStream = "";
                break;
            case AgentFailed failed:
                if (!string.IsNullOrEmpty(_currentStream))
                {
                    _chatStream.AddAssistant(_currentStream);
                    _currentStream = "";
                }
                Footer.ShowError(failed.Reason);
                break;
        }
    }

    public void Refresh()
    {
        AnsiConsole.Clear();
        AnsiConsole.Write(_statusBar.Build());
        AnsiConsole.Write(new Rule().RuleStyle("grey"));
        AnsiConsole.Write(_chatStream.Build());

        if (!string.IsNullOrEmpty(_currentStream))
        {
            AnsiConsole.Write(new Panel(new Markup(Markup.Escape(_currentStream)))
                .Header("[green]Sage[/] [yellow]streaming...[/]")
                .BorderColor(Color.Green)
                .RoundedBorder());
        }

        var tools = _toolPanel.Build();
        if (tools is not Markup)
        {
            AnsiConsole.Write(new Rule().RuleStyle("grey"));
            AnsiConsole.Write(tools);
        }
    }
}