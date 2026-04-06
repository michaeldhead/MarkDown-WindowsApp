using Microsoft.Win32;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;

namespace GHSMarkdownEditor.Services;

/// <summary>
/// Registers the application as the default handler for <c>.md</c> files via the
/// Windows registry (HKCU).  Shows a one-time confirmation prompt on the first launch
/// and persists the response so the user is never asked again.
/// </summary>
public class FileAssociationService
{
    private const string ProgId      = "GHSMarkdownEditor.md";
    private const string SettingsKey = "FileAssociationAsked";

    private readonly SettingsService _settings;

    /// <summary>Initialises the service with the application settings store.</summary>
    public FileAssociationService(SettingsService settings) => _settings = settings;

    /// <summary>
    /// Shows a one-time dialog asking the user whether to register <c>.md</c> files.
    /// If the user accepts and the registration succeeds, the shell is notified.
    /// Subsequent calls are no-ops (the prompt is only shown once).
    /// </summary>
    public void RegisterIfNeeded()
    {
        // Only ask once — persist the flag regardless of the user's answer.
        if (_settings.Get(SettingsKey, false)) return;
        _settings.Set(SettingsKey, true);

        if (IsRegistered()) return;

        var result = MessageBox.Show(
            "Register GHS Markdown Editor as the default app for .md files?\n\n" +
            "This can be changed later in Windows Settings → Default Apps.",
            "File Association",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        if (result == MessageBoxResult.Yes)
            Register();
    }

    /// <summary>
    /// Returns <see langword="true"/> if the application is already registered as the
    /// default handler for <c>.md</c> files in the current user's registry.
    /// </summary>
    public bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.md");
        return key?.GetValue(null) as string == ProgId;
    }

    /// <summary>
    /// Writes the ProgID and file-extension keys to HKCU\Software\Classes and notifies
    /// the Windows shell that file associations have changed.
    /// </summary>
    private void Register()
    {
        var exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        if (string.IsNullOrEmpty(exePath)) return;

        try
        {
            // ProgID: HKCU\Software\Classes\GHSMarkdownEditor.md
            using (var progKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            {
                progKey.SetValue(null, "Markdown File");

                using var iconKey = progKey.CreateSubKey("DefaultIcon");
                iconKey.SetValue(null, $"{exePath},0");

                using var cmdKey = progKey.CreateSubKey(@"shell\open\command");
                cmdKey.SetValue(null, $"\"{exePath}\" \"%1\"");
            }

            // Extension association: HKCU\Software\Classes\.md → ProgID
            using (var extKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\.md"))
            {
                extKey.SetValue(null, ProgId);
            }

            // Tell Windows Shell to refresh its association cache.
            NativeMethods.SHChangeNotify(0x08000000, 0x0000, nint.Zero, nint.Zero);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not register file association:\n{ex.Message}",
                "Registration Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    // ── P/Invoke ─────────────────────────────────────────────────────────────

    private static class NativeMethods
    {
        /// <summary>Notifies the shell that file associations have changed (SHCNE_ASSOCCHANGED).</summary>
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        internal static extern void SHChangeNotify(uint wEventId, uint uFlags, nint dwItem1, nint dwItem2);
    }
}
