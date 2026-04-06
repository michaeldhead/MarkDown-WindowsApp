using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GHSMarkdownEditor.Models;
using GHSMarkdownEditor.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace GHSMarkdownEditor.ViewModels;

/// <summary>
/// View model for the collapsible sidebar. Owns the document outline, snippets, recent
/// files, and settings panels. Editor font/theme settings are proxied through to
/// <see cref="MainViewModel"/> so a single source of truth exists. Heading parsing is
/// debounced to avoid regex overhead on every keystroke.
/// </summary>
public partial class SidebarViewModel : ObservableObject
{
    private readonly MainViewModel _mainVm;
    private readonly SettingsService _settings;
    private readonly FileService _fileService;

    [ObservableProperty] private ActivePanel _currentPanel = ActivePanel.None;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isAddingSnippet;
    [ObservableProperty] private string _newSnippetName = string.Empty;
    [ObservableProperty] private string _newSnippetContent = string.Empty;
    [ObservableProperty] private string _autoSaveInterval = "Off";

    public ObservableCollection<HeadingItem> HeadingItems { get; } = new();
    public ObservableCollection<SnippetItem> Snippets { get; } = new();
    public ObservableCollection<RecentFileItem> RecentFiles { get; } = new();
    public string[] AutoSaveItems { get; } = ["Off", "30s", "1m", "2m"];

    private readonly DispatcherTimer _autoSaveTimer = new();
    private CancellationTokenSource _headingCts = new();
    private DocumentTabViewModel? _subscribedTab;

