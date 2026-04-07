using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using System.IO;

namespace GHSMarkdownEditor.Services;

/// <summary>
/// Converts Markdown text to a self-contained HTML page ready for display in WebView2.
/// The Markdig pipeline is built once and reused across all calls because pipeline
/// construction is relatively expensive and the pipeline itself is thread-safe once built.
/// </summary>
public class MarkdownService
{
    /// <summary>
    /// Shared Markdig pipeline with the full advanced extension set (tables, task lists,
    /// auto-links, etc.). Built once at class initialisation and reused for every render.
    /// </summary>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private const string LightCss = """
        body {
            font-family: 'Segoe UI', Arial, sans-serif;
            font-size: 15px;
            line-height: 1.7;
            max-width: 900px;
            margin: 0 auto;
            padding: 20px 32px;
            color: #24292e;
            background-color: #ffffff;
        }
        h1, h2, h3, h4, h5, h6 {
            margin-top: 24px;
            margin-bottom: 16px;
            font-weight: 600;
            line-height: 1.25;
        }
        h1 { font-size: 2em; border-bottom: 1px solid #eaecef; padding-bottom: .3em; }
        h2 { font-size: 1.5em; border-bottom: 1px solid #eaecef; padding-bottom: .3em; }
        h3 { font-size: 1.25em; }
        code {
            font-family: 'Cascadia Code', Consolas, monospace;
            font-size: 0.875em;
            background-color: #f6f8fa;
            padding: 0.2em 0.4em;
            border-radius: 3px;
        }
        pre {
            background-color: #f6f8fa;
            border-radius: 6px;
            padding: 16px;
            overflow-x: auto;
            line-height: 1.45;
        }
        pre code {
            background-color: transparent;
            padding: 0;
            border-radius: 0;
            font-size: 0.875em;
        }
        blockquote {
            margin: 0 0 16px 0;
            padding: 0 1em;
            color: #6a737d;
            border-left: 0.25em solid #dfe2e5;
        }
        table {
            border-collapse: collapse;
            width: 100%;
            margin: 16px 0;
        }
        table th, table td {
            border: 1px solid #dfe2e5;
            padding: 6px 13px;
            text-align: left;
        }
        table th { font-weight: 600; background-color: #f6f8fa; }
        table tr:nth-child(2n) { background-color: #f6f8fa; }
        a { color: #0366d6; text-decoration: none; }
        a:hover { text-decoration: underline; }
        img { max-width: 100%; height: auto; }
        hr { height: 0.25em; padding: 0; margin: 24px 0; background-color: #e1e4e8; border: 0; }
        ul, ol { padding-left: 2em; margin: 0 0 16px 0; }
        li { margin-top: 4px; }
        input[type="checkbox"] { margin-right: 6px; }
        """;

    private const string DarkCss = """
        body {
            font-family: 'Segoe UI', Arial, sans-serif;
            font-size: 15px;
            line-height: 1.7;
            max-width: 900px;
            margin: 0 auto;
            padding: 20px 32px;
            color: #c9d1d9;
            background-color: #1e1e1e;
        }
        h1, h2, h3, h4, h5, h6 {
            margin-top: 24px;
            margin-bottom: 16px;
            font-weight: 600;
            line-height: 1.25;
            color: #e6edf3;
        }
        h1 { font-size: 2em; border-bottom: 1px solid #21262d; padding-bottom: .3em; }
        h2 { font-size: 1.5em; border-bottom: 1px solid #21262d; padding-bottom: .3em; }
        h3 { font-size: 1.25em; }
        code {
            font-family: 'Cascadia Code', Consolas, monospace;
            font-size: 0.875em;
            background-color: #161b22;
            padding: 0.2em 0.4em;
            border-radius: 3px;
        }
        pre {
            background-color: #161b22;
            border-radius: 6px;
            padding: 16px;
            overflow-x: auto;
            line-height: 1.45;
        }
        pre code {
            background-color: transparent;
            padding: 0;
            border-radius: 0;
            font-size: 0.875em;
        }
        blockquote {
            margin: 0 0 16px 0;
            padding: 0 1em;
            color: #8b949e;
            border-left: 0.25em solid #3d444d;
        }
        table {
            border-collapse: collapse;
            width: 100%;
            margin: 16px 0;
        }
        table th, table td {
            border: 1px solid #3d444d;
            padding: 6px 13px;
            text-align: left;
        }
        table th { font-weight: 600; background-color: #161b22; }
        table tr:nth-child(2n) { background-color: #161b22; }
        a { color: #58a6ff; text-decoration: none; }
        a:hover { text-decoration: underline; }
        img { max-width: 100%; height: auto; }
        hr { height: 0.25em; padding: 0; margin: 24px 0; background-color: #21262d; border: 0; }
        ul, ol { padding-left: 2em; margin: 0 0 16px 0; }
        li { margin-top: 4px; }
        input[type="checkbox"] { margin-right: 6px; }
        """;

    // Appended to both themes. Provides the visible outline that SplitView's cursor-sync
    // feature activates by adding the 'active-block' class via ExecuteScriptAsync.
    // Also provides styles for the inline-edit overlay that appears on double-click.
    private const string ActiveBlockCss = """

        .active-block {
            outline: 2px solid #1976D2;
            outline-offset: 2px;
            border-radius: 3px;
        }
        .inline-edit-overlay {
            position: relative;
            z-index: 100;
        }
        .inline-edit-overlay textarea {
            width: 100%;
            min-height: 80px;
            font-family: monospace;
            font-size: 13px;
            padding: 8px;
            border: 2px solid #1976D2;
            border-radius: 4px;
            background: #1e1e1e;
            color: #d4d4d4;
            resize: vertical;
            box-sizing: border-box;
        }
        .inline-edit-actions {
            display: flex;
            gap: 8px;
            margin-top: 6px;
            justify-content: flex-end;
        }
        .inline-edit-actions button {
            padding: 4px 16px;
            border-radius: 4px;
            border: none;
            cursor: pointer;
            font-size: 13px;
        }
        .inline-edit-save {
            background: #1976D2;
            color: white;
        }
        .inline-edit-cancel {
            background: #444;
            color: #ccc;
        }
        """;

    /// <summary>
    /// Renders <paramref name="markdown"/> to a complete HTML page with inline CSS.
    /// Parses to a Markdig AST first, stamps every block with a <c>data-source-line</c>
    /// attribute (1-based, matching AvalonEdit's line numbers), then renders to HTML.
    /// This allows the preview pane to highlight the block that corresponds to the
    /// editor cursor position without a separate server round-trip.
    /// The CSS is embedded directly (not linked) so <c>NavigateToString</c> can display
    /// it without a base URL — WebView2 does not resolve relative links via NavigateToString.
    /// </summary>
    /// <param name="isDark">When true, uses the dark-mode colour palette.</param>
    public string ToHtml(string markdown, bool isDark = false)
    {
        var document = Markdig.Markdown.Parse(markdown ?? string.Empty, Pipeline);

        // Walk the AST and stamp every block with data-source-line so the JavaScript
        // highlight logic can find the element closest to the editor cursor position.
        // Markdig line numbers are 0-based; add 1 to match AvalonEdit's 1-based lines.
        InjectSourceLines(document);

        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        var body = writer.ToString();

        var css = isDark ? DarkCss : LightCss;
        return $"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>{css}{ActiveBlockCss}</style>
            </head>
            <body>
            {body}
            </body>
            </html>
            """;
    }

    /// <summary>
    /// Recursively walks <paramref name="container"/> and adds a <c>data-source-line</c>
    /// HTML attribute to every block. Markdig's standard renderers honour
    /// <see cref="HtmlAttributes"/> by calling <c>WriteAttributes</c> when opening each
    /// block tag, so no custom renderer is needed.
    /// </summary>
    private static void InjectSourceLines(ContainerBlock container)
    {
        foreach (var block in container)
        {
            var attrs = block.TryGetAttributes() ?? new HtmlAttributes();
            if (attrs.Properties == null)
                attrs.Properties = new List<KeyValuePair<string, string?>>();
            attrs.Properties.Add(
                new KeyValuePair<string, string?>("data-source-line", (block.Line + 1).ToString()));
            block.SetAttributes(attrs);

            if (block is ContainerBlock child)
                InjectSourceLines(child);
        }
    }
}
