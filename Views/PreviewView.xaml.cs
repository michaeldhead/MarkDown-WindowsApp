using GHSMarkdownEditor.Models;
using GHSMarkdownEditor.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace GHSMarkdownEditor.Views;

/// <summary>
/// Code-behind for the WebView2 Markdown preview pane. Manages asynchronous WebView2
/// initialisation, re-renders on content change via a debounced pipeline, preserves the
/// scroll position across re-renders, and forwards keyboard shortcuts from the hosted page
/// back to the host application.
/// </summary>
public partial class PreviewView : UserControl
{
    /// <summary>
    /// Set to true once <see cref="Microsoft.Web.WebView2.Wpf.WebView2.EnsureCoreWebView2Async"/>
    /// completes. Guards against calling <c>ExecuteScriptAsync</c> or <c>NavigateToString</c>
    /// before the core is ready, which would throw. Also prevents duplicate initialisation
    /// if <c>Loaded</c> fires more than once (e.g. when the control is re-parented).
    /// </summary>
    private bool _webViewReady;
    private CancellationTokenSource _cts = new();
    private DocumentTabViewModel? _subscribedTab;

    // Exposed for SplitView scroll sync
    internal WebView2 WebBrowser => webBrowser;

    // JavaScript injected into every preview page to forward Ctrl+key combos to the host.
    // AddScriptToExecuteOnDocumentCreatedAsync re-runs this on every NavigateToString call.
    // Only the specific keys we need are intercepted; all other browser shortcuts are left alone.
    private const string KeyForwardScript = """
        document.addEventListener('keydown', function(e) {
            if (!e.ctrlKey) return;
            var key = e.key.toLowerCase();
            var handled = ['1','2','3','n','o','s','w','b','i','p'];
            if (handled.indexOf(key) === -1) return;
            var msg = 'ctrl' + (e.shiftKey ? '+shift' : '') + '+' + key;
            window.chrome.webview.postMessage(msg);
            e.preventDefault();
        });
        """;

    public PreviewView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>
    /// Initialises the WebView2 core asynchronously and wires up all event subscriptions.
    /// Must be <c>async void</c> because it is an event handler; exceptions from
    /// <c>EnsureCoreWebView2Async</c> are caught broadly to handle design-time and
    /// test environments where WebView2 is unavailable. The <see cref="_webViewReady"/>
    /// guard prevents re-initialisation if <c>Loaded</c> fires again after re-parenting.
    /// </summary>
    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Guard against duplicate initialisation (Loaded can fire more than once
        // if the control is removed from and re-added to the visual tree).
        if (_webViewReady) return;

        try
        {
            await webBrowser.EnsureCoreWebView2Async();
            _webViewReady = true;

            webBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webBrowser.CoreWebView2.Settings.IsStatusBarEnabled = false;
            webBrowser.CoreWebView2.Settings.AreDevToolsEnabled = false;

            // Register the keydown forwarder to run on every document creation,
            // including every subsequent NavigateToString call.
            await webBrowser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(KeyForwardScript);
            webBrowser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

            SubscribeToViewModel();
            if (App.ThemeService != null)
                App.ThemeService.ThemeChanged += OnThemeChanged;
            RenderNow();
        }
        catch { /* WebView2 not available in design-time or test environments */ }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (App.ThemeService != null)
            App.ThemeService.ThemeChanged -= OnThemeChanged;

        if (_webViewReady)
            webBrowser.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
    }

    // Receives forwarded keyboard shortcuts from the JavaScript keydown listener.
    // WebView2 may fire this on a background thread, so all command dispatch is
    // marshalled to the UI thread via Dispatcher.Invoke.
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var msg = e.TryGetWebMessageAsString();
        Debug.WriteLine($"[PreviewView] WebMessage received: {msg}");

