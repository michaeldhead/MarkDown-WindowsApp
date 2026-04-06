using GHSMarkdownEditor.ViewModels;
using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace GHSMarkdownEditor.Services;

/// <summary>
/// Handles all file I/O: creating new tabs, showing Open/Save dialogs, writing content
/// to disk, and maintaining the recent-files list in settings. Returns
/// <see cref="DocumentTabViewModel"/> instances rather than raw strings so callers get
/// a fully initialised tab (path, name, saved snapshot) in one step.
/// </summary>
public class FileService
{
    private const string OpenFilter  = "Markdown Files (*.md)|*.md|Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
    private const string SaveFilter  = "Markdown Files (*.md)|*.md|All Files (*.*)|*.*";

    private readonly SettingsService _settings;

    public FileService(SettingsService settings)
    {
        _settings = settings;
    }

    /// <summary>Creates a new blank tab with default "Untitled" state.</summary>
    public DocumentTabViewModel CreateNew() => new DocumentTabViewModel();

    /// <summary>Shows Open dialog and returns a loaded tab, or null if cancelled.</summary>
    public DocumentTabViewModel? OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open File",
            Filter = OpenFilter,
            DefaultExt = ".md"
        };

        if (dialog.ShowDialog() != true) return null;

        try
        {
            var content = File.ReadAllText(dialog.FileName);
            var tab = new DocumentTabViewModel { Content = content };
            tab.MarkSaved(dialog.FileName);
            AddToRecentFiles(dialog.FileName);
            return tab;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open file:\n{ex.Message}", "Open Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    /// <summary>Saves tab to its existing path; calls SaveFileAs if Untitled.</summary>
    public bool SaveFile(DocumentTabViewModel tab)
    {
        if (!string.IsNullOrEmpty(tab.FilePath))
            return WriteToDisk(tab, tab.FilePath);

        return SaveFileAs(tab);
    }

    /// <summary>Shows Save As dialog and writes the file.</summary>
    public bool SaveFileAs(DocumentTabViewModel tab)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save File As",
            Filter = SaveFilter,
            DefaultExt = ".md",
            FileName = tab.FileName
        };

        if (!string.IsNullOrEmpty(tab.FilePath))
            dialog.InitialDirectory = Path.GetDirectoryName(tab.FilePath);

        if (dialog.ShowDialog() != true) return false;

        return WriteToDisk(tab, dialog.FileName);
    }

    /// <summary>
    /// Writes content to disk and calls <see cref="DocumentTabViewModel.MarkSaved"/> to
    /// update the tab's path, name, and dirty snapshot in one atomic step.
    /// </summary>
    private bool WriteToDisk(DocumentTabViewModel tab, string path)
    {
        try
        {
            File.WriteAllText(path, tab.Content);
            tab.MarkSaved(path);
            AddToRecentFiles(path);
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save file:\n{ex.Message}", "Save Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    /// <summary>Opens a file directly from a known path (no dialog). Returns null on error.</summary>
    public DocumentTabViewModel? OpenFromPath(string path)
    {
        try
        {
            var content = File.ReadAllText(path);
            var tab = new DocumentTabViewModel { Content = content };
            tab.MarkSaved(path);
            AddToRecentFiles(path);
            return tab;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to open file:\n{ex.Message}", "Open Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }

    /// <summary>
    /// Inserts <paramref name="path"/> at the top of the recent-files list, removes any
    /// duplicate entry, and trims the list to 10 items before persisting.
    /// </summary>
    private void AddToRecentFiles(string path)
    {
        var recent = _settings.Get("RecentFiles", new List<string>());
        recent.Remove(path);
        recent.Insert(0, path);
        if (recent.Count > 10)
            recent = recent.Take(10).ToList();
        _settings.Set("RecentFiles", recent);
    }
}
