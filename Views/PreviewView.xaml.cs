using GHSMarkdownEditor.Models;
using GHSMarkdownEditor.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace GHSMarkdownEditor.Views;

/// <summary>
/// Carries a request for the raw Markdown source of a preview block.
/// <see cref="ProvideMarkdown"/> must be called with the raw Markdown text to pre-fill
/// the inline-edit textarea.
/// </summary>
public sealed class InlineEditRequestedEventArgs : EventArgs
{
    public int LineNumber { get; }
    public Action<string> ProvideMarkdown { get; }

    public InlineEditRequestedEventArgs(int lineNumber, Action<string> provideMarkdown)
    {
        LineNumber = lineNumber;
        ProvideMarkdown = provideMarkdown;
    }
}

/// <summary>
/// Carries the updated Markdown text from an inline-edit Save action.
/// </summary>
public sealed class InlineEditSavedEventArgs : EventArgs
{
    public int LineNumber { get; }
    public string NewMarkdown { get; }

    public InlineEditSavedEventArgs(int lineNumber, string newMarkdown)
    {
        LineNumber = lineNumber;
        NewMarkdown = newMarkdown;
    }
}

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

    /// <summary>
    /// True while <see cref="ScrollToRatio"/> is executing a programmatic scroll.
    /// Suppresses <see cref="PreviewScrolled"/> for scroll messages that originate from
    /// the programmatic scroll itself, preventing a ping-pong feedback loop with SplitView.
    /// Cleared on the Background dispatcher priority so that the WebMessageReceived event
    /// (which is queued by the JS postMessage call) is suppressed before the flag resets.
    /// </summary>
    private bool _isProgrammaticScroll;

    /// <summary>
    /// Raised when the user scrolls the preview pane. Not raised during a programmatic
    /// scroll triggered by <see cref="ScrollToRatio"/>. The argument is a 0–1 ratio
    /// (0 = top, 1 = bottom). Consumed by <c>SplitView</c> to mirror the scroll in the editor.
    /// </summary>
    public event EventHandler<double>? PreviewScrolled;

    /// <summary>
    /// Raised when the user double-clicks a block and the inline editor requests the raw
    /// Markdown source. The handler must call <see cref="InlineEditRequestedEventArgs.ProvideMarkdown"/>
    /// with the block's Markdown text to pre-fill the textarea.
    /// </summary>
    public event EventHandler<InlineEditRequestedEventArgs>? InlineEditRequested;

    /// <summary>
    /// Raised when the user clicks Save in the inline editor overlay.
    /// The handler should replace the corresponding lines in the backing document.
    /// </summary>
    public event EventHandler<InlineEditSavedEventArgs>? InlineEditSaved;

    /// <summary>
    /// Raised when the user single-clicks a block in the preview pane (outside any inline-edit
    /// overlay). The argument is the 1-based source line number from the block's
    /// <c>data-source-line</c> attribute. Consumed by <c>SplitView</c> to sync the editor cursor.
    /// </summary>
    public event EventHandler<int>? PreviewBlockClicked;

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

    // JavaScript injected into every preview page to post the scroll ratio to the host.
    // The ratio (0–1) is computed from window.scrollY relative to the scrollable height.
    // The guard "(... || 1)" avoids division-by-zero on short documents that don't scroll.
    private const string ScrollListenerScript = """
        window.addEventListener('scroll', function() {
            var ratio = window.scrollY / (document.body.scrollHeight - window.innerHeight || 1);
            window.chrome.webview.postMessage('scroll:' + ratio);
        });
        """;

    // JavaScript injected into every preview page to post the source line when the user
    // single-clicks a block. Clicks inside an active inline-edit textarea or its overlay
    // are ignored so they do not interfere with inline editing.
    private const string ClickSyncScript = """
        document.addEventListener('click', function(e) {
            if (e.target.tagName === 'TEXTAREA') return;
            if (e.target.closest('.inline-edit-overlay')) return;
            var block = e.target.closest('[data-source-line]');
            if (!block) return;
            var lineNum = block.getAttribute('data-source-line');
            window.chrome.webview.postMessage('click:' + lineNum);
        });
        """;

    // JavaScript injected into every preview page to handle double-click inline editing.
    // On dblclick a block gets replaced with a textarea pre-filled with the raw Markdown
    // (provided asynchronously by the host via ExecuteScriptAsync). Save posts editsave:
    // with the new text; Cancel restores the original HTML. The guard on querySelector('textarea')
    // prevents re-entering edit mode when the block is already being edited.
    private const string InlineEditScript = """
        (function() {
            var activeBlock = null;
            var activeOriginalHTML = null;

            function cancelActiveEdit() {
                if (activeBlock) {
                    activeBlock.innerHTML = activeOriginalHTML;
                    activeBlock.classList.remove('inline-edit-overlay');
                    activeBlock = null;
                    activeOriginalHTML = null;
                }
            }

            document.addEventListener('dblclick', function(e) {
                var block = e.target.closest('[data-source-line]');
                if (!block) {
                    cancelActiveEdit();
                    return;
                }

                // Double-clicking the same block already in edit mode — do nothing
                if (block === activeBlock) return;

                // Cancel any currently open editor before opening a new one
                cancelActiveEdit();

                var lineNum = block.getAttribute('data-source-line');
                activeBlock = block;
                activeOriginalHTML = block.innerHTML;

                block.classList.add('inline-edit-overlay');
                block.innerHTML =
                    '<textarea id="inline-edit-ta"></textarea>' +
                    '<div class="inline-edit-actions">' +
                    '<button class="inline-edit-cancel">Cancel</button>' +
                    '<button class="inline-edit-save">Save</button>' +
                    '</div>';

                var ta = block.querySelector('textarea');
                ta.focus();

                window.chrome.webview.postMessage('editrequest:' + lineNum);

                block.querySelector('.inline-edit-save')
                    .addEventListener('click', function() {
                        var newText = ta.value;
                        window.chrome.webview.postMessage('editsave:' + lineNum + ':' + newText);
                        activeBlock = null;
                        activeOriginalHTML = null;
                        block.classList.remove('inline-edit-overlay');
                    });

                block.querySelector('.inline-edit-cancel')
                    .addEventListener('click', function() {
                        cancelActiveEdit();
                    });
            });
        })();
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

            // Register the keydown forwarder, scroll listener, and inline-edit handler to run
            // on every document creation, including every subsequent NavigateToString call.
            await webBrowser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(KeyForwardScript);
            await webBrowser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ScrollListenerScript);
            await webBrowser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(ClickSyncScript);
            await webBrowser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(InlineEditScript);
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

    // Receives messages posted by the JavaScript listeners (keydown forwarder and scroll
    // listener). WebView2 may fire this on a background thread, so all dispatch is
    // marshalled to the UI thread via Dispatcher.Invoke.
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var msg = e.TryGetWebMessageAsString();

        Dispatcher.Invoke(() =>
        {
            // Scroll messages from the preview scroll listener — raise PreviewScrolled unless
            // the scroll was triggered programmatically by ScrollToRatio (guard suppresses it).
            if (msg.StartsWith("scroll:"))
            {
                if (!_isProgrammaticScroll &&
                    double.TryParse(msg[7..], System.Globalization.CultureInfo.InvariantCulture,
                        out double scrollRatio))
                {
                    PreviewScrolled?.Invoke(this, scrollRatio);
                }
                return;
            }

            // Single-click on a preview block — raise PreviewBlockClicked so SplitView can
            // sync the editor cursor to the corresponding source line.
            if (msg.StartsWith("click:"))
            {
                if (int.TryParse(msg["click:".Length..], out int clickLineNumber))
                    PreviewBlockClicked?.Invoke(this, clickLineNumber);
                return;
            }

            // Inline-edit request: the JS double-click handler wants the raw Markdown for a block.
            // Raise InlineEditRequested with a callback that writes the text into the textarea.
            if (msg.StartsWith("editrequest:"))
            {
                if (!int.TryParse(msg["editrequest:".Length..], out int editLineNumber)) return;
                Action<string> provideMarkdown = async (markdown) =>
                {
                    try
                    {
                        await webBrowser.ExecuteScriptAsync(
                            $"document.getElementById('inline-edit-ta').value = {System.Text.Json.JsonSerializer.Serialize(markdown)};");
                    }
                    catch { }
                };
                InlineEditRequested?.Invoke(this, new InlineEditRequestedEventArgs(editLineNumber, provideMarkdown));
                return;
            }

            // Inline-edit save: the JS Save button was clicked; raise InlineEditSaved with the
            // new Markdown text. Format is "editsave:lineNum:text" where text may contain colons.
            if (msg.StartsWith("editsave:"))
            {
                var rest = msg["editsave:".Length..];
                var colonIdx = rest.IndexOf(':');
                if (colonIdx < 0 || !int.TryParse(rest[..colonIdx], out int saveLineNumber)) return;
                var newMarkdown = rest[(colonIdx + 1)..];
                InlineEditSaved?.Invoke(this, new InlineEditSavedEventArgs(saveLineNumber, newMarkdown));
                return;
            }

            Debug.WriteLine($"[PreviewView] WebMessage received: {msg}");
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
    /// Highlights the preview block that best matches <paramref name="lineNumber"/> by
    /// adding the <c>active-block</c> CSS class (2 px blue outline) to the element whose
    /// <c>data-source-line</c> attribute is the highest value ≤ <paramref name="lineNumber"/>.
    /// Any previously highlighted block is de-highlighted first.
    /// Called by <c>SplitView</c> when the editor cursor line changes.
    /// Failures are silently ignored — the WebView2 may not yet have content or may be
    /// navigating when this is called.
    /// </summary>
    internal async Task HighlightSourceLine(int lineNumber)
    {
        if (!_webViewReady) return;
        // $$""" raw string: single { } are literal JS braces; {{lineNumber}} is the C# interpolation.
        var script = $$"""
            (function() {
                document.querySelectorAll('.active-block').forEach(function(el) {
                    el.classList.remove('active-block');
                });
                var elements = Array.from(document.querySelectorAll('[data-source-line]'));
                var best = null;
                var bestLine = -1;
                elements.forEach(function(el) {
                    var line = parseInt(el.getAttribute('data-source-line'), 10);
                    if (line <= {{lineNumber}} && line > bestLine) {
                        bestLine = line;
                        best = el;
                    }
                });
                if (best) best.classList.add('active-block');
            })();
            """;
        try
        {
            await webBrowser.ExecuteScriptAsync(script);
        }
        catch { /* ignore — WebView2 may be navigating or not yet ready */ }
    }

    /// <summary>
    /// Scrolls the WebView2 to a proportional position (0 = top, 1 = bottom).
    /// Called by <c>SplitView</c> to keep the preview in sync with the editor scroll.
    /// Sets <see cref="_isProgrammaticScroll"/> for the duration of the call so that the
    /// scroll event posted back from JavaScript is suppressed and does not cause a feedback
    /// loop. The flag is cleared on the Background dispatcher priority (lower than the
    /// Normal priority at which WebMessageReceived fires) to ensure the suppression holds
    /// for the queued JS message before the flag resets.
    /// </summary>
    internal async Task ScrollToRatio(double ratio)
    {
        if (!_webViewReady) return;
        _isProgrammaticScroll = true;
        try
        {
            var script = $"window.scrollTo(0, {ratio.ToString(System.Globalization.CultureInfo.InvariantCulture)} * (document.body.scrollHeight - window.innerHeight));";
            await webBrowser.ExecuteScriptAsync(script);
        }
        catch { /* ignore scroll errors */ }
        finally
        {
            // Use Background priority so any WebMessageReceived event already queued from
            // the programmatic scroll is processed (and suppressed) before the flag clears.
            _ = Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _isProgrammaticScroll = false);
        }
    }
}
