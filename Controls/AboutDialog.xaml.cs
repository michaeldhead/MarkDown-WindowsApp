using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace GHSMarkdownEditor.Controls;

/// <summary>
/// MaterialDesign dialog content that displays application information and clickable links.
/// Shown via <c>DialogHost.Show(new AboutDialog(), "RootDialog")</c>.
/// </summary>
public partial class AboutDialog : UserControl
{
    /// <summary>Initialises the dialog.</summary>
    public AboutDialog()
    {
        InitializeComponent();
    }

    /// <summary>Opens a hyperlink in the system default browser.</summary>
    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
