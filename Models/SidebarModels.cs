using System.IO;
using System.Text.Json.Serialization;

namespace GHSMarkdownEditor.Models;

public enum ActivePanel
{
    None,
    Outline,
    RecentFiles,
    Snippets,
    Settings
}

/// <summary>A saved text snippet. Serialised to settings.json.</summary>
public class SnippetItem
{
    public string Name { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    [JsonIgnore]
    public string Preview =>
        Content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
               .FirstOrDefault()?.Trim() ?? string.Empty;
}

/// <summary>A heading entry in the document outline. Runtime-only, not serialised.</summary>
public class HeadingItem
{
    public int Level { get; init; }       // 1–4
    public string Text { get; init; } = string.Empty;
    public int LineNumber { get; init; }  // 1-based

    [JsonIgnore]
    public double Indent => (Level - 1) * 12.0;

    [JsonIgnore]
    public bool IsBold => Level == 1;
}

/// <summary>An entry in the recent files list. Runtime-only, not serialised.</summary>
public class RecentFileItem
{
    public string FilePath { get; init; } = string.Empty;

    [JsonIgnore]
    public string FileName => Path.GetFileName(FilePath);

    [JsonIgnore]
    public bool FileExists => File.Exists(FilePath);
}
