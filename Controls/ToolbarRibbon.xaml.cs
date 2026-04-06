using System.Windows.Controls;

namespace GHSMarkdownEditor.Controls;

/// <summary>
/// Code-behind for the formatting toolbar ribbon. All toolbar actions are bound directly
/// to commands on <c>MainViewModel</c> via XAML; this file has no additional logic.
/// </summary>
public partial class ToolbarRibbon : UserControl
{
    public ToolbarRibbon()
    {
        InitializeComponent();
    }
}
