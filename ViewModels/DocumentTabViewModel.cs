using CommunityToolkit.Mvvm.ComponentModel;

namespace GHSMarkdownEditor.ViewModels;

/// <summary>
/// Represents a single open document tab. Tracks content, file path, and dirty state.
/// Dirty state is computed by comparing <see cref="Content"/> against a snapshot taken
/// at the last save; the content is never automatically reset to empty on save so that
/// undo history in the editor is preserved.
/// </summary>
public partial class DocumentTabViewModel : ObservableObject
{
    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _fileName = "Untitled";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDirty))]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private string _content = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayName))]
    private bool _isActive;

    /// <summary>Snapshot of content at the time of the last save, used to compute <see cref="IsDirty"/>.</summary>
    private string _savedContent = string.Empty;

    /// <summary>
    /// True when <see cref="Content"/> differs from the last saved snapshot.
    /// Drives the bullet-prefix on the tab header to signal unsaved changes.
    /// </summary>
    public bool IsDirty => Content != _savedContent;

    /// <summary>
    /// Tab header text. A leading bullet (•) is prepended when the document is dirty
    /// so the user can see unsaved state at a glance without reading the title bar.
    /// </summary>
    public string DisplayName => IsDirty ? $"• {FileName}" : FileName;

    partial void OnContentChanged(string value)
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DisplayName));
    }

    /// <summary>
    /// Updates the saved-content snapshot so <see cref="IsDirty"/> returns false.
    /// If <paramref name="filePath"/> is supplied the tab's path and display name are
    /// also updated to reflect the newly saved location (used by Save As).
    /// </summary>
    /// <param name="filePath">New file path, or <c>null</c> to keep the current path.</param>
    public void MarkSaved(string? filePath = null)
    {
        _savedContent = Content;
        if (filePath != null)
        {
            FilePath = filePath;
            FileName = System.IO.Path.GetFileName(filePath);
        }
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(DisplayName));
    }
}
