using AvalonEditB;
using System.Text.RegularExpressions;

namespace GHSMarkdownEditor.Services;

/// <summary>Identifies the formatting action to apply via <see cref="FormattingService.ApplyFormatting"/>.</summary>
public enum FormattingAction
{
    Bold,
    Italic,
    Strikethrough,
    InlineCode,
    H1, H2, H3, H4,
    BulletList,
    NumberedList,
    CodeBlock,
    Table,
    HorizontalRule,
    Link
}

/// <summary>
/// Applies Markdown formatting operations directly to an AvalonEdit <see cref="TextEditor"/>.
/// All mutations go through the editor's <see cref="AvalonEditB.Document.TextDocument"/> so
/// they participate in undo/redo history automatically.
/// </summary>
public class FormattingService
{
    /// <summary>
    /// Dispatches the requested <paramref name="action"/> to the appropriate helper and
    /// then returns focus to the editor so the user can keep typing without clicking.
    /// Inline actions (Bold, Italic, etc.) wrap selected text when there is a selection,
    /// or insert markers with the caret placed between them when there is no selection.
    /// Block actions (headings, lists, code blocks) operate on whole lines.
    /// </summary>
    public void ApplyFormatting(TextEditor editor, FormattingAction action)
    {
        switch (action)
        {
            case FormattingAction.Bold:           ApplyInlineWrap(editor, "**", "**"); break;
            case FormattingAction.Italic:         ApplyInlineWrap(editor, "*", "*"); break;
            case FormattingAction.Strikethrough:  ApplyInlineWrap(editor, "~~", "~~"); break;
            case FormattingAction.InlineCode:     ApplyInlineWrap(editor, "`", "`"); break;
            case FormattingAction.H1:             ApplyHeading(editor, 1); break;
            case FormattingAction.H2:             ApplyHeading(editor, 2); break;
            case FormattingAction.H3:             ApplyHeading(editor, 3); break;
            case FormattingAction.H4:             ApplyHeading(editor, 4); break;
            case FormattingAction.BulletList:     ApplyLinePrefix(editor, "- ", @"^- "); break;
            case FormattingAction.NumberedList:   ApplyLinePrefix(editor, "1. ", @"^\d+\. "); break;
            case FormattingAction.CodeBlock:      InsertCodeBlock(editor); break;
            case FormattingAction.Table:          InsertTable(editor); break;
            case FormattingAction.HorizontalRule: InsertHorizontalRule(editor); break;
            case FormattingAction.Link:           InsertLink(editor); break;
        }
        editor.Focus();
    }

    /// <summary>
    /// Wraps the current selection with <paramref name="open"/>/<paramref name="close"/> markers,
    /// or — when nothing is selected — inserts the pair and places the caret between them
    /// so the user can immediately type the content.
    /// </summary>
    private static void ApplyInlineWrap(TextEditor editor, string open, string close)
    {
        var doc = editor.Document;
        if (editor.SelectionLength > 0)
        {
            var start = editor.SelectionStart;
            var len = editor.SelectionLength;
            var text = editor.SelectedText;
            doc.Replace(start, len, open + text + close);
            editor.TextArea.Caret.Offset = start + open.Length + text.Length + close.Length;
        }
        else
        {
            var offset = editor.TextArea.Caret.Offset;
            doc.Insert(offset, open + close);
            editor.TextArea.Caret.Offset = offset + open.Length;
        }
    }

    /// <summary>
    /// Replaces any existing ATX heading markers on the current line and applies the
    /// requested <paramref name="level"/>. Idempotent: re-applying the same level is a no-op.
    /// </summary>
    private static void ApplyHeading(TextEditor editor, int level)
    {
        var prefix = new string('#', level) + " ";
        var doc = editor.Document;
        var caretOffset = editor.TextArea.Caret.Offset;
        var line = doc.GetLineByOffset(caretOffset);
        var lineText = doc.GetText(line.Offset, line.Length);

        // Strip any existing heading markers
        var stripped = Regex.Replace(lineText, @"^#{1,6} ?", "");
        var newText = prefix + stripped;

        if (lineText == newText) return; // already at this exact level

        doc.Replace(line.Offset, line.Length, newText);
        editor.TextArea.Caret.Offset = line.Offset + newText.Length;
    }

