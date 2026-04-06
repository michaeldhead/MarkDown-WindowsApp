using System.Windows.Controls;

namespace GHSMarkdownEditor.Controls;

/// <summary>
/// Code-behind for the document outline panel. All behaviour — heading parsing,
/// scroll-to-heading — is driven by <c>SidebarViewModel</c>; this file is a shell.
/// </summary>
public partial class OutlinePanel : UserControl
{
    public OutlinePanel() => InitializeComponent();
}
