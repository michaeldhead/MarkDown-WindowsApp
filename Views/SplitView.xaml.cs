using AvalonEditB;
using AvalonEditB.Rendering;
using GHSMarkdownEditor.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace GHSMarkdownEditor.Views;

/// <summary>
/// Code-behind for the split editor/preview layout. Restores the persisted splitter
/// ratio on load and wires AvalonEdit's scroll-offset changes to the WebView2 preview
/// so both panes stay roughly in sync as the user scrolls.
/// </summary>
public partial class SplitView : UserControl
{
    /// <summary>Exposed so MainWindow can route formatting commands to the active editor in Split mode.</summary>
    internal TextEditor Editor => editorView.Editor;

    /// <summary>
    /// True while <see cref="OnPreviewScrolled"/> is programmatically scrolling the editor
    /// in response to a preview scroll event. Suppresses the editor-to-preview sync in
    /// <see cref="OnEditorScrollOffsetChanged"/> for the resulting <c>ScrollOffsetChanged</c>
    /// event, preventing a ping-pong feedback loop. Reset inside the handler rather than
    /// immediately after the scroll call, because <c>ScrollOffsetChanged</c> fires
    /// asynchronously during the next WPF layout pass.
    /// </summary>
    private bool _isProgrammaticScroll;

    /// <summary>
    /// True while <see cref="OnPreviewBlockClicked"/> is moving the editor caret in response
    /// to a preview single-click. Suppresses the <see cref="OnEditorScrollOffsetChanged"/> →
    /// <see cref="PreviewView.ScrollToRatio"/> call that would otherwise scroll the preview
    /// away from the block the user just clicked. Reset inside <see cref="OnEditorScrollOffsetChanged"/>
    /// on the first scroll event that follows the programmatic caret move.
    /// </summary>
    private bool _isSyncingFromPreview;