    /// <summary>
    /// Prepends <paramref name="prefix"/> to the current line if it does not already match
    /// <paramref name="alreadyPattern"/>. Idempotent: a line that already starts with the
    /// prefix is left unchanged.
    /// </summary>
    private static void ApplyLinePrefix(TextEditor editor, string prefix, string alreadyPattern)
    {
        var doc = editor.Document;
        var caretOffset = editor.TextArea.Caret.Offset;
        var line = doc.GetLineByOffset(caretOffset);
        var lineText = doc.GetText(line.Offset, line.Length);

        if (Regex.IsMatch(lineText, alreadyPattern)) return;

        doc.Insert(line.Offset, prefix);
        editor.TextArea.Caret.Offset = caretOffset + prefix.Length;
    }

    /// <summary>
    /// Inserts a fenced code block using a language chosen via the language picker dialog.
    /// "plaintext" is treated as no language tag (bare triple-backtick fence).
    /// The caret is placed on the blank line between fences so the user can type code immediately.
    /// </summary>
    /// <param name="language">Language identifier from the picker, or "plaintext" for no tag.</param>
    public void ApplyCodeBlock(TextEditor editor, string language)
    {
        var doc = editor.Document;
        var offset = editor.TextArea.Caret.Offset;
        var line = doc.GetLineByOffset(offset);

        var pre = line.Length == 0 ? "" : "\n";
        var lang = string.Equals(language, "plaintext", StringComparison.OrdinalIgnoreCase) ? "" : language;
        var block = pre + $"```{lang}\n\n```\n";
        doc.Insert(offset, block);

        // Place cursor on the blank line between the fences (ready to type code)
        editor.TextArea.Caret.Offset = offset + pre.Length + 3 + lang.Length + 1;
        editor.Focus();
    }

    /// <summary>
    /// Legacy code block insert used by the Format menu (no language picker).
    /// Selects the "language" placeholder so the user can type a language immediately.
    /// </summary>
    private static void InsertCodeBlock(TextEditor editor)
    {
        var doc = editor.Document;
        var offset = editor.TextArea.Caret.Offset;
        var line = doc.GetLineByOffset(offset);

        var pre = line.Length == 0 ? "" : "\n";
        var block = pre + "```language\n\n```\n";
        doc.Insert(offset, block);

        // Select "language" so the user can immediately type the language name
        var langStart = offset + pre.Length + 3; // after "```"
        editor.Select(langStart, "language".Length);
    }

    private static void InsertTable(TextEditor editor)
    {
        var doc = editor.Document;
        var offset = editor.TextArea.Caret.Offset;
        var line = doc.GetLineByOffset(offset);

        var pre = line.Length == 0 ? "" : "\n";
        var table = pre +
            "| Header 1 | Header 2 | Header 3 |\n" +
            "| -------- | -------- | -------- |\n" +
            "| Cell     | Cell     | Cell     |\n";

        doc.Insert(offset, table);
        editor.TextArea.Caret.Offset = offset + table.Length;
    }

    private static void InsertHorizontalRule(TextEditor editor)
    {
        var doc = editor.Document;
        var offset = editor.TextArea.Caret.Offset;
        var line = doc.GetLineByOffset(offset);

        var pre = line.Length == 0 ? "" : "\n";
        var text = pre + "---\n";
        doc.Insert(offset, text);
        editor.TextArea.Caret.Offset = offset + text.Length;
    }

    private static void InsertLink(TextEditor editor)
    {
        var doc = editor.Document;
        var offset = editor.TextArea.Caret.Offset;
        doc.Insert(offset, "[](url)");

        // Select "url" so the user can immediately type the URL
        editor.Select(offset + 3, 3); // "[](url)" — "url" at index 3, length 3
    }
}
