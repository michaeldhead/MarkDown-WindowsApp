using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GHSMarkdownEditor.Models;
using GHSMarkdownEditor.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.RegularExpressions;

namespace GHSMarkdownEditor.ViewModels;

// ── Palette row types ────────────────────────────────────────────────────────

/// <summary>Base class for items in the command palette list.</summary>
public abstract class PaletteRow
{
    /// <summary>Returns <see langword="true"/> for non-interactive category header rows.</summary>
    public abstract bool IsHeader { get; }
}

/// <summary>Non-interactive category header displayed above a group of results.</summary>
public sealed class PaletteHeader : PaletteRow
{
    /// <inheritdoc/>
    public override bool IsHeader => true;

    /// <summary>Display label for the category group.</summary>
    public string Category { get; init; } = string.Empty;
}

/// <summary>Selectable command palette result that performs an action when activated.</summary>
public sealed class PaletteItem : PaletteRow
{
    /// <inheritdoc/>
    public override bool IsHeader => false;

    /// <summary>Category this item belongs to (used for grouping).</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>Primary display text.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Optional secondary text (e.g., file path or line number).</summary>
    public string? Subtitle { get; init; }

    /// <summary>Action invoked when the user activates this item.</summary>
    public Action? Execute { get; init; }
}

// ── ViewModel ────────────────────────────────────────────────────────────────

/// <summary>
/// ViewModel for the command palette overlay.
/// Provides fuzzy-filtered, grouped search across open tabs, document headings,
/// recent files, and application commands.
/// </summary>
public partial class CommandPaletteViewModel : ObservableObject
{
    private readonly MainViewModel _mainVm;
    private readonly SettingsService _settings;

    /// <summary>Complete unfiltered candidate list, rebuilt each time the palette opens.</summary>
    private readonly List<PaletteItem> _allItems = new();

    /// <summary>Current search query entered in the palette's search box.</summary>
    [ObservableProperty] private string _searchText = string.Empty;

    /// <summary>
    /// Flat observable list of <see cref="PaletteHeader"/> and <see cref="PaletteItem"/> rows
    /// shown in the palette list after filtering.
    /// </summary>
    public ObservableCollection<PaletteRow> FilteredItems { get; } = new();

    /// <summary>
    /// Currently highlighted result (always a <see cref="PaletteItem"/>, never a header).
    /// </summary>
    [ObservableProperty] private PaletteItem? _selectedItem;

