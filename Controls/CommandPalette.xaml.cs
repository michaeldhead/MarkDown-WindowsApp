using GHSMarkdownEditor.ViewModels;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GHSMarkdownEditor.Controls;

/// <summary>
/// Full-width overlay panel providing fuzzy search across open tabs, document headings,
/// recent files, and application commands.  Opened with Ctrl+P; dismissed with Escape or
/// by clicking the backdrop behind the panel.
/// </summary>
public partial class CommandPalette : UserControl
{
    /// <summary>Initialises the command palette and subscribes to ViewModel selection changes.</summary>
    public CommandPalette()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // ── DataContext wiring ────────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is CommandPaletteViewModel oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is CommandPaletteViewModel newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    /// <summary>Scrolls the list to keep the selected item in view when it changes from the ViewModel.</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CommandPaletteViewModel.SelectedItem)) return;
        var vm = DataContext as CommandPaletteViewModel;
        if (vm?.SelectedItem != null)
            resultsList.ScrollIntoView(vm.SelectedItem);
    }

    // ── Focus on show ─────────────────────────────────────────────────────────

    /// <summary>Auto-focuses the search box whenever the palette becomes visible.</summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
        {
            Dispatcher.BeginInvoke(() =>
            {
                searchBox.Focus();
                searchBox.SelectAll();
            });
        }
    }

    // ── Keyboard navigation ───────────────────────────────────────────────────

    /// <summary>
    /// Handles keyboard shortcuts from the search box:
    /// Arrow keys navigate the list, Enter activates the selection, Escape closes the palette.
    /// </summary>
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not CommandPaletteViewModel vm) return;

        switch (e.Key)
        {
            case Key.Down:
                vm.MoveDownCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Up:
                vm.MoveUpCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Enter:
                vm.ActivateSelectedCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Escape:
                vm.CloseCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }

    // ── List interaction ──────────────────────────────────────────────────────

    /// <summary>Prevents category headers from being selected in the ListBox.</summary>
    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (resultsList.SelectedItem is PaletteHeader)
            resultsList.SelectedItem = null;
    }

    /// <summary>Activates the item the user double-clicks.</summary>
    private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CommandPaletteViewModel vm)
            vm.ActivateSelectedCommand.Execute(null);
    }
}
