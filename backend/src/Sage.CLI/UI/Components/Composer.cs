using Spectre.Console;

namespace Sage.CLI.UI.Components;

public class Composer
{
    private readonly List<string> _history = new();
    private int _historyIndex = -1;

    public string? Prompt()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.Markup("[bold cyan]❯[/] ");

        var input = Console.ReadLine();
        if (input == null) return null;

        if (!string.IsNullOrWhiteSpace(input))
        {
            _history.Add(input);
            _historyIndex = _history.Count;
        }

        return input;
    }

    public string? Previous()
    {
        if (_history.Count == 0) return null;
        if (_historyIndex > 0) _historyIndex--;
        return _history[_historyIndex];
    }

    public string? Next()
    {
        if (_historyIndex < _history.Count - 1)
        {
            _historyIndex++;
            return _history[_historyIndex];
        }
        _historyIndex = _history.Count;
        return "";
    }
}