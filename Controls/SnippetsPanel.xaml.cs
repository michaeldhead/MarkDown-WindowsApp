using System.Windows.Controls;

namespace GHSMarkdownEditor.Controls;

/// <summary>
/// Code-behind for the snippets panel. All behaviour — listing, inserting, adding,
/// and deleting snippets — is driven by <c>SidebarViewModel</c>; this file is a shell.
/// </summary>
public partial class SnippetsPanel : UserControl
{
    public SnippetsPanel() => InitializeComponent();
}