        Dispatcher.Invoke(() =>
        {
            Debug.WriteLine($"[PreviewView] MainWindow type: {Application.Current.MainWindow?.GetType().Name}");
            Debug.WriteLine($"[PreviewView] MainWindow.DataContext type: {Application.Current.MainWindow?.DataContext?.GetType().Name ?? "NULL"}");

            var vm = GetMainViewModel();
            if (vm == null)
            {
                Debug.WriteLine("[PreviewView] MainViewModel is null — command not dispatched");
                return;
            }

            Debug.WriteLine($"[PreviewView] Dispatching command for: {msg}");
            switch (msg)
            {
                // View mode
                case "ctrl+1": vm.SetViewModeWriteCommand.Execute(null);   break;
                case "ctrl+2": vm.SetViewModeSplitCommand.Execute(null);   break;
                case "ctrl+3": vm.SetViewModePreviewCommand.Execute(null); break;

                // File operations
                case "ctrl+n": vm.NewTabCommand.Execute(null);             break;
                case "ctrl+o": vm.OpenFileCommand.Execute(null);           break;
                case "ctrl+s": vm.SaveFileCommand.Execute(null);           break;
                case "ctrl+shift+s": vm.SaveFileAsCommand.Execute(null);   break;
                case "ctrl+w": vm.CloseActiveTabCommand.Execute(null);     break;

                // Formatting
                case "ctrl+b": vm.BoldCommand.Execute(null);               break;
                case "ctrl+i": vm.ItalicCommand.Execute(null);             break;

                // Phase 5
                case "ctrl+p":       vm.OpenCommandPaletteCommand.Execute(null); break;
                case "ctrl+shift+p": vm.PrintCommand.Execute(null);              break;
            }
        });
    }

    // Use Application.Current.MainWindow rather than Window.GetWindow(this) so the
    // lookup works regardless of where PreviewView sits in the visual tree and
    // regardless of whether its own DataContext has been set.
    private static MainViewModel? GetMainViewModel() =>
        Application.Current.MainWindow?.DataContext as MainViewModel;

    private void OnThemeChanged(object? sender, EventArgs e) => RenderNow(preserveScroll: false);

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue && _webViewReady)
            RenderNow();
    }

    private MainViewModel? GetViewModel() => DataContext as MainViewModel;

    private void SubscribeToViewModel()
    {
        var vm = GetViewModel();
        if (vm == null) return;
        vm.PropertyChanged += OnViewModelPropertyChanged;
        SubscribeToTab(vm.ActiveTab);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveTab))
        {
            var vm = GetViewModel();
            SubscribeToTab(vm?.ActiveTab);
            RenderNow();
        }
    }

    private void SubscribeToTab(DocumentTabViewModel? tab)
    {
        if (_subscribedTab != null)
            _subscribedTab.PropertyChanged -= OnTabPropertyChanged;
        _subscribedTab = tab;
        if (_subscribedTab != null)
            _subscribedTab.PropertyChanged += OnTabPropertyChanged;
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentTabViewModel.Content))
            _ = RenderDebounced();
    }

    /// <summary>
    /// Delays rendering by 300 ms and cancels any pending render if content changes again
    /// within that window. Uses a replace-the-CTS pattern rather than a timer to avoid
    /// accumulating tasks when the user types quickly.
    /// </summary>
    private async Task RenderDebounced()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        try
        {
            await Task.Delay(300, token);
            if (!token.IsCancellationRequested)
                RenderNow();
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Re-renders the current content immediately. When <paramref name="preserveScroll"/> is
    /// true the scroll position is captured before navigation and restored via a one-shot
    /// <see cref="Microsoft.Web.WebView2.Core.CoreWebView2.NavigationCompleted"/> handler
    /// that unsubscribes itself immediately after firing. This prevents the preview from
    /// jumping to the top on every debounced keystroke.
    /// Pass <c>false</c> for theme changes, where resetting to the top is the correct behaviour.
    /// </summary>
    private async void RenderNow(bool preserveScroll = true)
    {
        if (!_webViewReady || !IsVisible) return;
        var vm = GetViewModel();
        var content = vm?.ActiveTab?.Content ?? string.Empty;
        var isDark = App.ThemeService?.IsDark ?? false;
        var html = App.MarkdownService.ToHtml(content, isDark);

        double scrollY = 0;
        if (preserveScroll)
        {
            try
            {
                var scrollJson = await webBrowser.ExecuteScriptAsync("window.scrollY");
                double.TryParse(scrollJson, out scrollY);
            }
            catch { scrollY = 0; }
        }

        if (preserveScroll && scrollY > 0)
        {
            EventHandler<CoreWebView2NavigationCompletedEventArgs>? handler = null;
            handler = async (s, e) =>
            {
                webBrowser.NavigationCompleted -= handler;
                try
                {
                    await webBrowser.ExecuteScriptAsync(
                        $"window.scrollTo(0, {scrollY.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
                }
                catch { }
            };
            webBrowser.NavigationCompleted += handler;
        }

        webBrowser.NavigateToString(html);
    }

    /// <summary>
    /// Scrolls the WebView2 to a proportional position (0 = top, 1 = bottom).
    /// Called by <c>SplitView</c> to keep the preview in sync with the editor scroll.
    /// Failures are ignored because the WebView2 may not yet have content.
    /// </summary>
    internal async Task ScrollToRatio(double ratio)
    {
        if (!_webViewReady) return;
        try
        {
            var script = $"window.scrollTo(0, {ratio.ToString(System.Globalization.CultureInfo.InvariantCulture)} * (document.body.scrollHeight - window.innerHeight));";
            await webBrowser.ExecuteScriptAsync(script);
        }
        catch { /* ignore scroll errors */ }
    }
}