    /// <summary>
    /// Initialises the palette with a reference to the main ViewModel and the settings service.
    /// </summary>
    public CommandPaletteViewModel(MainViewModel mainVm, SettingsService settings)
    {
        _mainVm   = mainVm;
        _settings = settings;
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the palette is opened.  Rebuilds the candidate list from the current
    /// application state, clears the search query, and selects the first result.
    /// </summary>
    public void Open()
    {
        BuildAllItems();
        // Reset search — triggers FilterItems via the partial hook.
        SearchText = string.Empty;
        FilterItems();
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>Closes the palette without activating any item.</summary>
    [RelayCommand]
    private void Close() => _mainVm.IsCommandPaletteOpen = false;

    /// <summary>Moves the selection to the next visible result item, skipping headers.</summary>
    [RelayCommand]
    private void MoveDown()
    {
        var visible = FilteredItems.OfType<PaletteItem>().ToList();
        if (visible.Count == 0) return;
        var idx = SelectedItem == null ? -1 : visible.IndexOf(SelectedItem);
        SelectedItem = visible[Math.Min(idx + 1, visible.Count - 1)];
    }

    /// <summary>Moves the selection to the previous visible result item, skipping headers.</summary>
    [RelayCommand]
    private void MoveUp()
    {
        var visible = FilteredItems.OfType<PaletteItem>().ToList();
        if (visible.Count == 0) return;
        var idx = SelectedItem == null ? visible.Count : visible.IndexOf(SelectedItem);
        SelectedItem = visible[Math.Max(idx - 1, 0)];
    }

    /// <summary>Closes the palette and invokes the selected item's action.</summary>
    [RelayCommand]
    private void ActivateSelected()
    {
        if (SelectedItem == null) return;
        var action = SelectedItem.Execute;
        _mainVm.IsCommandPaletteOpen = false;
        action?.Invoke();
    }

    // ── Search ───────────────────────────────────────────────────────────────

    partial void OnSearchTextChanged(string value) => FilterItems();

    /// <summary>
    /// Filters <see cref="_allItems"/> against the current query using fuzzy matching,
    /// then rebuilds <see cref="FilteredItems"/> with interleaved category headers.
    /// </summary>
    private void FilterItems()
    {
        var query = SearchText.Trim();

        var groups = _allItems
            .Where(item => MatchesFuzzy(query, item.Title) ||
                           (item.Subtitle != null && MatchesFuzzy(query, item.Subtitle)))
            .GroupBy(item => item.Category);

        FilteredItems.Clear();

        foreach (var group in groups)
        {
            FilteredItems.Add(new PaletteHeader { Category = group.Key });
            foreach (var item in group)
                FilteredItems.Add(item);
        }

        SelectedItem = FilteredItems.OfType<PaletteItem>().FirstOrDefault();
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="query"/> fuzzy-matches
    /// <paramref name="text"/>.  Empty query always matches.
    /// Matching priority: substring → all characters appear in order.
    /// </summary>
    private static bool MatchesFuzzy(string query, string text)
    {
        if (string.IsNullOrEmpty(query)) return true;

        query = query.ToLowerInvariant();
        text  = text.ToLowerInvariant();

        if (text.Contains(query)) return true;

        // Fuzzy: every query character must appear in text, in the same order.
        int qi = 0;
        foreach (char c in text)
        {
            if (qi < query.Length && c == query[qi])
                qi++;
        }
        return qi == query.Length;
    }

    // ── Candidate list ───────────────────────────────────────────────────────

    /// <summary>
    /// Populates <see cref="_allItems"/> with candidates from all four categories:
    /// Tabs, Headings, Recent Files, and Commands.
    /// </summary>
    private void BuildAllItems()
    {
        _allItems.Clear();
        BuildTabItems();
        BuildHeadingItems();
        BuildRecentFileItems();
        BuildCommandItems();
    }

    private void BuildTabItems()
    {
        foreach (var tab in _mainVm.Tabs)
        {
            var capturedTab = tab;
            _allItems.Add(new PaletteItem
            {
                Category = "Tabs",
                Title    = tab.FileName,
                Subtitle = tab.FilePath,
                Execute  = () => _mainVm.SetActiveTab(capturedTab)
            });
        }
    }

    private void BuildHeadingItems()
    {
        var content = _mainVm.ActiveTab?.Content ?? string.Empty;
        var lines   = content.Split('\n');

        for (int i = 0; i < lines.Length; i++)
        {
            var match = Regex.Match(lines[i], @"^(#{1,4})\s+(.+)");
            if (!match.Success) continue;

            int    lineNumber = i + 1;
            string prefix     = new string('#', match.Groups[1].Length) + " ";
            string text       = match.Groups[2].Value.Trim();
            int    capturedLine = lineNumber;

            _allItems.Add(new PaletteItem
            {
                Category = "Headings",
                Title    = prefix + text,
                Subtitle = $"Line {lineNumber}",
                Execute  = () => _mainVm.ScrollEditorToLine(capturedLine)
            });
        }
    }

    private void BuildRecentFileItems()
    {
        var paths = _settings.Get("RecentFiles", new List<string>());
        foreach (var path in paths.Take(10))
        {
            var capturedPath = path;
            _allItems.Add(new PaletteItem
            {
                Category = "Recent Files",
                Title    = Path.GetFileName(path),
                Subtitle = path,
                Execute  = () =>
                {
                    if (File.Exists(capturedPath))
                        _mainVm.OpenFromPath(capturedPath);
                }
            });
        }
    }

    private void BuildCommandItems()
    {
        var commands = new (string Title, Action Execute)[]
        {
            ("New Tab",      () => _mainVm.NewTabCommand.Execute(null)),
            ("Open File",    () => _mainVm.OpenFileCommand.Execute(null)),
            ("Save",         () => _mainVm.SaveFileCommand.Execute(null)),
            ("Save As",      () => _mainVm.SaveFileAsCommand.Execute(null)),
            ("Toggle Theme", ToggleTheme),
            ("Write Mode",   () => _mainVm.SetViewModeWriteCommand.Execute(null)),
            ("Split Mode",   () => _mainVm.SetViewModeSplitCommand.Execute(null)),
            ("Preview Mode", () => _mainVm.SetViewModePreviewCommand.Execute(null)),
            ("Print",        () => _mainVm.PrintCommand.Execute(null)),
            ("Exit",         () => _mainVm.ExitCommand.Execute(null)),
        };

        foreach (var (title, execute) in commands)
            _allItems.Add(new PaletteItem { Category = "Commands", Title = title, Execute = execute });
    }

    /// <summary>Toggles between light and dark theme.</summary>
    private static void ToggleTheme()
    {
        if (App.ThemeService == null) return;
        var newMode = App.ThemeService.IsDark ? ThemeMode.Light : ThemeMode.Dark;
        App.ThemeService.SetTheme(newMode);
    }
}
