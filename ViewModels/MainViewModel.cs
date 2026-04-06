using AvalonEditB;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GHSMarkdownEditor.Models;
using GHSMarkdownEditor.Services;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;

namespace GHSMarkdownEditor.ViewModels;

/// <summary>
/// Root application view model. Owns all open tabs, the current layout mode, editor
/// settings, and coordinates between file I/O, formatting, and the sidebar.
/// <see cref="ActiveEditorProvider"/> is injected by <c>MainWindow</c> after the window
/// finishes loading, because the AvalonEdit control is not available during construction.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly FormattingService _formattingService = new();
    private readonly FileService _fileService;

    /// <summary>Set from MainWindow code-behind; returns the currently active TextEditor.</summary>
    public Func<TextEditor?>? ActiveEditorProvider { get; set; }

    /// <summary>
    /// Raised when the main window should open or close the detached preview window.
    /// The main window subscribes and handles the actual Window creation/disposal.
    /// </summary>
    public event EventHandler? DetachPreviewRequested;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private DocumentTabViewModel? _activeTab;

    [ObservableProperty] private int _editorFontSize = 14;
    [ObservableProperty] private bool _editorWordWrap = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWriteMode))]
    [NotifyPropertyChangedFor(nameof(IsSplitMode))]
    [NotifyPropertyChangedFor(nameof(IsPreviewMode))]
    [NotifyPropertyChangedFor(nameof(ShowDetachPreviewButton))]
    private ViewMode _currentViewMode = ViewMode.Split;

    /// <summary>Whether the preview is currently shown in a detached window.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowDetachPreviewButton))]
    private bool _isPreviewDetached;

    /// <summary>Whether the command palette overlay is currently open.</summary>
    [ObservableProperty] private bool _isCommandPaletteOpen;

    private DocumentTabViewModel? _previousActiveTab;

    /// <summary>The sidebar panel view model, shared between the sidebar control and each sub-panel.</summary>
    public SidebarViewModel Sidebar { get; }

    /// <summary>The command palette ViewModel, bound to the overlay in MainWindow.</summary>
    public CommandPaletteViewModel CommandPalette { get; }

    /// <summary>All open document tabs.</summary>
    public ObservableCollection<DocumentTabViewModel> Tabs { get; } = new();

    /// <summary>Recent files list mirrored from settings for the menu bar submenu.</summary>
    public ObservableCollection<RecentFileItem> RecentFiles { get; } = new();

    /// <summary>Window title bar text; reflects the active file name so the OS taskbar shows the open document.</summary>
    public string WindowTitle => ActiveTab != null
        ? $"GHS Markdown Editor — {ActiveTab.FileName}"
        : "GHS Markdown Editor";

    /// <summary>Live word count of the active document; re-evaluated on every content change.</summary>
    public int WordCount => CountWords(ActiveTab?.Content ?? string.Empty);

    /// <summary>Convenience projections of <see cref="CurrentViewMode"/> used for toolbar toggle bindings.</summary>
    public bool IsWriteMode   => CurrentViewMode == ViewMode.Write;
    public bool IsSplitMode   => CurrentViewMode == ViewMode.Split;
    public bool IsPreviewMode => CurrentViewMode == ViewMode.Preview;

    /// <summary>
    /// Whether the Detach Preview button should be visible in the toolbar.
    /// True when the app is in Split or Preview mode, or while the preview is detached.
    /// </summary>
    public bool ShowDetachPreviewButton => IsSplitMode || IsPreviewMode || IsPreviewDetached;

    /// <summary>
    /// Constructs the view model, restores persisted settings, and opens a blank tab.
    /// <c>App.SettingsService</c> is a static singleton guaranteed to exist by the time
    /// any view model is constructed (initialised in <c>App.OnStartup</c>).
    /// </summary>
    public MainViewModel()
    {
        _fileService = new FileService(App.SettingsService);

        CurrentViewMode = App.SettingsService.Get("ViewMode", ViewMode.Split);
        EditorFontSize  = App.SettingsService.Get("EditorFontSize", 14);
        EditorWordWrap  = App.SettingsService.Get("EditorWordWrap", true);

        Sidebar          = new SidebarViewModel(this, App.SettingsService, _fileService);
        CommandPalette   = new CommandPaletteViewModel(this, App.SettingsService);

        RefreshRecentFiles();
        NewTab();
    }

    /// <summary>
    /// Migrates the <c>PropertyChanged</c> subscription to the new tab so
    /// <see cref="WordCount"/> stays live without leaking handlers on old tabs.
    /// </summary>
    partial void OnActiveTabChanged(DocumentTabViewModel? value)
    {
        if (_previousActiveTab != null)
            _previousActiveTab.PropertyChanged -= OnActiveTabPropertyChanged;
        if (value != null)
            value.PropertyChanged += OnActiveTabPropertyChanged;
        _previousActiveTab = value;
        OnPropertyChanged(nameof(WordCount));
    }

    private void OnActiveTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentTabViewModel.Content))
            OnPropertyChanged(nameof(WordCount));
    }

    partial void OnEditorFontSizeChanged(int value) => App.SettingsService.Set("EditorFontSize", value);
    partial void OnEditorWordWrapChanged(bool value) => App.SettingsService.Set("EditorWordWrap", value);

    // ── File Operations ──────────────────────────────────────────────────────

    [RelayCommand]
    public void NewTab()
    {
        var tab = _fileService.CreateNew();
        Tabs.Add(tab);
        SetActiveTab(tab);
    }

    [RelayCommand]
    public void OpenFile()
    {
        var tab = _fileService.OpenFile();
        if (tab != null)
        {
            Tabs.Add(tab);
            SetActiveTab(tab);
            RefreshRecentFiles();
        }
    }

    [RelayCommand]
    public void SaveFile()
    {
        if (ActiveTab != null && _fileService.SaveFile(ActiveTab))
            RefreshRecentFiles();
    }

    [RelayCommand]
    public void SaveFileAs()
    {
        if (ActiveTab != null && _fileService.SaveFileAs(ActiveTab))
            RefreshRecentFiles();
    }

    [RelayCommand]
    public async Task CloseActiveTab() => await CloseTab(ActiveTab);

    [RelayCommand]
    public async Task CloseTab(DocumentTabViewModel? tab)
    {
        if (tab == null) return;

        if (tab.IsDirty)
        {
            var dialog = new GHSMarkdownEditor.Controls.ConfirmCloseDialog(tab.FileName);
            var result = await DialogHost.Show(dialog, "RootDialog");
            if (result is not string s || s != bool.TrueString)
                return;
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);

        if (Tabs.Count == 0)
        {
            NewTab();
        }
        else if (tab.IsActive)
        {
            var newIndex = Math.Min(index, Tabs.Count - 1);
            SetActiveTab(Tabs[newIndex]);
        }
    }

    /// <summary>Opens a file from a known path without showing a dialog; used by drag-drop, pipe, and recent files.</summary>
    public void OpenFromPath(string path)
    {
        var tab = _fileService.OpenFromPath(path);
        if (tab == null) return;
        Tabs.Add(tab);
        SetActiveTab(tab);
        RefreshRecentFiles();
    }

    /// <summary>Opens a recent file by path; bound to the Recent Files submenu.</summary>
    [RelayCommand]
    public void OpenRecentFile(string? path)
    {
        if (!string.IsNullOrEmpty(path))
            OpenFromPath(path);
    }

    // ── Print ────────────────────────────────────────────────────────────────

    /// <summary>Renders the active document to HTML and opens the system print dialog.</summary>
    [RelayCommand]
    private async Task PrintAsync()
    {
        if (ActiveTab == null) return;
        await App.PrintService.PrintAsync(ActiveTab.Content, App.ThemeService?.IsDark ?? false);
    }

    // ── Export ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the Export As dialog, prompts for a file path, then runs the selected export.
    /// PDF export must be called on the UI thread (creates a temporary WebView2 window).
    /// </summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (ActiveTab == null) return;

        // 1. Show the format-selection dialog.
        var dialog = new GHSMarkdownEditor.Controls.ExportDialog();
        var result = await DialogHost.Show(dialog, "RootDialog");

        if (result is not ExportFormat format) return;

        // 2. Show a SaveFileDialog with the appropriate filter for the chosen format.
        var saveDialog = new SaveFileDialog
        {
            Title      = "Export As",
            Filter     = ExportFormatInfo.GetFilter(format),
            DefaultExt = ExportFormatInfo.GetExtension(format).TrimStart('.'),
            FileName   = Path.GetFileNameWithoutExtension(ActiveTab.FileName ?? "export")
                         + ExportFormatInfo.GetExtension(format)
        };

        if (!string.IsNullOrEmpty(ActiveTab.FilePath))
            saveDialog.InitialDirectory = Path.GetDirectoryName(ActiveTab.FilePath);

        if (saveDialog.ShowDialog() != true) return;

        // 3. Run the export.
        try
        {
            await App.ExportService.ExportAsync(format, ActiveTab.Content, saveDialog.FileName);
            MessageBox.Show(
                $"Exported successfully:\n{saveDialog.FileName}",
                "Export Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Export failed:\n{ex.Message}",
                "Export Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    // ── Exit ─────────────────────────────────────────────────────────────────

    /// <summary>Exits the application.</summary>
    [RelayCommand]
    private void Exit() => System.Windows.Application.Current.Shutdown();

    // ── View Mode Commands ───────────────────────────────────────────────────

    [RelayCommand]
    private void SetViewMode(ViewMode mode)
    {
        if (CurrentViewMode == mode) return;
        CurrentViewMode = mode;
        App.SettingsService.Set("ViewMode", mode);
    }

    [RelayCommand] public void SetViewModeWrite()   => SetViewMode(ViewMode.Write);
    [RelayCommand] public void SetViewModeSplit()   => SetViewMode(ViewMode.Split);
    [RelayCommand] public void SetViewModePreview() => SetViewMode(ViewMode.Preview);

    // ── Detach Preview ───────────────────────────────────────────────────────

    /// <summary>
    /// Fires <see cref="DetachPreviewRequested"/>.
    /// MainWindow subscribes and toggles the detached preview window.
    /// </summary>
    [RelayCommand]
    private void ToggleDetachPreview() => DetachPreviewRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Called by MainWindow when the detached preview window opens or closes.
    /// Switches the main window to Write mode on detach and back to Split on reattach.
    /// </summary>
    public void NotifyPreviewDetached(bool isDetached)
    {
        IsPreviewDetached = isDetached;
        SetViewMode(isDetached ? ViewMode.Write : ViewMode.Split);
    }

    // ── Command Palette ──────────────────────────────────────────────────────

    /// <summary>Opens the command palette overlay.</summary>
    [RelayCommand]
    public void OpenCommandPalette()
    {
        CommandPalette.Open();
        IsCommandPaletteOpen = true;
    }

    // ── About / Companion Web App ────────────────────────────────────────────

    /// <summary>Shows the About dialog using the MaterialDesign DialogHost.</summary>
    [RelayCommand]
    private void ShowAbout()
    {
        var dialog = new GHSMarkdownEditor.Controls.AboutDialog();
        _ = DialogHost.Show(dialog, "RootDialog");
    }

    /// <summary>Opens the companion web app in the system default browser.</summary>
    [RelayCommand]
    private void OpenCompanionWebApp()
    {
        Process.Start(new ProcessStartInfo("https://md.theheadfamily.com") { UseShellExecute = true });
    }

    // ── Editor scrolling (used by command palette heading activation) ─────────

    /// <summary>Scrolls the active editor to the specified 1-based line number.</summary>
    public void ScrollEditorToLine(int lineNumber)
    {
        var editor = ActiveEditorProvider?.Invoke();
        if (editor == null) return;
        editor.TextArea.Caret.Line = lineNumber;
        editor.TextArea.Caret.BringCaretToView();
        editor.Focus();
    }

    // ── Formatting Commands ──────────────────────────────────────────────────

    [RelayCommand] public void Bold()           => ApplyFormat(FormattingAction.Bold);
    [RelayCommand] public void Italic()         => ApplyFormat(FormattingAction.Italic);
    [RelayCommand] public void Strikethrough()  => ApplyFormat(FormattingAction.Strikethrough);
    [RelayCommand] public void InlineCode()     => ApplyFormat(FormattingAction.InlineCode);
    [RelayCommand] public void H1()             => ApplyFormat(FormattingAction.H1);
    [RelayCommand] public void H2()             => ApplyFormat(FormattingAction.H2);
    [RelayCommand] public void H3()             => ApplyFormat(FormattingAction.H3);
    [RelayCommand] public void H4()             => ApplyFormat(FormattingAction.H4);
    [RelayCommand] public void BulletList()     => ApplyFormat(FormattingAction.BulletList);
    [RelayCommand] public void NumberedList()   => ApplyFormat(FormattingAction.NumberedList);
    [RelayCommand]
    public async Task CodeBlock()
    {
        var dialog = new GHSMarkdownEditor.Controls.LanguagePickerPopup();
        var result = await DialogHost.Show(dialog, "RootDialog");
        if (result is not string language) return;
        var editor = ActiveEditorProvider?.Invoke();
        if (editor != null)
        {
            _formattingService.ApplyCodeBlock(editor, language);
            // DialogHost defers its close/focus-cleanup at Normal priority (9).
            // Posting at Background priority (4) ensures this runs after that
            // cleanup, so the DialogHost can't steal focus back.
            _ = editor.Dispatcher.BeginInvoke(
                editor.Focus,
                System.Windows.Threading.DispatcherPriority.Background);
        }
    }
    [RelayCommand]
    public async Task OpenLanguagePickerForReplace(int lineNumber)
    {
        var editor = ActiveEditorProvider?.Invoke();
        if (editor == null) return;

        var doc = editor.Document;
        if (lineNumber < 1 || lineNumber > doc.LineCount) return;

        var line = doc.GetLineByNumber(lineNumber);
        var lineText = doc.GetText(line.Offset, line.Length);

        var match = Regex.Match(lineText, @"^```(\w*)$");
        if (!match.Success) return;

        var currentLang = match.Groups[1].Value;
        var dialog = new GHSMarkdownEditor.Controls.LanguagePickerPopup(
            string.IsNullOrEmpty(currentLang) ? null : currentLang);
        var result = await DialogHost.Show(dialog, "RootDialog");

        if (result is not string language) return;

        editor = ActiveEditorProvider?.Invoke();
        if (editor == null) return;

        var doc2 = editor.Document;
        if (lineNumber > doc2.LineCount) return;

        var targetLine = doc2.GetLineByNumber(lineNumber);
        var lang = string.Equals(language, "plaintext", StringComparison.OrdinalIgnoreCase) ? "" : language;
        doc2.Replace(targetLine.Offset, targetLine.Length, $"```{lang}");

        editor.TextArea.Caret.Line = lineNumber;
        _ = editor.Dispatcher.BeginInvoke(
            editor.Focus,
            System.Windows.Threading.DispatcherPriority.Background);
    }

    [RelayCommand] public void Table()          => ApplyFormat(FormattingAction.Table);
    [RelayCommand] public void HorizontalRule() => ApplyFormat(FormattingAction.HorizontalRule);
    [RelayCommand] public void Link()           => ApplyFormat(FormattingAction.Link);

    /// <summary>
    /// Retrieves the currently active editor via <see cref="ActiveEditorProvider"/> and
    /// delegates to <see cref="FormattingService"/>. No-op if no editor is available
    /// (e.g. in Preview-only mode where no editor control is in the visual tree).
    /// </summary>
    private void ApplyFormat(FormattingAction action)
    {
        var editor = ActiveEditorProvider?.Invoke();
        if (editor != null)
            _formattingService.ApplyFormatting(editor, action);
    }

    // ── Tab Management ───────────────────────────────────────────────────────

    public void SetActiveTab(DocumentTabViewModel tab)
    {
        if (ActiveTab != null)
            ActiveTab.IsActive = false;

        ActiveTab = tab;
        tab.IsActive = true;
        OnPropertyChanged(nameof(WindowTitle));
    }

    // ── Recent Files / Jump List ─────────────────────────────────────────────

    /// <summary>Refreshes the RecentFiles collection from settings and updates the Jump List.</summary>
    private void RefreshRecentFiles()
    {
        var paths = App.SettingsService.Get("RecentFiles", new List<string>());
        RecentFiles.Clear();
        foreach (var p in paths)
            RecentFiles.Add(new RecentFileItem { FilePath = p });

        App.JumpListService?.UpdateJumpList(paths);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        return text.Split([' ', '\t', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
