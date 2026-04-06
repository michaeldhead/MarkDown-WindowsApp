using Markdig;

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

    /// <summary>
    /// Renders <paramref name="markdown"/> to a complete HTML page with inline CSS.
    /// The CSS is embedded directly (not linked) so <c>NavigateToString</c> can display
    /// it without a base URL or local file access — WebView2 does not resolve relative
    /// stylesheet links when content is loaded via <c>NavigateToString</c>.
    /// </summary>
    /// <param name="isDark">When true, uses the dark-mode colour palette.</param>
    public string ToHtml(string markdown, bool isDark = false)
    {
        var body = Markdig.Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
        var css = isDark ? DarkCss : LightCss;
        return $"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>{css}</style>
            </head>
            <body>
            {body}
            </body>
            </html>
            """;
    }
}
