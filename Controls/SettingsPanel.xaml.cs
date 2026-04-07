using GHSMarkdownEditor.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GHSMarkdownEditor.Controls;

/// <summary>
/// Code-behind for the settings panel. Theme toggle behaviour is driven by
/// <c>SidebarViewModel</c> via data binding. Accent color swatch selection is handled
/// here directly: click → call ThemeService, ThemeChanged → update visuals.
/// </summary>
public partial class SettingsPanel : UserControl
{
    // Maps each AccentColor enum value to its named Border swatch element.
    // Built once after InitializeComponent so named elements are available.
    private Dictionary<AccentColor, Border>? _swatchMap;

    public SettingsPanel()
    {
        InitializeComponent();

        _swatchMap = new Dictionary<AccentColor, Border>
        {
            [AccentColor.Blue]       = SwatchBlue,
            [AccentColor.Red]        = SwatchRed,
            [AccentColor.Orange]     = SwatchOrange,
            [AccentColor.Green]      = SwatchGreen,
            [AccentColor.Purple]     = SwatchPurple,
            [AccentColor.Teal]       = SwatchTeal,
            [AccentColor.Pink]       = SwatchPink,
            [AccentColor.DeepOrange] = SwatchDeepOrange,
            [AccentColor.BlueGrey]   = SwatchBlueGrey,
        };

        if (App.ThemeService != null)
        {
            UpdateSwatchSelection(App.ThemeService.CurrentAccentColor);
            App.ThemeService.ThemeChanged += OnThemeChanged;
        }

        Unloaded += (_, _) =>
        {
            if (App.ThemeService != null)
                App.ThemeService.ThemeChanged -= OnThemeChanged;
        };
    }

    private void OnSwatchClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border border &&
            border.Tag is string tag &&
            Enum.TryParse<AccentColor>(tag, out var color))
        {
            App.ThemeService?.SetAccentColor(color);
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (App.ThemeService != null)
            UpdateSwatchSelection(App.ThemeService.CurrentAccentColor);
    }

    /// <summary>
    /// Applies the selected/deselected visual state to all swatches:
    /// the active swatch gets a 2 px white border; all others are reset to no border.
    /// </summary>
    private void UpdateSwatchSelection(AccentColor selected)
    {
        if (_swatchMap == null) return;
        foreach (var (color, border) in _swatchMap)
        {
            bool isSelected = color == selected;
            border.BorderBrush     = isSelected ? Brushes.White : null;
            border.BorderThickness = isSelected ? new Thickness(2) : new Thickness(0);
        }
    }
}
