namespace GHSMarkdownEditor.Models;

/// <summary>Supported document export formats.</summary>
public enum ExportFormat
{
    /// <summary>Print-ready PDF rendered via WebView2 / Chromium.</summary>
    Pdf,

    /// <summary>Microsoft Word Open XML document (.docx).</summary>
    Docx,

    /// <summary>Self-contained HTML file with embedded CSS identical to the live preview.</summary>
    Html,

    /// <summary>HTML body markup only — no wrapper tags or stylesheet.</summary>
    HtmlClean,

    /// <summary>Plain text with all Markdown formatting stripped.</summary>
    PlainText
}

/// <summary>
/// Static metadata lookup for each <see cref="ExportFormat"/> value.
/// Provides display names, descriptions, file extensions, and SaveFileDialog filter strings.
/// </summary>
public static class ExportFormatInfo
{
    private static readonly Dictionary<ExportFormat, (string DisplayName, string Description, string Extension, string Filter)> Lookup
        = new()
        {
            [ExportFormat.Pdf]       = ("PDF Document",     "Print-ready PDF with full preview styling (A4, 20 mm margins)",  ".pdf",  "PDF Files (*.pdf)|*.pdf"),
            [ExportFormat.Docx]      = ("Word Document",    "Microsoft Word compatible (.docx) with heading and list styles", ".docx", "Word Documents (*.docx)|*.docx"),
            [ExportFormat.Html]      = ("HTML (styled)",    "Self-contained HTML with embedded CSS — identical to the preview", ".html", "HTML Files (*.html)|*.html"),
            [ExportFormat.HtmlClean] = ("HTML (clean)",     "HTML body markup only — no <html>, <head>, or stylesheet",       ".html", "HTML Files (*.html)|*.html"),
            [ExportFormat.PlainText] = ("Plain Text",       "Plain text with all Markdown formatting removed",                ".txt",  "Text Files (*.txt)|*.txt"),
        };

    /// <summary>Returns the user-facing format name (e.g. "PDF Document").</summary>
    public static string GetDisplayName(ExportFormat format) => Lookup[format].DisplayName;

    /// <summary>Returns the one-line description shown in the export dialog.</summary>
    public static string GetDescription(ExportFormat format) => Lookup[format].Description;

    /// <summary>Returns the default file extension including the leading dot (e.g. ".pdf").</summary>
    public static string GetExtension(ExportFormat format) => Lookup[format].Extension;

    /// <summary>Returns the <see cref="Microsoft.Win32.SaveFileDialog.Filter"/> string for this format.</summary>
    public static string GetFilter(ExportFormat format) => Lookup[format].Filter;
}