    public SplitView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestoreSplitRatio();
        WireScrollSync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        previewView.PreviewScrolled -= OnPreviewScrolled;
        editorView.CursorLineChanged -= OnCursorLineChanged;
        previewView.InlineEditRequested -= OnInlineEditRequested;
        previewView.InlineEditSaved -= OnInlineEditSaved;
        previewView.PreviewBlockClicked -= OnPreviewBlockClicked;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
            RestoreSplitRatio();
    }

    /// <summary>
    /// Reads the persisted column ratio from settings and applies it to the Grid's star
    /// columns. Called on load and whenever the view becomes visible so the ratio is
    /// applied even when the control is added to the tree after the initial layout pass.
    /// </summary>
    private void RestoreSplitRatio()
    {
        var ratio = App.SettingsService.Get("SplitRatio", 0.5);
        ratio = Math.Clamp(ratio, 0.1, 0.9);
        editorColumn.Width = new GridLength(ratio, GridUnitType.Star);
        previewColumn.Width = new GridLength(1.0 - ratio, GridUnitType.Star);
    }

    /// <summary>Persists the new split ratio whenever the user finishes dragging the splitter.</summary>
    private void GridSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        var total = editorColumn.ActualWidth + previewColumn.ActualWidth;
        if (total <= 0) return;
        var ratio = editorColumn.ActualWidth / total;
        App.SettingsService.Set("SplitRatio", ratio);
    }

    /// <summary>
    /// Subscribes to <see cref="TextView.ScrollOffsetChanged"/> (editor → preview) and to
    /// <see cref="PreviewView.PreviewScrolled"/> (preview → editor) for bidirectional scroll
    /// sync. Wired after load rather than in the constructor so the child controls are
    /// guaranteed to be initialised.
    /// </summary>
    private void WireScrollSync()
    {
        var textView = editorView.Editor.TextArea.TextView;
        textView.ScrollOffsetChanged += OnEditorScrollOffsetChanged;
        previewView.PreviewScrolled += OnPreviewScrolled;
        editorView.CursorLineChanged += OnCursorLineChanged;
        previewView.InlineEditRequested += OnInlineEditRequested;
        previewView.InlineEditSaved += OnInlineEditSaved;
        previewView.PreviewBlockClicked += OnPreviewBlockClicked;
    }

    /// <summary>
    /// Converts the editor's absolute scroll offset to a 0–1 ratio and forwards it to
    /// <c>PreviewView.ScrollToRatio</c> so the preview scrolls proportionally rather
    /// than by the same pixel amount (the two panes have different content heights).
    /// Skips sync (and resets the guard) when the scroll was triggered programmatically
    /// by <see cref="OnPreviewScrolled"/> to prevent a feedback loop.
    /// </summary>
    private async void OnEditorScrollOffsetChanged(object? sender, EventArgs e)
    {
        if (_isProgrammaticScroll)
        {
            // The scroll was initiated by OnPreviewScrolled — reset the guard and bail out
            // so we don't echo the scroll back to the preview.
            _isProgrammaticScroll = false;
            return;
        }
        if (_isSyncingFromPreview)
        {
            // The scroll was initiated by OnPreviewBlockClicked — reset the guard and bail out
            // so we don't scroll the preview away from the block the user just clicked.
            _isSyncingFromPreview = false;
            return;
        }
        if (sender is not TextView textView) return;
        var maxScroll = textView.DocumentHeight - textView.ActualHeight;
        if (maxScroll <= 0) return;
        var ratio = Math.Clamp(textView.ScrollOffset.Y / maxScroll, 0.0, 1.0);
        await previewView.ScrollToRatio(ratio);
    }

    /// <summary>
    /// Handles <see cref="PreviewView.PreviewScrolled"/>: scrolls the editor to the
    /// proportionally matching position. Sets <see cref="_isProgrammaticScroll"/> before
    /// scrolling so the resulting <see cref="TextView.ScrollOffsetChanged"/> event (which
    /// fires during the next WPF layout pass) is suppressed.
    /// </summary>
    private void OnPreviewScrolled(object? sender, double ratio)
    {
        var scrollViewer = FindScrollViewer(editorView.Editor);
        if (scrollViewer == null) return;
        _isProgrammaticScroll = true;
        scrollViewer.ScrollToVerticalOffset(ratio * scrollViewer.ScrollableHeight);
    }

    /// <summary>
    /// Handles <see cref="EditorView.CursorLineChanged"/>: asks the preview to highlight
    /// the block that best corresponds to the new cursor position. Fire-and-forget — the
    /// result is not awaited because this is an event handler and the highlight is
    /// best-effort (the preview may be navigating between renders).
    /// </summary>
    private void OnCursorLineChanged(object? sender, int lineNumber)
    {
        _ = previewView.HighlightSourceLine(lineNumber);
    }

    /// <summary>
    /// Handles <see cref="PreviewView.PreviewBlockClicked"/>: moves the AvalonEdit caret to the
    /// clicked block's source line and gives focus to the editor so the user can type immediately.
    /// Sets <see cref="_isSyncingFromPreview"/> before scrolling so the resulting
    /// <see cref="OnEditorScrollOffsetChanged"/> event does not echo a scroll back to the preview,
    /// which would jump it away from the block the user just clicked.
    /// </summary>
    private void OnPreviewBlockClicked(object? sender, int lineNumber)
    {
        var editor = editorView.Editor;
        var doc = editor.Document;
        if (lineNumber < 1 || lineNumber > doc.LineCount) return;

        _isSyncingFromPreview = true;
        var line = doc.GetLineByNumber(Math.Min(lineNumber, doc.LineCount));
        editor.CaretOffset = line.Offset;
        editor.ScrollToLine(lineNumber);
        editor.Focus();
    }

    /// <summary>
    /// Handles <see cref="PreviewView.InlineEditRequested"/>: extracts the raw Markdown for the
    /// block starting at <paramref name="e"/>.LineNumber and delivers it to the textarea via the
    /// <see cref="InlineEditRequestedEventArgs.ProvideMarkdown"/> callback. Collects lines from
    /// the document starting at the given 1-based line until the next blank line or end of file.
    /// </summary>
    private void OnInlineEditRequested(object? sender, InlineEditRequestedEventArgs e)
    {
        var content = (DataContext as MainViewModel)?.ActiveTab?.Content ?? string.Empty;
        var lines = content.Split('\n');
        var startIdx = e.LineNumber - 1;
        if (startIdx < 0 || startIdx >= lines.Length) return;

        var blockLines = new List<string>();
        for (int i = startIdx; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) break;
            blockLines.Add(line);
        }

        e.ProvideMarkdown(string.Join('\n', blockLines));
    }

    /// <summary>
    /// Handles <see cref="PreviewView.InlineEditSaved"/>: replaces the affected block lines in
    /// the AvalonEdit document with the new Markdown text. Uses the same block-boundary logic as
    /// <see cref="OnInlineEditRequested"/> (consecutive non-blank lines starting at
    /// <paramref name="e"/>.LineNumber). The document replacement triggers the normal content-change
    /// pipeline: IsDirty becomes true and the preview re-renders via the debounced handler.
    /// </summary>
    private void OnInlineEditSaved(object? sender, InlineEditSavedEventArgs e)
    {
        var doc = editorView.Editor.Document;
        if (e.LineNumber < 1 || e.LineNumber > doc.LineCount) return;

        // Walk forward from the starting line to find the last line of the block.
        int lastLineNum = e.LineNumber;
        for (int lineNum = e.LineNumber; lineNum <= doc.LineCount; lineNum++)
        {
            var docLine = doc.GetLineByNumber(lineNum);
            var lineText = doc.GetText(docLine.Offset, docLine.Length);
            if (string.IsNullOrWhiteSpace(lineText)) break;
            lastLineNum = lineNum;
        }

        var startLine = doc.GetLineByNumber(e.LineNumber);
        var endLine   = doc.GetLineByNumber(lastLineNum);

        // Replace only the text of the block lines (EndOffset excludes the line delimiter),
        // so the newline after the block and everything following it are preserved.
        doc.Replace(startLine.Offset, endLine.EndOffset - startLine.Offset, e.NewMarkdown);
    }

    /// <summary>
    /// Walks the visual tree of <paramref name="d"/> depth-first and returns the first
    /// <see cref="ScrollViewer"/> found, or <c>null</c>. Used to locate the ScrollViewer
    /// inside AvalonEditB's <c>TextEditor</c> control template, which does not expose it
    /// as a public property.
    /// </summary>
    private static ScrollViewer? FindScrollViewer(DependencyObject d)
    {
        int count = VisualTreeHelper.GetChildrenCount(d);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(d, i);
            if (child is ScrollViewer sv) return sv;
            var found = FindScrollViewer(child);
            if (found != null) return found;
        }
        return null;
    }
}
