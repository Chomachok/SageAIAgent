using Spectre.Console;

namespace Sage.CLI.UI.Components;

public class Footer
{
    public static async Task<T> RunWithStatusAsync<T>(string message, Func<Task<T>> action)
    {
        T result = default!;
        await AnsiConsole.Status()
            .Spinner(Spinner.Known.Dots)
            .SpinnerStyle(Style.Parse("cyan"))
            .StartAsync(message, async ctx =>
            {
                result = await action();
            });
        return result;
    }

    /// <summary>
    /// Service status message — light grey (brighter than dim).
    /// </summary>
    public static void ShowStatus(string message)
    {
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(message)}[/]");
    }

    public static void ShowError(string message)
    {
        AnsiConsole.MarkupLine($"[red]✗ {Markup.Escape(message)}[/]");
    }

    public static void ShowSuccess(string message)
    {
        AnsiConsole.MarkupLine($"[green]✓ {Markup.Escape(message)}[/]");
    }
}