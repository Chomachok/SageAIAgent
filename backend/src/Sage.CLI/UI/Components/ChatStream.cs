using Sage.CLI.Rendering;
using Sage.CLI.UI.Models;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace Sage.CLI.UI.Components;

public class ChatStream
{
    private readonly List<MessageEntry> _messages = new();
    private readonly SpectreMarkdownRenderer _markdownRenderer = new();

    public void AddUser(string content)
        => _messages.Add(new MessageEntry("You", content, "cyan"));

    public void AddAssistant(string content)
        => _messages.Add(new MessageEntry("Sage", content, "green"));

    public void Clear() => _messages.Clear();

    public IRenderable Build()
    {
        if (_messages.Count == 0)
        {
            var empty = new Markup(
                "[grey]Ask Sage anything about programming. Type /help for commands.[/]");
            return new Panel(empty).Border(BoxBorder.None).PadLeft(2);
        }

        var rows = new List<IRenderable>();

        foreach (var m in _messages)
        {
            var color = m.Color switch
            {
                "cyan" => Color.Cyan1,
                "green" => Color.Green,
                _ => Color.White
            };

            IRenderable contentRenderable;
            if (m.Role == "Sage")
            {
                contentRenderable = _markdownRenderer.RenderToRenderable(m.Content);
            }
            else
            {
                contentRenderable = new Text(m.Content);
            }

            rows.Add(new Panel(contentRenderable)
                .Header($"[{m.Color}]{m.Role}[/] [grey]{m.Timestamp:HH:mm:ss}[/]")
                .BorderColor(color)
                .RoundedBorder());
        }

        return new Rows(rows);
    }
}