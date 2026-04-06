using Microsoft.Web.WebView2.Wpf;
using System.Windows;
using System.Windows.Controls;

namespace GHSMarkdownEditor.Services;

/// <summary>
/// Renders the active Markdown document to HTML and prints it via the browser print dialog.
/// </summary>
/// <remarks>
/// Implementation choice: a temporary WebView2 host window is created, the HTML is navigated
/// to it, and <c>window.print()</c> is executed via <c>ExecuteScriptAsync</c> once navigation
/// completes. In Chromium (the WebView2 runtime), <c>window.print()</c> is synchronous in the
/// JS execution context — it blocks until the user dismisses the print dialog — so
/// <c>ExecuteScriptAsync</c> reliably awaits the entire print interaction.  The host window is
/// then closed automatically.  This produces higher-fidelity output than a WPF FlowDocument
/// because the rendered HTML/CSS is identical to the live preview.
/// </remarks>
public sealed class PrintService
{
    private bool _isPrinting;

    /// <summary>
    /// Renders <paramref name="markdown"/> and opens the system print dialog.
    /// If a print is already in progress, the call is silently ignored.
    /// </summary>
    public async Task PrintAsync(string markdown, bool isDark)
    {
        if (_isPrinting) return;
        _isPrinting = true;

        try
        {
            var html = App.MarkdownService.ToHtml(markdown, isDark);
            await RunPrintWindowAsync(html);
        }
        finally
        {
            _isPrinting = false;
        }
    }

    /// <summary>
    /// Creates a minimal host window containing a WebView2, navigates to the HTML content,
    /// triggers <c>window.print()</c>, and closes the window after the dialog is dismissed.
    /// </summary>
    private static async Task RunPrintWindowAsync(string html)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var statusLabel = new TextBlock
        {
            Text                = "Preparing document for printing…",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment   = VerticalAlignment.Center,
            FontSize            = 14,
            Margin              = new Thickness(20)
        };

        // WebView2 requires a visible HWND to render content, so we create a real window.
        // It is sized modestly and positioned near the centre of the screen.
        var win = new Window
        {
            Title                      = "Printing…",
            Width                      = 480,
            Height                     = 200,
            WindowStartupLocation      = WindowStartupLocation.CenterScreen,
            ShowInTaskbar              = false,
            WindowStyle                = WindowStyle.ToolWindow,
            ResizeMode                 = ResizeMode.NoResize,
            Owner                      = Application.Current.MainWindow
        };

        // The WebView2 is hidden inside the window while the label provides user feedback.
        var webView = new WebView2 { Visibility = Visibility.Collapsed };
        var grid    = new Grid();
        grid.Children.Add(statusLabel);
        grid.Children.Add(webView);
        win.Content = grid;

        win.Closed += (_, _) => tcs.TrySetResult(true);
        win.Show();

        try
        {
            await webView.EnsureCoreWebView2Async();
            webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webView.CoreWebView2.Settings.AreDevToolsEnabled            = false;

            bool handled = false;
            webView.NavigationCompleted += async (_, _) =>
            {
                // Guard: NavigationCompleted can fire more than once.
                if (handled) return;
                handled = true;

                statusLabel.Text = "Print dialog is open — close when done.";

                // window.print() is synchronous in Chromium; ExecuteScriptAsync awaits it.
                await webView.ExecuteScriptAsync("window.print()");
                win.Close();
            };

            webView.NavigateToString(html);
            await tcs.Task;
        }
        catch
        {
            try { win.Close(); } catch { /* best effort */ }
        }
    }
}
