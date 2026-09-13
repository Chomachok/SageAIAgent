using Sage.CLI.UI.Models;
using Sage.Core.Events;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Sage.CLI.UI.Components;

public class ToolPanel
{
    private readonly List<ToolCallState> _calls = new();

    public void OnStarted(ToolCallStarted evt)
    {
        _calls.Add(new ToolCallState
        {
            Id = evt.CallId,
            Name = evt.ToolName,
            Parameters = evt.ParametersJson,
            StartedAt = DateTime.UtcNow,
            IsActive = true
        });
    }

    public void OnCompleted(ToolCallCompleted evt)
    {
        var call = _calls.FirstOrDefault(c => c.Id == evt.CallId);
        if (call != null)
        {
            call.IsActive = false;
            call.Result = evt.Result;
            call.Duration = evt.Duration;
            call.Success = evt.Success;
        }
    }

    public bool HasActiveCalls => _calls.Any(c => c.IsActive);

    public IRenderable Build()
    {
        if (_calls.Count == 0)
            return new Markup("");

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumn(new TableColumn("").NoWrap().Width(2))
            .AddColumn(new TableColumn("").NoWrap().Width(16))
            .AddColumn(new TableColumn(""));

        foreach (var call in _calls.TakeLast(5))
        {
            var icon = call.IsActive
                ? "[yellow]⚙[/]"
                : (call.Success ? "[green]✓[/]" : "[red]✗[/]");

            var duration = call.IsActive
                ? $"[dim]{(DateTime.UtcNow - call.StartedAt).TotalSeconds:F1}s[/]"
                : $"[dim]{call.Duration.TotalSeconds:F1}s[/]";

            var paramPreview = Truncate(call.Parameters, 60);

            table.AddRow(
                new Markup(icon),
                new Markup($"[bold]{call.Name}[/]"),
                new Markup($"[dim]{Markup.Escape(paramPreview)}[/] {duration}")
            );
        }

        return table;
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s[..max] + "…";
    }
}