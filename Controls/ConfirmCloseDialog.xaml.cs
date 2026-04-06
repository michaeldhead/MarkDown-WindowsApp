using System.Windows.Controls;

namespace GHSMarkdownEditor.Controls;

public partial class ConfirmCloseDialog : UserControl
{
    public ConfirmCloseDialog(string fileName)
    {
        InitializeComponent();
        MessageText.Text = $"\"{fileName}\" has unsaved changes. Close without saving?";
    }
}