    public SidebarViewModel(MainViewModel mainVm, SettingsService settings, FileService fileService)
    {
        _mainVm = mainVm;
        _settings = settings;
        _fileService = fileService;

        CurrentPanel    = _settings.Get("SidebarCurrentPanel", ActivePanel.None);
        IsExpanded      = _settings.Get("SidebarIsExpanded", false);
        AutoSaveInterval = _settings.Get("AutoSaveInterval", "Off");

        LoadSnippets();
        RefreshRecentFiles();

        _mainVm.PropertyChanged += OnMainVmPropertyChanged;
        SubscribeToActiveTab();

        _autoSaveTimer.Tick += (_, _) => _mainVm.SaveFile();
        ApplyAutoSaveInterval(AutoSaveInterval);

        if (App.ThemeService != null)
            App.ThemeService.ThemeChanged += OnThemeChanged;

        HeadingItems.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasHeadings));
    }

    // ── Computed ─────────────────────────────────────────────────────────────

    public bool HasHeadings => HeadingItems.Count > 0;

    // ── Theme pass-throughs ──────────────────────────────────────────────────

    /// <summary>
    /// Radio-button-style theme properties. The setter calls <see cref="ThemeService.SetTheme"/>
    /// (which fires <see cref="ThemeService.ThemeChanged"/>); the getter checks the current
    /// theme so the radio buttons reflect the persisted state when the settings panel opens.
    /// <see cref="OnThemeChanged"/> then raises <c>PropertyChanged</c> for all three so the
    /// correct button appears selected after any external theme change.
    /// </summary>
    public bool IsThemeLight
    {
        get => App.ThemeService?.CurrentTheme == ThemeMode.Light;
        set { if (value) App.ThemeService?.SetTheme(ThemeMode.Light); }
    }

    public bool IsThemeDark
    {
        get => App.ThemeService?.CurrentTheme == ThemeMode.Dark;
        set { if (value) App.ThemeService?.SetTheme(ThemeMode.Dark); }
    }

    public bool IsThemeAuto
    {
        get => App.ThemeService?.CurrentTheme == ThemeMode.Auto;
        set { if (value) App.ThemeService?.SetTheme(ThemeMode.Auto); }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsThemeLight));
        OnPropertyChanged(nameof(IsThemeDark));
        OnPropertyChanged(nameof(IsThemeAuto));
    }

    // ── Editor settings pass-throughs ────────────────────────────────────────

    /// <summary>
    /// Pass-through properties to <see cref="MainViewModel"/> so the settings panel
    /// can bind here without needing a reference to the root view model.
    /// Changes propagate via <see cref="OnMainVmPropertyChanged"/> in the reverse direction.
    /// </summary>
    public int EditorFontSize
    {
        get => _mainVm.EditorFontSize;
        set => _mainVm.EditorFontSize = value;
    }

    public bool EditorWordWrap
    {
        get => _mainVm.EditorWordWrap;
        set => _mainVm.EditorWordWrap = value;
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void TogglePanel(ActivePanel panel)
    {
        if (IsExpanded && CurrentPanel == panel)
        {
            IsExpanded   = false;
            CurrentPanel = ActivePanel.None;
        }
        else
        {
            CurrentPanel = panel;
            IsExpanded   = true;

            if (panel == ActivePanel.Outline)
                ParseHeadings(_mainVm.ActiveTab?.Content ?? string.Empty);
        }

        _settings.Set("SidebarCurrentPanel", CurrentPanel);
        _settings.Set("SidebarIsExpanded", IsExpanded);
    }

    [RelayCommand]
    private void ScrollToHeading(HeadingItem item)
    {
        var editor = _mainVm.ActiveEditorProvider?.Invoke();
        if (editor == null) return;
        editor.TextArea.Caret.Line = item.LineNumber;
        editor.TextArea.Caret.BringCaretToView();
        editor.Focus();
    }

    [RelayCommand]
    private void InsertSnippet(SnippetItem snippet)
    {
        var editor = _mainVm.ActiveEditorProvider?.Invoke();
        if (editor == null) return;
        editor.Document.Insert(editor.CaretOffset, snippet.Content);
        editor.Focus();
    }

    [RelayCommand]
    private void OpenRecentFile(RecentFileItem item)
    {
        if (!item.FileExists) return;
        var tab = _fileService.OpenFromPath(item.FilePath);
        if (tab == null) return;
        _mainVm.Tabs.Add(tab);
        _mainVm.SetActiveTab(tab);
        RefreshRecentFiles();
    }

    [RelayCommand]
    private void ClearRecentFiles()
    {
        _settings.Set("RecentFiles", new List<string>());
        RecentFiles.Clear();
    }

    [RelayCommand]
    private void StartAddSnippet()
    {
        NewSnippetName    = string.Empty;
        NewSnippetContent = string.Empty;
        IsAddingSnippet   = true;
    }

    [RelayCommand]
    private void SaveNewSnippet()
    {
        if (string.IsNullOrWhiteSpace(NewSnippetName)) return;
        Snippets.Add(new SnippetItem { Name = NewSnippetName.Trim(), Content = NewSnippetContent });
        SaveSnippets();
        IsAddingSnippet = false;
    }

    [RelayCommand]
    private void CancelAddSnippet() => IsAddingSnippet = false;

    [RelayCommand]
    private void DeleteSnippet(SnippetItem snippet)
    {
        Snippets.Remove(snippet);
        SaveSnippets();
    }

    // ── Partial hooks ────────────────────────────────────────────────────────

    partial void OnAutoSaveIntervalChanged(string value)
    {
        _settings.Set("AutoSaveInterval", value);
        ApplyAutoSaveInterval(value);
    }

    // ── Heading parsing ──────────────────────────────────────────────────────

    private void OnMainVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.ActiveTab):
                SubscribeToActiveTab();
                RefreshRecentFiles();
                _ = RefreshHeadingsDebounced();
                break;
            case nameof(MainViewModel.EditorFontSize):
                OnPropertyChanged(nameof(EditorFontSize));
                break;
            case nameof(MainViewModel.EditorWordWrap):
                OnPropertyChanged(nameof(EditorWordWrap));
                break;
        }
    }

    private void SubscribeToActiveTab()
    {
        if (_subscribedTab != null)
            _subscribedTab.PropertyChanged -= OnTabPropertyChanged;
        _subscribedTab = _mainVm.ActiveTab;
        if (_subscribedTab != null)
            _subscribedTab.PropertyChanged += OnTabPropertyChanged;
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentTabViewModel.Content))
            _ = RefreshHeadingsDebounced();
    }

    /// <summary>
    /// Cancels any pending heading parse and schedules a new one 400 ms in the future.
    /// The replace-the-CTS pattern (cancel old token, create new one) ensures that rapid
    /// content changes collapse into a single parse at the end of the burst rather than
    /// queuing one parse per keystroke.
    /// </summary>
    private async Task RefreshHeadingsDebounced()
    {
        _headingCts.Cancel();
        _headingCts = new CancellationTokenSource();
        var token = _headingCts.Token;
        try
        {
            await Task.Delay(400, token);
            if (!token.IsCancellationRequested)
                ParseHeadings(_mainVm.ActiveTab?.Content ?? string.Empty);
        }
        catch (OperationCanceledException) { }
    }

    private void ParseHeadings(string content)
    {
        HeadingItems.Clear();
        var lines = content.Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], @"^(#{1,4})\s+(.+)");
            if (match.Success)
            {
                HeadingItems.Add(new HeadingItem
                {
                    Level      = match.Groups[1].Length,
                    Text       = match.Groups[2].Value.Trim(),
                    LineNumber = i + 1
                });
            }
        }
    }

    // ── Snippets ─────────────────────────────────────────────────────────────

    private void LoadSnippets()
    {
        var list = _settings.Get("Snippets", new List<SnippetItem>());
        foreach (var s in list)
            Snippets.Add(s);
    }

    private void SaveSnippets()
    {
        _settings.Set("Snippets", Snippets.ToList());
    }

    // ── Recent files ─────────────────────────────────────────────────────────

    public void RefreshRecentFiles()
    {
        var paths = _settings.Get("RecentFiles", new List<string>());
        RecentFiles.Clear();
        foreach (var p in paths)
            RecentFiles.Add(new RecentFileItem { FilePath = p });
    }

    // ── Auto-save ────────────────────────────────────────────────────────────

    /// <summary>
    /// Stops any running auto-save timer and restarts it at the new interval.
    /// "Off" (or any unrecognised value) leaves the timer stopped.
    /// The timer fires <see cref="MainViewModel.SaveFile"/> on the UI thread via
    /// <see cref="DispatcherTimer"/>, so no marshalling is needed.
    /// </summary>
    private void ApplyAutoSaveInterval(string interval)
    {
        _autoSaveTimer.Stop();
        int seconds = interval switch
        {
            "30s" => 30,
            "1m"  => 60,
            "2m"  => 120,
            _     => 0
        };
        if (seconds > 0)
        {
            _autoSaveTimer.Interval = TimeSpan.FromSeconds(seconds);
            _autoSaveTimer.Start();
        }
    }
}
