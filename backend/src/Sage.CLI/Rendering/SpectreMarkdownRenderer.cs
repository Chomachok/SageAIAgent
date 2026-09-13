using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Spectre.Console;
using Spectre.Console.Rendering;
using MarkdownTable = Markdig.Extensions.Tables.Table;
using MarkdownTableRow = Markdig.Extensions.Tables.TableRow;

namespace Sage.CLI.Rendering;

public class SpectreMarkdownRenderer
{
    private readonly MarkdownPipeline _pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .UseGridTables()
        .Build();

    public void Render(string markdown)
    {
        var renderable = RenderToRenderable(markdown);
        AnsiConsole.Write(renderable);
        AnsiConsole.WriteLine();
    }

    public IRenderable RenderToRenderable(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return new Text("");

        var document = Markdown.Parse(markdown, _pipeline);
        var renderables = Render(document);
        return renderables.Count == 0 ? new Text("") : new Rows(renderables);
    }

    private List<IRenderable> Render(MarkdownObject node)
    {
        var result = new List<IRenderable>();

        // 🔑 ВАЖНО: сначала проверяем более специфичные типы (HeadingBlock, CodeBlock),
        // потому что они наследуются от LeafBlock.
        if (node is HeadingBlock heading)
        {
            result.Add(RenderHeading(heading));
        }
        else if (node is CodeBlock code)
        {
            result.Add(RenderCodeBlock(code));
        }
        else if (node is MarkdownTable table)
        {
            result.Add(RenderTable(table));
        }
        else if (node is QuoteBlock quote)
        {
            result.Add(RenderQuote(quote));
        }
        else if (node is ListBlock list)
        {
            result.Add(RenderList(list));
        }
        else if (node is ThematicBreakBlock)
        {
            result.Add(new Text("──────────────────────────────────────"));
        }
        else if (node is ContainerBlock container)
        {
            foreach (var child in container)
                result.AddRange(Render(child));
        }
        else if (node is LeafBlock leaf && leaf.Inline != null)
        {
            // Обычный параграф: собираем весь инлайн в одну строку
            var markup = ProcessInlineToMarkup(leaf.Inline);
            if (!string.IsNullOrWhiteSpace(markup))
                result.Add(new Markup(markup));
        }
        else if (node is Inline inline)
        {
            var markup = ProcessInlineToMarkup(inline);
            if (!string.IsNullOrWhiteSpace(markup))
                result.Add(new Markup(markup));
        }
        else
        {
            result.Add(new Text(node.ToString() ?? ""));
        }

        return result;
    }

    /// <summary>
    /// Преобразует инлайн-дерево Markdig в строку Spectre markup.
    /// </summary>
    private string ProcessInlineToMarkup(Inline inline)
    {
        var sb = new StringBuilder();
        BuildMarkup(inline, sb);
        return sb.ToString().TrimEnd();
    }

    private void BuildMarkup(Inline inline, StringBuilder sb)
    {
        if (inline == null) return;

        // 🔑 ВАЖНО: специфичные типы (EmphasisInline, LinkInline) проверяем
        // ДО ContainerInline, потому что они от него наследуются.
        switch (inline)
        {
            case EmphasisInline emphasis:
            {
                var dec = emphasis.DelimiterCount == 1 ? "italic" : "bold";
                sb.Append($"[{dec}]");
                foreach (var child in emphasis)
                    BuildMarkup(child, sb);
                sb.Append("[/]");
                break;
            }
            case LinkInline link:
            {
                var linkText = link.FirstChild?.ToString() ?? link.Url ?? "";
                sb.Append($"[link={Markup.Escape(link.Url ?? "")}]{Markup.Escape(linkText)}[/]");
                break;
            }
            case CodeInline code:
                sb.Append($"[italic cyan]{Markup.Escape(code.Content)}[/]");
                break;
            case LiteralInline literal:
                sb.Append(Markup.Escape(literal.Content.ToString()));
                break;
            case LineBreakInline:
                sb.Append('\n');
                break;
            case HtmlInline:
                // игнорируем
                break;
            case ContainerInline container:
                foreach (var child in container)
                    BuildMarkup(child, sb);
                break;
            default:
                sb.Append(Markup.Escape(inline.ToString() ?? ""));
                break;
        }
    }

    private IRenderable RenderHeading(HeadingBlock heading)
    {
        // Рендерим инлайн содержимое заголовка (чтобы поддержать **bold** и т.д.)
        var content = heading.Inline != null
            ? ProcessInlineToMarkup(heading.Inline)
            : Markup.Escape(heading.ToString() ?? "");

        return heading.Level switch
        {
            1 => new Rows(new IRenderable[]
            {
                new Markup($"[bold yellow]{content}[/]"),
                new Markup("[grey]──────────────────────────────────────[/]")
            }),
            2 => new Rows(new IRenderable[]
            {
                new Markup($"[bold cyan]{content}[/]"),
                new Markup("[grey]──────────────────────────[/]")
            }),
            _ => new Markup($"[bold]{content}[/]")
        };
    }

    private IRenderable RenderList(ListBlock list)
    {
        var items = new List<IRenderable>();
        int index = 1;
        var bullet = list.BulletType == '1' ? null : "•";

        foreach (var item in list)
        {
            if (item is ListItemBlock li)
            {
                // Собираем содержимое элемента списка в одну markup-строку
                var innerMarkup = new StringBuilder();

                void Traverse(MarkdownObject node)
                {
                    if (node is ParagraphBlock para && para.Inline != null)
                    {
                        BuildMarkup(para.Inline, innerMarkup);
                    }
                    else if (node is ContainerBlock c)
                    {
                        foreach (var child in c)
                            Traverse(child);
                    }
                }

                Traverse(li);

                string marker = bullet ?? $"{index}.";
                items.Add(new Markup($"[green]{marker}[/] {innerMarkup}"));
                if (bullet == null) index++;
            }
        }

        return new Rows(items);
    }

    private IRenderable RenderQuote(QuoteBlock quote)
    {
        var sb = new StringBuilder();

        void Traverse(MarkdownObject node)
        {
            if (node is ParagraphBlock para && para.Inline != null)
            {
                BuildMarkup(para.Inline, sb);
            }
            else if (node is ContainerBlock c)
            {
                foreach (var child in c)
                    Traverse(child);
            }
        }

        Traverse(quote);
        return new Markup($"[grey]│[/] {sb}");
    }

    private IRenderable RenderCodeBlock(CodeBlock code)
    {
        var text = code.Lines.ToString();
        return new Panel(new Text(text))
            .Header(" code ", Justify.Center)
            .BorderColor(Color.Grey)
            .Border(BoxBorder.Rounded)
            .PadLeft(2)
            .PadRight(2);
    }

    private IRenderable RenderTable(MarkdownTable table)
    {
        var spectreTable = new Spectre.Console.Table();
        if (table.Count == 0) return new Text("");

        if (table[0] is MarkdownTableRow headerRow)
        {
            foreach (var cell in headerRow)
                spectreTable.AddColumn(new TableColumn(cell.ToString()).Centered());
        }

        for (int i = 1; i < table.Count; i++)
        {
            if (table[i] is MarkdownTableRow row)
            {
                var cells = row.Select(c => c.ToString()).ToArray();
                spectreTable.AddRow(cells);
            }
        }

        spectreTable.Border = TableBorder.Rounded;
        spectreTable.Expand = true;
        return spectreTable;
    }
}