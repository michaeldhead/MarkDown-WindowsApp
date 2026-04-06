using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Shell;

namespace GHSMarkdownEditor.Services;

/// <summary>
/// Manages the Windows taskbar Jump List for the application.
/// Populates recent-file entries that re-launch the app with a file path argument.
/// </summary>
public class JumpListService
{
    private string? _exePath;

    /// <summary>Gets the path to the running executable (cached after first access).</summary>
    private string ExePath =>
        _exePath ??= Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

    /// <summary>
    /// Rebuilds the taskbar Jump List from <paramref name="recentFilePaths"/>.
    /// Each entry launches the application with the file path as a command-line argument.
    /// Missing files are silently skipped.
    /// </summary>
    /// <param name="recentFilePaths">Ordered list of recent file paths (most recent first).</param>
    public void UpdateJumpList(IEnumerable<string> recentFilePaths)
    {
        var jumpList = new JumpList();

        foreach (var path in recentFilePaths.Take(10))
        {
            if (!File.Exists(path)) continue;

            jumpList.JumpItems.Add(new JumpTask
            {
                Title           = Path.GetFileName(path),
                Description     = path,
                ApplicationPath = ExePath,
                Arguments       = $"\"{path}\"",
                CustomCategory  = "Recent Files"
            });
        }

        JumpList.SetJumpList(Application.Current, jumpList);
    }
}
