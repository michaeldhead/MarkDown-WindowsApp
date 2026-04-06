using GHSMarkdownEditor.ViewModels;
using System.Windows.Controls;

namespace GHSMarkdownEditor.Controls;

/// <summary>
/// MaterialDesign dialog content that lets the user choose an export format.
/// The selected <see cref="Models.ExportFormat"/> value is returned as the dialog result via
/// <c>DialogHost.CloseDialogCommand</c> with <c>CommandParameter = SelectedFormat.Format</c>.
/// </summary>
public partial class ExportDialog : UserControl
{
    /// <summary>
    /// Initialises the dialog and sets its DataContext to a new
    /// <see cref="ExportDialogViewModel"/> with PDF selected by default.
    /// </summary>
    public ExportDialog()
    {
        InitializeComponent();
        DataContext = new ExportDialogViewModel();
    }
}
