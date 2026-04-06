using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using GHSMarkdownEditor.Models;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using MdTable     = Markdig.Extensions.Tables.Table;
using MdTableRow  = Markdig.Extensions.Tables.TableRow;
using MdTableCell = Markdig.Extensions.Tables.TableCell;
using OxStyle     = DocumentFormat.OpenXml.Wordprocessing.Style;
using OxColor     = DocumentFormat.OpenXml.Wordprocessing.Color;

namespace GHSMarkdownEditor.Services;

/// <summary>
/// Exports the active document to PDF, DOCX, HTML, or plain text.
/// </summary>
/// <remarks>
/// PDF uses WebView2 <c>PrintToPdfAsync</c> (Chromium engine) — identical output to the
/// live preview, A4 page size, 20 mm margins, no additional NuGet package required.
/// DOCX uses <c>DocumentFormat.OpenXml</c> 3.x; maps the Markdig AST to Word paragraph
/// styles, character runs, numbering definitions, and tables.
/// HTML and plain-text exports use Markdig directly.
/// </remarks>
public sealed class ExportService
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Dispatches to the correct export implementation based on <paramref name="format"/>.
    /// PDF must be called from the UI thread (creates a temporary WebView2 window).
    /// </summary>
    public Task ExportAsync(ExportFormat format, string markdown, string outputPath) =>
        format switch
        {
            ExportFormat.Pdf       => ExportPdfAsync(markdown, outputPath),
            ExportFormat.Docx      => Task.Run(() => ExportDocxCore(markdown, outputPath)),
            ExportFormat.Html      => Task.Run(() => File.WriteAllText(outputPath,
                                          App.MarkdownService.ToHtml(markdown, isDark: false))),
            ExportFormat.HtmlClean => Task.Run(() => File.WriteAllText(outputPath,
                                          Markdig.Markdown.ToHtml(markdown, Pipeline))),
            ExportFormat.PlainText => Task.Run(() => File.WriteAllText(outputPath,
                                          ToPlainText(markdown))),
            _                      => throw new ArgumentOutOfRangeException(nameof(format))
        };

    // ── PDF ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Renders Markdown to HTML (light theme) and saves a PDF using WebView2's built-in
    /// Chromium PDF renderer.  A4 page size with 20 mm margins.  Must be called on the UI thread.
    /// </summary>
    private static async Task ExportPdfAsync(string markdown, string outputPath)
    {
        var html = App.MarkdownService.ToHtml(markdown, isDark: false);

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Create an off-screen 1×1 window — WebView2 requires a live HWND to render content.
        var win = new Window
        {
            Width          = 1,
            Height         = 1,
            Left           = -32000,
            Top            = -32000,
            ShowInTaskbar  = false,
            WindowStyle    = WindowStyle.None,
            ResizeMode     = ResizeMode.NoResize,
            AllowsTransparency = false
        };

        var webView = new WebView2();
        win.Content = webView;
        win.Closed += (_, _) => tcs.TrySetCanceled();
        win.Show();

        try
        {
            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled            = false;

            bool handled = false;
            webView.NavigationCompleted += async (_, _) =>
            {
                if (handled) return;
                handled = true;

                // A4 in inches: 8.27 × 11.69.  20 mm margins = 0.787 in.
                var settings = webView.CoreWebView2.Environment.CreatePrintSettings();
                settings.PageWidth          = 8.27;
                settings.PageHeight         = 11.69;
                settings.MarginTop          = 0.787;
                settings.MarginBottom       = 0.787;
                settings.MarginLeft         = 0.787;
                settings.MarginRight        = 0.787;
                settings.ShouldPrintBackgrounds      = false;
                settings.ShouldPrintHeaderAndFooter  = false;

                await webView.CoreWebView2.PrintToPdfAsync(outputPath, settings);
                tcs.TrySetResult(true);
            };

            webView.NavigateToString(html);
            await tcs.Task;
        }
        finally
        {
            try { win.Close(); } catch { /* best effort */ }
        }
    }

    // ── DOCX ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Core DOCX writer.  Parses the Markdig AST and maps each block to the appropriate
    /// Word paragraph style, character formatting, list numbering, or table structure.
    /// </summary>
    private static void ExportDocxCore(string markdown, string outputPath)
    {
        var mdDoc = Markdig.Markdown.Parse(markdown, Pipeline);

        using var wordDoc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
        var mainPart = wordDoc.AddMainDocumentPart();
        mainPart.Document = new Document();
        var body = mainPart.Document.AppendChild(new Body());

        var stylesPart = mainPart.AddNewPart<StyleDefinitionsPart>();
        stylesPart.Styles = BuildDocxStyles();

        var numberingPart = mainPart.AddNewPart<NumberingDefinitionsPart>();
        numberingPart.Numbering = BuildDocxNumbering();

        foreach (var block in mdDoc)
            DocxAppendBlock(body, block);

        // Final section properties: A4 page size
        body.AppendChild(new SectionProperties(
            new PageSize { Width = 11906, Height = 16838 }, // A4 in twentieths of a point
            new PageMargin { Top = 1134, Bottom = 1134, Left = 1134, Right = 1134 } // 20mm ≈ 1134 twips
        ));

        mainPart.Document.Save();
    }

    // ── DOCX: Styles ──────────────────────────────────────────────────────────

    private static Styles BuildDocxStyles()
    {
        var styles = new Styles();
        styles.Append(BuildDocxNormalStyle());
        styles.Append(BuildDocxHeadingStyle("Heading1", "heading 1", 36, spaceBefore: 480));
        styles.Append(BuildDocxHeadingStyle("Heading2", "heading 2", 32, spaceBefore: 360, bottomBorder: true));
        styles.Append(BuildDocxHeadingStyle("Heading3", "heading 3", 28, spaceBefore: 240));
        styles.Append(BuildDocxHeadingStyle("Heading4", "heading 4", 26, spaceBefore: 240));
        styles.Append(BuildDocxCodeStyle());
        return styles;
    }

    private static OxStyle BuildDocxNormalStyle()
    {
        var style = new OxStyle { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true };
        style.Append(new Name { Val = "Normal" });
        var pPr = new StyleParagraphProperties();
        pPr.Append(new SpacingBetweenLines { After = "160" });
        style.Append(pPr);
        var rPr = new StyleRunProperties();
        rPr.Append(new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri" });
        rPr.Append(new FontSize { Val = "22" }); // 11pt
        style.Append(rPr);
        return style;
    }

    private static OxStyle BuildDocxHeadingStyle(string styleId, string styleName,
        int halfPointSize, int spaceBefore = 240, bool bottomBorder = false)
    {
        var style = new OxStyle { Type = StyleValues.Paragraph, StyleId = styleId };
        style.Append(new Name { Val = styleName });
        style.Append(new BasedOn { Val = "Normal" });
        style.Append(new NextParagraphStyle { Val = "Normal" });

        var pPr = new StyleParagraphProperties();
        pPr.Append(new KeepNext());
        pPr.Append(new KeepLines());
        pPr.Append(new SpacingBetweenLines { Before = spaceBefore.ToString(), After = "80" });
        if (bottomBorder)
        {
            pPr.Append(new ParagraphBorders(
                new BottomBorder { Val = BorderValues.Single, Size = 4, Space = 1, Color = "AAAAAA" }
            ));
        }
        style.Append(pPr);

        var rPr = new StyleRunProperties();
        rPr.Append(new Bold());
        rPr.Append(new OxColor { Val = "1F3864" });
        rPr.Append(new FontSize { Val = halfPointSize.ToString() });
        style.Append(rPr);
        return style;
    }

    private static OxStyle BuildDocxCodeStyle()
    {
        var style = new OxStyle { Type = StyleValues.Paragraph, StyleId = "Code" };
        style.Append(new Name { Val = "Code" });
        style.Append(new BasedOn { Val = "Normal" });

        var pPr = new StyleParagraphProperties();
        pPr.Append(new SpacingBetweenLines { Before = "60", After = "60" });
        pPr.Append(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "F2F4F7" });
        pPr.Append(new Indentation { Left = "284", Right = "284" });
        style.Append(pPr);

        var rPr = new StyleRunProperties();
        rPr.Append(new RunFonts { Ascii = "Courier New", HighAnsi = "Courier New" });
        rPr.Append(new FontSize { Val = "20" }); // 10pt
        style.Append(rPr);
        return style;
    }

    // ── DOCX: Numbering ───────────────────────────────────────────────────────

    private static Numbering BuildDocxNumbering()
    {
        // Abstract 0 — bullet list
        var abstractBullet = new AbstractNum { AbstractNumberId = 0 };
        abstractBullet.Append(new MultiLevelType { Val = MultiLevelValues.Multilevel });
        for (int i = 0; i <= 8; i++)
        {
            var lvl = new Level { LevelIndex = i };
            lvl.Append(new StartNumberingValue { Val = 1 });
            lvl.Append(new NumberingFormat { Val = NumberFormatValues.Bullet });
            lvl.Append(new LevelText { Val = "•" });
            lvl.Append(new LevelJustification { Val = LevelJustificationValues.Left });
            lvl.Append(new PreviousParagraphProperties(
                new Indentation { Left = (720 + i * 360).ToString(), Hanging = "360" }));
            abstractBullet.Append(lvl);
        }

        // Abstract 1 — numbered list
        var abstractNumbered = new AbstractNum { AbstractNumberId = 1 };
        abstractNumbered.Append(new MultiLevelType { Val = MultiLevelValues.Multilevel });
        for (int i = 0; i <= 8; i++)
        {
            var lvl = new Level { LevelIndex = i };
            lvl.Append(new StartNumberingValue { Val = 1 });
            lvl.Append(new NumberingFormat { Val = NumberFormatValues.Decimal });
            lvl.Append(new LevelText { Val = $"%{i + 1}." });
            lvl.Append(new LevelJustification { Val = LevelJustificationValues.Left });
            lvl.Append(new PreviousParagraphProperties(
                new Indentation { Left = (720 + i * 360).ToString(), Hanging = "360" }));
            abstractNumbered.Append(lvl);
        }

        // Concrete instance 1 → bullet, instance 2 → numbered
        var bulletInstance = new NumberingInstance { NumberID = 1 };
        bulletInstance.Append(new AbstractNumId { Val = 0 });

        var numberedInstance = new NumberingInstance { NumberID = 2 };
        numberedInstance.Append(new AbstractNumId { Val = 1 });

        return new Numbering(abstractBullet, abstractNumbered, bulletInstance, numberedInstance);
    }

    // ── DOCX: Block processing ────────────────────────────────────────────────

    private static void DocxAppendBlock(Body body, Block block)
    {
        switch (block)
        {
            case HeadingBlock h:
                body.Append(DocxBuildHeading(h));
                break;

            case ParagraphBlock p:
                body.Append(DocxBuildParagraph(p));
                break;

            case FencedCodeBlock fenced:
                body.Append(DocxBuildCodeBlock(fenced.Lines.ToString()));
                break;

            case CodeBlock code:
                body.Append(DocxBuildCodeBlock(code.Lines.ToString()));
                break;

            case ListBlock list:
                DocxAppendList(body, list, level: 0);
                break;

            case ThematicBreakBlock:
                body.Append(DocxBuildThematicBreak());
                break;

            case MdTable table:
                body.Append(DocxBuildTable(table));
                break;

            case QuoteBlock quote:
                // Render blockquote children with indentation
                foreach (var child in quote)
                    DocxAppendBlock(body, child);
                break;

            case ContainerBlock container:
                foreach (var child in container)
                    DocxAppendBlock(body, child);
                break;
        }
    }

    // ── DOCX: Individual block builders ──────────────────────────────────────

    private static Paragraph DocxBuildHeading(HeadingBlock h)
    {
        var styleId = h.Level switch
        {
            1 => "Heading1",
            2 => "Heading2",
            3 => "Heading3",
            _ => "Heading4"
        };

        var para = new Paragraph();
        para.Append(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));
        DocxAppendInlines(para, h.Inline);
        return para;
    }

    private static Paragraph DocxBuildParagraph(ParagraphBlock p)
    {
        var para = new Paragraph();
        para.Append(new ParagraphProperties(new ParagraphStyleId { Val = "Normal" }));
        DocxAppendInlines(para, p.Inline);
        return para;
    }

    private static Paragraph DocxBuildCodeBlock(string code)
    {
        var para = new Paragraph();
        var pPr = new ParagraphProperties(new ParagraphStyleId { Val = "Code" });
        para.Append(pPr);

        // Split into lines and create a run per line with soft breaks between them.
        var lines = code.TrimEnd('\n', '\r').Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var run = new Run();
            run.Append(DocxCodeRunProperties());
            run.Append(new Text(lines[i]) { Space = SpaceProcessingModeValues.Preserve });
            para.Append(run);

            if (i < lines.Length - 1)
                para.Append(new Run(new Break()));
        }

        return para;
    }

    private static Paragraph DocxBuildThematicBreak()
    {
        var para = new Paragraph();
        var pPr = new ParagraphProperties();
        pPr.Append(new ParagraphBorders(
            new BottomBorder { Val = BorderValues.Single, Size = 6, Space = 1, Color = "999999" }
        ));
        para.Append(pPr);
        return para;
    }

    private static void DocxAppendList(Body body, ListBlock list, int level)
    {
        int numberingId = list.IsOrdered ? 2 : 1;

        foreach (var item in list.OfType<ListItemBlock>())
        {
            foreach (var subBlock in item)
            {
                if (subBlock is ParagraphBlock para)
                {
                    var p = new Paragraph();
                    var pPr = new ParagraphProperties();
                    pPr.Append(new NumberingProperties(
                        new NumberingLevelReference { Val = level },
                        new NumberingId { Val = numberingId }
                    ));
                    p.Append(pPr);
                    DocxAppendInlines(p, para.Inline);
                    body.Append(p);
                }
                else if (subBlock is ListBlock nested)
                {
                    DocxAppendList(body, nested, level + 1);
                }
            }
        }
    }

    private static Table DocxBuildTable(MdTable mdTable)
    {
        var table = new Table();

        // Table-wide borders
        var tblProps = new TableProperties(
            new TableBorders(
                new TopBorder    { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 },
                new LeftBorder   { Val = BorderValues.Single, Size = 4 },
                new RightBorder  { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                new InsideVerticalBorder   { Val = BorderValues.Single, Size = 4 }
            ),
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct } // 100%
        );
        table.Append(tblProps);

        foreach (var mdRow in mdTable.OfType<MdTableRow>())
        {
            var row = new TableRow();
            bool isHeader = mdRow.IsHeader;

            foreach (var mdCell in mdRow.OfType<MdTableCell>())
            {
                var cell = new TableCell();
                var tcPr = new TableCellProperties(
                    new TableCellWidth { Type = TableWidthUnitValues.Auto }
                );
                if (isHeader)
                    tcPr.Append(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "E8EDF2" });
                cell.Append(tcPr);

                var cellPara = new Paragraph();
                if (isHeader)
                {
                    var pPr = new ParagraphProperties();
                    var rPr = new ParagraphMarkRunProperties(new Bold());
                    pPr.Append(rPr);
                    cellPara.Append(pPr);
                }

                foreach (var cellBlock in mdCell)
                {
                    if (cellBlock is ParagraphBlock pb)
                        DocxAppendInlines(cellPara, pb.Inline, bold: isHeader);
                }

                cell.Append(cellPara);
                row.Append(cell);
            }

            table.Append(row);
        }

        return table;
    }

    // ── DOCX: Inline processing ───────────────────────────────────────────────

    /// <summary>
    /// Recursively walks <paramref name="inlines"/> and appends <see cref="Run"/> elements
    /// to <paramref name="para"/> with accumulated bold/italic/code formatting.
    /// </summary>
    private static void DocxAppendInlines(
        Paragraph para,
        ContainerInline? inlines,
        bool bold   = false,
        bool italic = false,
        bool code   = false)
    {
        if (inlines == null) return;

        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    para.Append(DocxBuildRun(lit.Content.ToString(), bold, italic, code));
                    break;

                case EmphasisInline em:
                    bool isBold   = em.DelimiterCount >= 2;
                    bool isItalic = em.DelimiterCount == 1 || em.DelimiterCount == 3;
                    DocxAppendInlines(para, em,
                        bold   || isBold,
                        italic || isItalic,
                        code);
                    break;

                case CodeInline ci:
                    para.Append(DocxBuildRun(ci.Content, bold, italic, isCode: true));
                    break;

                case LineBreakInline lb:
                    if (lb.IsHard)
                        para.Append(new Run(new Break()));
                    else
                        para.Append(DocxBuildRun(" ", bold, italic, code));
                    break;

                case LinkInline link:
                    // Render as underlined text; preserve the URL as a subtitle run
                    DocxAppendInlines(para, link, bold, italic, code);
                    if (!string.IsNullOrEmpty(link.Url))
                    {
                        var urlRun = DocxBuildRun($" ({link.Url})", bold: false, italic: true, isCode: false);
                        para.Append(urlRun);
                    }
                    break;

                case ContainerInline container:
                    DocxAppendInlines(para, container, bold, italic, code);
                    break;
            }
        }
    }

    private static Run DocxBuildRun(string text, bool bold, bool italic, bool isCode)
    {
        var run = new Run();
        var rPr = new RunProperties();

        if (isCode)
        {
            rPr.Append(DocxCodeRunProperties());
        }
        else
        {
            if (bold)   rPr.Append(new Bold());
            if (italic) rPr.Append(new Italic());
        }

        run.Append(rPr);
        run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static RunProperties DocxCodeRunProperties()
    {
        var rPr = new RunProperties();
        rPr.Append(new RunFonts { Ascii = "Courier New", HighAnsi = "Courier New" });
        rPr.Append(new FontSize { Val = "20" }); // 10pt
        return rPr;
    }

    // ── Plain Text ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Strips Markdown formatting by converting to HTML then removing all HTML tags,
    /// decoding common HTML entities, and normalising whitespace.
    /// </summary>
    private static string ToPlainText(string markdown)
    {
        var html = Markdig.Markdown.ToHtml(markdown, Pipeline);
        // Remove HTML tags
        var text = Regex.Replace(html, @"<[^>]+>", string.Empty);
        // Decode common entities
        text = text.Replace("&amp;", "&")
                   .Replace("&lt;",  "<")
                   .Replace("&gt;",  ">")
                   .Replace("&quot;", "\"")
                   .Replace("&#39;", "'")
                   .Replace("&nbsp;", " ");
        // Collapse runs of 3+ blank lines to 2
        text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
        return text;
    }
}
