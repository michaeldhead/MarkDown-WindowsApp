using System.Windows.Controls;

namespace GHSMarkdownEditor.Controls;

/// <summary>
/// Full-width application menu bar containing File, View, and Help menus.
/// Inherits its DataContext from the parent window (<see cref="ViewModels.MainViewModel"/>).
/// </summary>
public partial class MenuBar : UserControl
{
    /// <summary>Initialises the menu bar.</summary>
    public MenuBar()
    {
        InitializeComponent();
    }
}
