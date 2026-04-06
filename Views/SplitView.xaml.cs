using AvalonEditB;
using AvalonEditB.Rendering;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

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

    public SplitView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RestoreSplitRatio();
        WireScrollSync();
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
    /// Subscribes to <see cref="TextView.ScrollOffsetChanged"/> so the preview pane
    /// mirrors the editor's vertical scroll position. Wired after load rather than in
    /// the constructor so <c>editorView.Editor</c> is guaranteed to be initialised.
    /// </summary>
    private void WireScrollSync()
    {
        var textView = editorView.Editor.TextArea.TextView;
        textView.ScrollOffsetChanged += OnEditorScrollOffsetChanged;
    }

    /// <summary>
    /// Converts the editor's absolute scroll offset to a 0–1 ratio and forwards it to
    /// <c>PreviewView.ScrollToRatio</c> so the preview scrolls proportionally rather
    /// than by the same pixel amount (the two panes have different content heights).
    /// </summary>
    private async void OnEditorScrollOffsetChanged(object? sender, EventArgs e)
    {
        if (sender is not TextView textView) return;
        var maxScroll = textView.DocumentHeight - textView.ActualHeight;
        if (maxScroll <= 0) return;
        var ratio = Math.Clamp(textView.ScrollOffset.Y / maxScroll, 0.0, 1.0);
        await previewView.ScrollToRatio(ratio);
    }
}
