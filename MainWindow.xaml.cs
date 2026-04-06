using AvalonEditB;
using GHSMarkdownEditor.Models;
using GHSMarkdownEditor.ViewModels;
using GHSMarkdownEditor.Views;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace GHSMarkdownEditor;

public partial class MainWindow : Window
{
    private DetachedPreview? _detachedPreviewWindow;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        vm.ActiveEditorProvider   = GetActiveEditor;
        vm.DetachPreviewRequested += OnDetachPreviewRequested;

        // Subscribe to file paths arriving from a second instance via the named pipe.
        App.FileOpenRequested += OnFileOpenRequested;

        // Open a file that was supplied as a command-line argument on first launch.
        if (App.StartupFilePath is { Length: > 0 } filePath)
            vm.OpenFromPath(filePath);
    }

    // ── Active editor resolution ──────────────────────────────────────────────

    private TextEditor? GetActiveEditor()
    {
        var vm = (MainViewModel)DataContext;
        return vm.CurrentViewMode switch
        {
            ViewMode.Write => editorView.Editor,
            ViewMode.Split => splitView.Editor,
            _              => null
        };
    }

    // ── Detach Preview ────────────────────────────────────────────────────────

    /// <summary>
    /// Handles the <see cref="MainViewModel.DetachPreviewRequested"/> event.
    /// Toggles the detached preview window open or closed.
    /// </summary>
    private void OnDetachPreviewRequested(object? sender, EventArgs e)
    {
        if (_detachedPreviewWindow == null)
            OpenDetachedPreview();
        else
            CloseDetachedPreview();
    }

    private void OpenDetachedPreview()
    {
        if (_detachedPreviewWindow != null) return;
        var vm = (MainViewModel)DataContext;

        _detachedPreviewWindow       = new DetachedPreview(vm);
        _detachedPreviewWindow.Owner = this;
        _detachedPreviewWindow.Closed += (_, _) =>
        {
            _detachedPreviewWindow = null;
            ((MainViewModel)DataContext).NotifyPreviewDetached(false);
        };
        _detachedPreviewWindow.Show();
        vm.NotifyPreviewDetached(true);
    }

    private void CloseDetachedPreview()
    {
        _detachedPreviewWindow?.Close();
        // OnClosed fires and calls NotifyPreviewDetached(false).
    }

    // ── Named pipe: open file from second instance ────────────────────────────

    /// <summary>
    /// Called when a second app instance sends a file path via the named pipe.
    /// Brings the window to the foreground and opens the file in a new tab.
    /// </summary>
    private void OnFileOpenRequested(string path)
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();

        if (DataContext is MainViewModel vm)
            vm.OpenFromPath(path);
    }

    // ── Drag and Drop ─────────────────────────────────────────────────────────

    /// <summary>
    /// Shows a blue highlight border when dragging a supported file over the window.
    /// </summary>
    protected override void OnDragOver(DragEventArgs e)
    {
        base.OnDragOver(e);

        if (HasSupportedFiles(e))
        {
            e.Effects = DragDropEffects.Copy;
            dragDropBorder.BorderBrush = Brushes.CornflowerBlue;
        }
        else
        {
            e.Effects = DragDropEffects.None;
            dragDropBorder.BorderBrush = Brushes.Transparent;
        }

        e.Handled = true;
    }

    /// <summary>Clears the drag-over highlight border when the drag leaves the window.</summary>
    protected override void OnDragLeave(DragEventArgs e)
    {
        base.OnDragLeave(e);
        dragDropBorder.BorderBrush = Brushes.Transparent;
    }

    /// <summary>
    /// Opens each dropped <c>.md</c> or <c>.txt</c> file in a new tab.
    /// Non-supported files are silently ignored.
    /// </summary>
    protected override void OnDrop(DragEventArgs e)
    {
        base.OnDrop(e);
        dragDropBorder.BorderBrush = Brushes.Transparent;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);

        if (DataContext is not MainViewModel vm) return;

        foreach (var file in files)
        {
            var ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is ".md" or ".txt")
                vm.OpenFromPath(file);
        }
    }

    private static bool HasSupportedFiles(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;
        var files = (string[])e.Data.GetData(DataFormats.FileDrop);
        return files.Any(f =>
        {
            var ext = Path.GetExtension(f).ToLowerInvariant();
            return ext is ".md" or ".txt";
        });
    }

    // ── Command Palette backdrop ──────────────────────────────────────────────

    /// <summary>Closes the command palette when the user clicks outside the palette card.</summary>
    private void CommandPaletteBackdrop_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.IsCommandPaletteOpen = false;
    }
}
