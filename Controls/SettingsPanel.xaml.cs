using System.Windows.Controls;

namespace GHSMarkdownEditor.Controls;

/// <summary>
/// Code-behind for the settings panel. All behaviour — theme toggles, font size,
/// word-wrap, auto-save interval — is driven by <c>SidebarViewModel</c>; this file is a shell.
/// </summary>
public partial class SettingsPanel : UserControl
{
    public SettingsPanel() => InitializeComponent();
}
