using GHSMarkdownEditor.ViewModels;
using System.ComponentModel;
using System.Windows;

namespace GHSMarkdownEditor.Views;

/// <summary>
/// Standalone preview window that mirrors the live rendered Markdown from the main editor.
/// Opens via the View → Detach Preview menu (or toolbar button) and stays synchronised with
/// content changes and theme switches.  When this window is closed, the main window reverts
/// to Split mode.
/// </summary>
public partial class DetachedPreview : Window
{
    private readonly MainViewModel _mainVm;
    private bool _webViewReady;
    private DocumentTabViewModel? _subscribedTab;
    private CancellationTokenSource _cts = new();

    /// <summary>
    /// Creates a new detached preview window bound to the supplied main ViewModel.
    /// </summary>
    /// <param name="mainVm">The application's main ViewModel (source of content and theme).</param>
    public DetachedPreview(MainViewModel mainVm)
    {
        _mainVm = mainVm;
        InitializeComponent();
        Loaded   += OnLoaded;
        Unloaded += OnUnloaded;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_webViewReady) return;

        try
        {
            await webBrowser.EnsureCoreWebView2Async();
            _webViewReady = true;

            webBrowser.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            webBrowser.CoreWebView2.Settings.IsStatusBarEnabled            = false;
            webBrowser.CoreWebView2.Settings.AreDevToolsEnabled            = false;

            SubscribeToViewModel();
            App.ThemeService.ThemeChanged += OnThemeChanged;
            RenderNow();
        }
        catch { /* WebView2 unavailable */ }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        App.ThemeService.ThemeChanged -= OnThemeChanged;
        UnsubscribeFromTab(_subscribedTab);
        _mainVm.PropertyChanged -= OnMainVmPropertyChanged;
        _cts.Cancel();
    }

    // ── ViewModel subscriptions ───────────────────────────────────────────────

    private void SubscribeToViewModel()
    {
        _mainVm.PropertyChanged += OnMainVmPropertyChanged;
        SubscribeToTab(_mainVm.ActiveTab);
    }

    private void OnMainVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ActiveTab)) return;
        SubscribeToTab(_mainVm.ActiveTab);
        RenderNow();
    }

    private void SubscribeToTab(DocumentTabViewModel? tab)
    {
        UnsubscribeFromTab(_subscribedTab);
        _subscribedTab = tab;
        if (_subscribedTab != null)
            _subscribedTab.PropertyChanged += OnTabPropertyChanged;
    }

    private void UnsubscribeFromTab(DocumentTabViewModel? tab)
    {
        if (tab != null)
            tab.PropertyChanged -= OnTabPropertyChanged;
    }

    private void OnTabPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentTabViewModel.Content))
            _ = RenderDebounced();
    }

    // ── Rendering ─────────────────────────────────────────────────────────────

    private void OnThemeChanged(object? sender, EventArgs e) => RenderNow();

    /// <summary>Re-renders after a 300 ms debounce to avoid hammering the WebView2 on each keystroke.</summary>
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

    /// <summary>Immediately renders the active document's Markdown to the WebView2.</summary>
    private void RenderNow()
    {
        if (!_webViewReady) return;
        var content = _mainVm.ActiveTab?.Content ?? string.Empty;
        var isDark   = App.ThemeService?.IsDark ?? false;
        var html     = App.MarkdownService.ToHtml(content, isDark);
        webBrowser.NavigateToString(html);
    }
}
