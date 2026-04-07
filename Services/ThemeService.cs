using GHSMarkdownEditor.Models;
using MaterialDesignThemes.Wpf;
using Microsoft.Win32;
using System.Windows.Media;

namespace GHSMarkdownEditor.Services;

/// <summary>
/// The accent color used for the MaterialDesign primary and secondary palette.
/// Each value maps to a Material Design 700-hue swatch applied at runtime.
/// </summary>
public enum AccentColor
{
    Blue,
    Red,
    Orange,
    Green,
    Purple,
    Teal,
    Pink,
    DeepOrange,
    BlueGrey
}

/// <summary>
/// Applies MaterialDesign light/dark themes and persists the user's preference.
/// Exposes <see cref="ThemeChanged"/> so that components with their own theme-sensitive
/// rendering (e.g. <c>PreviewView</c>, which regenerates its CSS) can react without
/// polling or being directly coupled to this service.
/// </summary>
public class ThemeService
{
    private readonly SettingsService _settings;
    private ThemeMode _currentTheme = ThemeMode.Dark;
    private AccentColor _currentAccentColor = AccentColor.Teal;

    // Material Design 700-hue representative colors for each accent, matching the
    // swatch fill colors shown in the settings panel.
    private static readonly Dictionary<AccentColor, Color> AccentColorMap = new()
    {
        [AccentColor.Blue]       = Color.FromRgb(0x19, 0x76, 0xD2),
        [AccentColor.Red]        = Color.FromRgb(0xD3, 0x2F, 0x2F),
        [AccentColor.Orange]     = Color.FromRgb(0xF5, 0x7C, 0x00),
        [AccentColor.Green]      = Color.FromRgb(0x38, 0x8E, 0x3C),
        [AccentColor.Purple]     = Color.FromRgb(0x7B, 0x1F, 0xA2),
        [AccentColor.Teal]       = Color.FromRgb(0x00, 0x79, 0x6B),
        [AccentColor.Pink]       = Color.FromRgb(0xC2, 0x18, 0x5B),
        [AccentColor.DeepOrange] = Color.FromRgb(0xE6, 0x4A, 0x19),
        [AccentColor.BlueGrey]   = Color.FromRgb(0x45, 0x5A, 0x64),
    };

    /// <summary>
    /// Fired after a theme or accent color change has been applied and persisted.
    /// Subscribers (e.g. the preview pane) use this to re-render with updated styles.
    /// </summary>
    public event EventHandler? ThemeChanged;

    /// <summary>The currently active theme selection, which may be <see cref="ThemeMode.Auto"/>.</summary>
    public ThemeMode CurrentTheme => _currentTheme;

    /// <summary>The currently active accent color selection.</summary>
    public AccentColor CurrentAccentColor => _currentAccentColor;

    /// <summary>
    /// Resolves the effective dark/light state, expanding <see cref="ThemeMode.Auto"/>
    /// to the system registry value so callers never have to handle the Auto case themselves.
    /// </summary>
    public bool IsDark => _currentTheme == ThemeMode.Dark ||
        (_currentTheme == ThemeMode.Auto && IsSystemDarkTheme());

    /// <summary>
    /// Loads the persisted theme and accent color from settings and applies them immediately.
    /// Defaults to Dark theme with Teal accent on first launch (no settings file yet).
    /// </summary>
    public ThemeService(SettingsService settings)
    {
        _settings = settings;
        _currentTheme       = _settings.Get("Theme",       ThemeMode.Dark);
        _currentAccentColor = _settings.Get("AccentColor", AccentColor.Teal);
        ApplyTheme(_currentTheme);
        ApplyAccentColor(_currentAccentColor);
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

    /// <summary>
    /// Switches to <paramref name="color"/>, applies it as the MaterialDesign primary and
    /// secondary palette color, persists the choice, then raises <see cref="ThemeChanged"/>
    /// so dependents (including the preview CSS) can update.
    /// </summary>
    public void SetAccentColor(AccentColor color)
    {
        _currentAccentColor = color;
        ApplyAccentColor(color);
        _settings.Set("AccentColor", color);
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

    private static void ApplyAccentColor(AccentColor color)
    {
        var accentColor = AccentColorMap[color];
        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        theme.SetPrimaryColor(accentColor);
        theme.SetSecondaryColor(accentColor);
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
