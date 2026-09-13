using Spectre.Console;
using Spectre.Console.Rendering;

namespace Sage.CLI.UI.Components;

public class StatusBar(string model, string mode, string workingDir)
{
    public IRenderable Build()
    {
        var grid = new Grid()
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn().NoWrap())
            .AddColumn(new GridColumn());

        grid.AddRow(
            new Markup($"[grey]Model:[/] [cyan]{model}[/]"),
            new Markup($"[grey]Mode:[/] [yellow]{mode}[/]"),
            new Markup($"[grey]Dir:[/] [green]{workingDir}[/]")
        );

        return grid;
    }
}