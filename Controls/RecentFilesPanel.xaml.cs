using System.Windows.Controls;

namespace GHSMarkdownEditor.Controls;

/// <summary>
/// Code-behind for the recent files panel. All behaviour — listing, clearing, opening
/// recent files — is driven by <c>SidebarViewModel</c>; this file is a shell.
/// </summary>
public partial class RecentFilesPanel : UserControl
{
    public RecentFilesPanel() => InitializeComponent();
}
