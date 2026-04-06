using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace GHSMarkdownEditor.ViewModels;

public class LanguageItem
{
    public required string Name { get; init; }
    public bool IsCurrent { get; init; }
}

public partial class LanguagePickerViewModel : ObservableObject
{
    private static readonly string[] AllLanguages =
    [
        "plaintext", "csharp", "javascript", "typescript", "python",
        "html", "css", "sql", "bash", "json", "xml", "yaml", "markdown", "powershell"
    ];

    private readonly string _currentLanguage;

    public ObservableCollection<LanguageItem> FilteredLanguages { get; } = new();

    [ObservableProperty]
    private string _searchText = string.Empty;

    public LanguagePickerViewModel(string? currentLanguage = null)
    {
        _currentLanguage = currentLanguage ?? string.Empty;
        RebuildFilter(string.Empty);
    }

    partial void OnSearchTextChanged(string value) => RebuildFilter(value);

    private void RebuildFilter(string filter)
    {
        FilteredLanguages.Clear();
        var f = filter.Trim();
        foreach (var lang in AllLanguages)
        {
            if (string.IsNullOrEmpty(f) || lang.Contains(f, StringComparison.OrdinalIgnoreCase))
                FilteredLanguages.Add(new LanguageItem
                {
                    Name = lang,
                    IsCurrent = string.Equals(lang, _currentLanguage, StringComparison.OrdinalIgnoreCase)
                });
        }
    }
}
