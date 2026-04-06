using GHSMarkdownEditor.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace GHSMarkdownEditor.Controls;

/// <summary>
/// Code-behind for the horizontal tab strip. Tab activation and close are handled here
/// in code-behind rather than commands because the close button sits inside the tab
/// button's visual tree, requiring <c>e.Handled = true</c> to prevent the click from
/// bubbling up to the parent tab button and triggering an unwanted activation.
/// </summary>
public partial class TabBar : UserControl
{
    public TabBar()
    {
        InitializeComponent();
    }

    private void TabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is DocumentTabViewModel tab)
        {
            if (DataContext is MainViewModel vm)
                vm.SetActiveTab(tab);
        }
    }

    /// <summary>
    /// Closes the tab whose close button was clicked.
    /// <c>e.Handled = true</c> stops the click from bubbling to the enclosing tab button,
    /// which would otherwise re-activate the tab being closed.
    /// </summary>
    private async void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true; // prevent bubbling to TabButton_Click
        if (sender is Button btn && btn.Tag is DocumentTabViewModel tab)
        {
            if (DataContext is MainViewModel vm)
                await vm.CloseTabCommand.ExecuteAsync(tab);
        }
    }
}
