using GHSMarkdownEditor.ViewModels;
using MaterialDesignThemes.Wpf;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GHSMarkdownEditor.Controls;

public partial class LanguagePickerPopup : UserControl
{
    public LanguagePickerPopup(string? currentLanguage = null)
    {
        InitializeComponent();
        DataContext = new LanguagePickerViewModel(currentLanguage);
        Loaded += (_, _) => SearchBox.Focus();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            DialogHost.CloseDialogCommand.Execute(null, this);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
