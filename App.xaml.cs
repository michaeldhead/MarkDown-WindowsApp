using GHSMarkdownEditor.Services;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;

namespace GHSMarkdownEditor;

public partial class App : Application
{
    // ── Service singletons ────────────────────────────────────────────────────

    public static SettingsService  SettingsService  { get; private set; } = null!;
    public static ThemeService     ThemeService     { get; private set; } = null!;
    public static MarkdownService  MarkdownService  { get; private set; } = null!;
    public static JumpListService  JumpListService  { get; private set; } = null!;
    public static PrintService     PrintService     { get; private set; } = null!;
    public static ExportService    ExportService    { get; private set; } = null!;

    // ── Single-instance ───────────────────────────────────────────────────────

    private const string MutexName = "GHSMarkdownEditorMutex";
    private const string PipeName  = "GHSMarkdownEditorPipe";

    private static Mutex?                      _mutex;
    private static CancellationTokenSource     _pipeCts = new();

    /// <summary>
    /// File path passed as a command-line argument on the first launch.
    /// Read by MainWindow after it loads.
    /// </summary>
    internal static string? StartupFilePath { get; private set; }

    /// <summary>
    /// Raised on the UI thread when a second instance sends a file path via the named pipe.
    /// MainWindow subscribes and opens the file in a new tab.
    /// </summary>
    internal static event Action<string>? FileOpenRequested;

    // ── Startup ───────────────────────────────────────────────────────────────

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, MutexName, out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // A window is already open.  If a file was supplied, forward it via the pipe.
            if (e.Args.Length > 0)
                TrySendPathToFirstInstance(e.Args[0]);

            Shutdown();
            return;
        }

        // ── First instance initialisation ────────────────────────────────────

        SettingsService = new SettingsService();
        ThemeService    = new ThemeService(SettingsService);
        MarkdownService = new MarkdownService();
        JumpListService = new JumpListService();
        PrintService    = new PrintService();
        ExportService   = new ExportService();

        StartupFilePath = e.Args.Length > 0 ? e.Args[0] : null;

        base.OnStartup(e);

        // Start the named-pipe listener so subsequent launches can send file paths.
        _ = Task.Run(() => PipeListenerLoopAsync(_pipeCts.Token));

        // Defer the file-association prompt until the main window is fully shown.
        Dispatcher.BeginInvoke(() =>
            new FileAssociationService(SettingsService).RegisterIfNeeded());
    }

    // ── Shutdown ──────────────────────────────────────────────────────────────

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts.Cancel();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    // ── Pipe: sender (second instance) ───────────────────────────────────────

    /// <summary>
    /// Connects to the named pipe created by the first instance and writes the file path.
    /// Silently ignores failures (e.g. first instance still starting up).
    /// </summary>
    private static void TrySendPathToFirstInstance(string path)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000); // 2-second timeout
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(path);
        }
        catch { /* best effort */ }
    }

    // ── Pipe: listener (first instance) ──────────────────────────────────────

    /// <summary>
    /// Runs on a background thread for the lifetime of the first instance.
    /// Waits for incoming connections from subsequent launches and fires
    /// <see cref="FileOpenRequested"/> on the UI thread for each received path.
    /// </summary>
    private static async Task PipeListenerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await server.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(server);
                var path = await reader.ReadLineAsync(ct);

                if (!string.IsNullOrEmpty(path))
                {
                    Current.Dispatcher.Invoke(
                        () => FileOpenRequested?.Invoke(path));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // Transient error (e.g., broken pipe) — retry after a short delay.
                await Task.Delay(100, ct).ContinueWith(_ => { });
            }
        }
    }
}
