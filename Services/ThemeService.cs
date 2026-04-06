using GHSMarkdownEditor.Models;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;

namespace GHSMarkdownEditor.Services;

/// <summary>
/// Applies MaterialDesign light/dark themes and persists the user's preference.
/// Exposes <see cref="ThemeChanged"/> so that components with their own theme-sensitive
/// rendering (e.g. <c>PreviewView</c>, which regenerates its CSS) can react without
/// polling or being directly coupled to this service.
/// </summary>
public class ThemeService
{
    private readonly SettingsService _settings;
    private ThemeMode _currentTheme = ThemeMode.Auto;

    /// <summary>
    /// Fired after a theme change has been applied and persisted.
    /// Subscribers (e.g. the preview pane) use this to re-render with updated styles.
    /// </summary>
    public event EventHandler? ThemeChanged;

    /// <summary>The currently active theme selection, which may be <see cref="ThemeMode.Auto"/>.</summary>
    public ThemeMode CurrentTheme => _currentTheme;

    /// <summary>
    /// Resolves the effective dark/light state, expanding <see cref="ThemeMode.Auto"/>
    /// to the system registry value so callers never have to handle the Auto case themselves.
    /// </summary>
    public bool IsDark => _currentTheme == ThemeMode.Dark ||
        (_currentTheme == ThemeMode.Auto && IsSystemDarkTheme());

    /// <summary>Loads the persisted theme from settings and applies it immediately.</summary>
    public ThemeService(SettingsService settings)
    {
        _settings = settings;
        _currentTheme = _settings.Get("Theme", ThemeMode.Auto);
        ApplyTheme(_currentTheme);
    }

    /// <summary>
    /// Switches to <paramref name="mode"/>, applies it to the MaterialDesign palette,
    /// persists the choice, then raises <see cref="ThemeChanged"/> so dependents can update.
    /// </summary>
    public void SetTheme(ThemeMode mode)
    {
        _currentTheme = mode;
        ApplyTheme(mode);
        _settings.Set("Theme", mode);
        ThemeChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyTheme(ThemeMode mode)
    {
        bool isDark = mode switch
        {
            ThemeMode.Light => false,
            ThemeMode.Dark => true,
            ThemeMode.Auto => IsSystemDarkTheme(),
            _ => false
        };

        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        theme.SetBaseTheme(isDark ? BaseTheme.Dark : BaseTheme.Light);
        paletteHelper.SetTheme(theme);
    }

    /// <summary>
    /// Reads the Windows <c>AppsUseLightTheme</c> registry value to determine the
    /// current system dark-mode preference. Returns <c>false</c> (light) on any error
    /// so the app degrades gracefully on non-standard Windows configurations.
    /// </summary>
    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intVal && intVal == 0;
        }
        catch
        {
            return false;
        }
    }
}
