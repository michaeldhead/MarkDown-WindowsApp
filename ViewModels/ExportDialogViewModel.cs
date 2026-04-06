using CommunityToolkit.Mvvm.ComponentModel;
using GHSMarkdownEditor.Models;

namespace GHSMarkdownEditor.ViewModels;

/// <summary>
/// Represents a single selectable format option in the export dialog.
/// Wraps <see cref="ExportFormat"/> with display-ready metadata.
/// </summary>
public sealed class ExportFormatOption
{
    /// <summary>The underlying export format enum value.</summary>
    public ExportFormat Format { get; init; }

    /// <summary>User-facing display name (e.g. "PDF Document").</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>One-line description shown below the display name.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>File extension including the leading dot (e.g. ".pdf").</summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>
    /// Builds the full ordered list of all export format options.
    /// </summary>
    public static IReadOnlyList<ExportFormatOption> All { get; } =
        Enum.GetValues<ExportFormat>()
            .Select(f => new ExportFormatOption
            {
                Format      = f,
                DisplayName = ExportFormatInfo.GetDisplayName(f),
                Description = ExportFormatInfo.GetDescription(f),
                Extension   = ExportFormatInfo.GetExtension(f)
            })
            .ToList();
}

/// <summary>
/// ViewModel for the Export As dialog.  Holds the list of available formats and tracks
/// the user's selection, which is returned to <see cref="MainViewModel"/> as the dialog result.
/// </summary>
public partial class ExportDialogViewModel : ObservableObject
{
    /// <summary>All available export formats, displayed in the dialog list.</summary>
    public IReadOnlyList<ExportFormatOption> Formats { get; } = ExportFormatOption.All;

    /// <summary>The format currently selected by the user; defaults to PDF.</summary>
    [ObservableProperty] private ExportFormatOption? _selectedFormat;

    /// <summary>Initialises the dialog ViewModel with PDF selected by default.</summary>
    public ExportDialogViewModel()
    {
        SelectedFormat = Formats.Count > 0 ? Formats[0] : null;
    }
}
