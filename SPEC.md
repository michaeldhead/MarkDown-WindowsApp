# GHS Markdown Editor — Living Specification

## Stack & Library Versions

| Package | Version |
|---|---|
| .NET | 8.0 (net8.0-windows) |
| CommunityToolkit.Mvvm | 8.4.2 |
| MaterialDesignThemes | 5.3.1 |
| MaterialDesignColors | 5.3.1 (transitive) |
| Microsoft.Xaml.Behaviors.Wpf | 1.1.77 (transitive) |
| AvalonEditB | 2.4.0 |
| Markdig | 1.1.2 |
| Microsoft.Web.WebView2 | 1.0.3856.49 |

## Architecture Decisions

### MVVM via CommunityToolkit.Mvvm
Source-generator based MVVM. `[ObservableProperty]` fields generate public properties with change notification. `[RelayCommand]` generates `ICommand` / `IAsyncRelayCommand` implementations. ViewModels never reference View types.

### MaterialDesignInXAML (v5)
Theme bootstrapped via `<materialDesign:BundledTheme>` in `App.xaml` using MD3 defaults resource dictionary. Runtime theme switching is done by calling `PaletteHelper.SetTheme()` — no restart required.

### ThemeService
Singleton owned by `App`. Reads/writes `%AppData%\GHSMarkdownEditor\settings.json` via `System.Text.Json`. Auto mode reads `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme`. Exposed via `App.ThemeService`.

### Tab Model
Each open document is a `DocumentTabViewModel`. `IsDirty` is a computed property comparing `Content` against `_savedContent` (last-saved snapshot). `DisplayName` prepends `•` when dirty. `MarkSaved()` updates the snapshot and optionally updates `FilePath`/`FileName`.

### Dirty-tab confirmation
`MainViewModel.CloseTab` is an `IAsyncRelayCommand`. It awaits `DialogHost.Show(...)` with identifier `"RootDialog"` (declared in `MainWindow.xaml`). The dialog returns `bool` via `DialogHost.CloseDialogCommand` parameter.

### Tab Bar
Custom `UserControl` (not WPF `TabControl`) for full layout control. Tabs use `DataTrigger` on `IsActive` to switch between transparent and primary-color background. Scrollable via `ScrollViewer` with `HorizontalScrollBarVisibility="Auto"`. The `+` button is docked right.

### Window Chrome
Uses `Style="{StaticResource MaterialDesignWindow}"` — this is the MaterialDesign window style that replaces the default Windows chrome.

### SettingsService (Phase 2)
Generic `Get<T>` / `Set<T>` backed by `%AppData%\GHSMarkdownEditor\settings.json` as a `Dictionary<string, JsonElement>`. All services and view models access settings through this single file. Enums are persisted as integers by the default `System.Text.Json` serializer.

### ThemeService (Phase 2 refactor)
Now accepts `SettingsService` via constructor injection. `App` creates `SettingsService` first, then `ThemeService`. Exposes `IsDark` computed property (evaluates Auto mode against the Windows registry key at read time).

### MarkdownService
Uses Markdig `UseAdvancedExtensions()` pipeline (includes GFM tables, strikethrough, task lists, auto-identifiers, fenced code). Light and dark CSS are embedded string constants — no external files. `ToHtml(string, bool isDark)` produces a complete HTML document. Called from `PreviewView` and `SplitView`'s internal `PreviewView`.

### AvalonEditB namespace
AvalonEditB 2.4.0 uses namespace root `AvalonEditB.*` (not `ICSharpCode.AvalonEdit.*`), but registers the same XAML namespace URI: `http://icsharpcode.net/sharpdevelop/avalonedit`. Markdown `.xshd` highlighting definition is embedded as a C# raw string constant in `EditorView.xaml.cs` and registered once via `HighlightingManager.Instance.RegisterHighlighting`.

### ViewMode switching
`ViewMode` enum (`Write`, `Split`, `Preview`) lives in `Models/DocumentTab.cs`. `MainViewModel` exposes `CurrentViewMode`, `IsWriteMode`, `IsSplitMode`, `IsPreviewMode`. Three `UserControl` views (`EditorView`, `SplitView`, `PreviewView`) coexist in `MainWindow`'s content grid with `BooleanToVisibilityConverter` — all three are instantiated at startup and shown/hidden. This avoids WebView2 re-initialization on mode switch.

### EditorView two-way binding
`TextEditor.Text` is not a `DependencyProperty` — bound manually. `TextChanged` event writes to `ActiveTab.Content`. When `ActiveTab` changes or the view becomes visible (`IsVisibleChanged`), content is loaded from the VM with a `_isUpdatingFromViewModel` guard to prevent feedback loops.

### PreviewView debounce
Content changes trigger `RenderDebounced()` which waits 300ms using `CancellationTokenSource`. Cancelled and restarted on each keystroke. Only renders when `_webViewReady && IsVisible`.

### Synchronized scrolling (SplitView)
`SplitView` subscribes to `AvalonEditB.Rendering.TextView.ScrollOffsetChanged`. Scroll ratio = `ScrollOffset.Y / (DocumentHeight - ActualHeight)`, clamped 0–1. Applied to WebView2 via `ExecuteScriptAsync("window.scrollTo(0, ratio * (document.body.scrollHeight - window.innerHeight))")`. Editor drives preview — not bidirectional.

### GridSplitter ratio persistence
`DragCompleted` event saves `editorColumn.ActualWidth / totalWidth` as `"SplitRatio"` double in settings. Restored on `Loaded` and `IsVisibleChanged`. Clamped to 0.1–0.9 to prevent degenerate states.

### Known Phase 2 limitations
- Preview CSS does not auto-update when theme is toggled — updates on next content change or mode switch.
- `EditorViewModel` is a thin stub; caret position tracking deferred to Phase 3.

### ToolbarRibbon (Phase 3)
`Controls/ToolbarRibbon.xaml` — a `UserControl` containing a `ScrollViewer` + `StackPanel` of `MaterialDesignIconButton`-styled buttons organised into five groups (File, Format, Headings, Lists, Insert), separated by 1px `Rectangle` dividers. Inherits `DataContext` from `MainWindow` (which is `MainViewModel`). No code-behind logic — all buttons bind directly to commands on the ViewModel.

### FormattingService (Phase 3)
Stateless service in `Services/FormattingService.cs`. Receives `AvalonEditB.TextEditor` as a parameter — no static references. Inline-wrap actions (Bold, Italic, Strikethrough, InlineCode) check `editor.SelectionLength > 0`; if so, wrap selection, else insert markers and place caret between them. Heading actions strip any existing `#…` prefix via regex before prepending the new level. List-prefix actions use regex to skip idempotent re-application. Block inserts (CodeBlock, Table, HorizontalRule, Link) prepend `\n` only when the caret is not already on an empty line. CodeBlock and Link use `editor.Select(offset, length)` to highlight the placeholder text ("language" / "url") so the user can type immediately.

### FileService (Phase 3)
`Services/FileService.cs` — stateful only in that it holds a `SettingsService` reference for the recent-files list. `OpenFile()` / `SaveFileAs()` use `Microsoft.Win32.OpenFileDialog` / `SaveFileDialog`. `SaveFile()` writes to the existing `FilePath` if present, otherwise delegates to `SaveFileAs()`. All methods call `tab.MarkSaved(path)` on success. Errors surface via `MessageBox` — no exception propagation.

### ActiveEditorProvider pattern (Phase 3)
`MainViewModel` exposes `Func<TextEditor?>? ActiveEditorProvider`. Set from `MainWindow` code-behind after `InitializeComponent()`. The lambda switches on `CurrentViewMode`: Write → `editorView.Editor`; Split → `splitView.Editor`; Preview → `null`. This keeps ViewModels free of View references while allowing formatting commands to operate on the live editor. `SplitView.xaml.cs` exposes `internal TextEditor Editor => editorView.Editor` (the only addition to Phase 2 SplitView).

### Keyboard shortcuts (Phase 3)
Declared as `<KeyBinding>` in `Window.InputBindings` in `MainWindow.xaml`. Ctrl+B/I bind to `BoldCommand`/`ItalicCommand`; Ctrl+N/O/S/Shift+S to file commands; Ctrl+W to `CloseActiveTabCommand`; Ctrl+1/2/3 (`Key="D1/D2/D3"`) to `SetViewModeCommand` with `ViewMode` parameter. All bindings resolve through the Window's DataContext (`MainViewModel`). AvalonEdit does not consume Ctrl+B or Ctrl+I, so Window-level InputBindings fire correctly while the editor has focus.

## Build Verification

> **Build must compile with zero errors and zero warnings in Visual Studio 2022 (or `dotnet build`).**

Phase 1 verified clean. Phase 2 verified clean. Phase 3 verified clean. Phase 5 verified clean:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

## Phase 5 Architecture Decisions

### MenuBar (Phase 5)
`Controls/MenuBar.xaml` — a `UserControl` wrapping a WPF `Menu` control in a bordered `Border`.
Inherits `DataContext` from MainWindow (MainViewModel). The Recent Files submenu uses
`ItemsSource="{Binding RecentFiles}"` on a `MenuItem` with `ItemContainerStyle` that sets
`Header`, `Command` (`OpenRecentFileCommand`), and `CommandParameter` (`FilePath`).
`RelativeSource AncestorType=Menu` is used in the style setters to reach the `Menu`'s
DataContext from inside `ItemContainerStyle`. File-not-found entries are greyed out via
`IsEnabled="{Binding FileExists}"` on `RecentFileItem`.

### Single-Instance Enforcement (Phase 5)
Named `Mutex` (`GHSMarkdownEditorMutex`) checked in `App.OnStartup`. If not the first instance
and a file argument is present, the path is sent via a `NamedPipeClientStream` to the listening
first instance and the process exits immediately without creating a window. The first instance
runs `PipeListenerLoopAsync` on a background thread; received paths are marshalled to the UI
thread via `Dispatcher.Invoke` and the static `App.FileOpenRequested` event. `MainWindow`
subscribes on `Loaded` and calls `vm.OpenFromPath(path)`. The pipe name is
`GHSMarkdownEditorPipe`.

### Jump List (Phase 5)
`Services/JumpListService.cs` wraps `System.Windows.Shell.JumpList`. `UpdateJumpList` creates a
fresh `JumpList`, adds up to 10 `JumpTask` items (one per recent file, skipping missing files),
and calls `JumpList.SetJumpList(Application.Current, jumpList)`. This is called from
`MainViewModel.RefreshRecentFiles()` which is invoked after every file open or save operation.
No extra NuGet package required — `System.Windows.Shell` is part of the WPF runtime.

### Command Palette (Phase 5)
`Controls/CommandPalette.xaml` + `ViewModels/CommandPaletteViewModel.cs`. The palette is an
overlay `UserControl` placed in `MainWindow`'s content row with `Panel.ZIndex=100` and
`VerticalAlignment=Top`. A transparent backdrop with `ZIndex=99` and a `MouseDown` handler
closes the palette when the user clicks outside. Opened via `MainViewModel.OpenCommandPaletteCommand`
(Ctrl+P); closed via Escape or backdrop click.

`FilteredItems` is an `ObservableCollection<PaletteRow>` containing interleaved `PaletteHeader`
and `PaletteItem` objects. `PaletteRow.IsHeader` drives a `DataTrigger` in `ItemContainerStyle`
that sets `IsEnabled=False` and replaces the template for headers, making them non-interactive.
Fuzzy matching: substring first, then ordered-character-sequence. `MoveDown` / `MoveUp` operate
on `FilteredItems.OfType<PaletteItem>()` to skip headers. `ActivateSelected` closes the palette
before invoking the action so the UI is clean during command execution. Ctrl+P in
`PreviewView`'s JS bridge is forwarded to `OpenCommandPaletteCommand`.

### Detached Preview (Phase 5)
`Views/DetachedPreview.xaml` — a `Window` (not `UserControl`) with a single `WebView2`.
MainViewModel fires `DetachPreviewRequested` (event, not command) to avoid ViewModel→View
coupling. `MainWindow.xaml.cs` subscribes and manages the `DetachedPreview` window instance.
`MainViewModel.NotifyPreviewDetached(bool)` is called by MainWindow when the window opens/closes
to update `IsPreviewDetached` and switch view modes (Write on detach, Split on reattach).
Only one detached window is allowed; a second toggle closes the existing one.

### PrintService (Phase 5)
`Services/PrintService.cs`. **Implementation choice: WebView2 `window.print()`** rather than
WPF FlowDocument. Rationale: the printed output is identical to the live preview (full CSS,
tables, code blocks) — FlowDocument would lose all HTML/CSS fidelity. A temporary `Window`
containing a hidden `WebView2` is created, the rendered HTML is navigated to it, and
`ExecuteScriptAsync("window.print()")` is awaited. In Chromium, `window.print()` is synchronous
in the JavaScript execution context — it blocks until the user dismisses the print dialog — so
the `await` reliably spans the full print interaction. The helper window is closed when
`ExecuteScriptAsync` returns. Guard flag `_isPrinting` prevents re-entrant calls.

### FileAssociationService (Phase 5)
`Services/FileAssociationService.cs`. Writes to `HKCU\Software\Classes\.md` (extension →
ProgID) and `HKCU\Software\Classes\GHSMarkdownEditor.md` (ProgID shell/open/command). Uses
`SHChangeNotify(SHCNE_ASSOCCHANGED)` via P/Invoke to flush the shell cache. A settings flag
(`FileAssociationAsked`) ensures the prompt is shown only once — on first launch — regardless
of whether the user accepted. The dialog is deferred to after the main window is shown via
`Dispatcher.BeginInvoke` in `App.OnStartup`.

### Drag and Drop (Phase 5)
`AllowDrop=True` on MainWindow. `OnDragOver` / `OnDragLeave` / `OnDrop` override the
`Window` virtual methods. Supported extensions: `.md` and `.txt`. A named `Border`
(`dragDropBorder`, `IsHitTestVisible=False`, `Panel.ZIndex=50`) in the content row provides
the visual 3px blue highlight during drag-over. Non-supported files set
`DragDropEffects.None` and leave the border transparent.

### Keyboard Shortcuts Added (Phase 5)
| Key           | Action               |
|---------------|----------------------|
| Ctrl+P        | Open command palette |
| Ctrl+Shift+P  | Print                |

Both bindings added to `MainWindow.InputBindings` and forwarded via `PreviewView`'s JS bridge.

## Architecture Decisions (Phase 6 — Export & Installer)

### Export Service Design
`Services/ExportService.cs`. Single `ExportAsync(ExportFormat, string markdown, string outputPath)` entry point dispatches to format-specific methods. All formats produce a file on disk.

### PDF Export (Phase 6)
Uses WebView2 `CoreWebView2.PrintToPdfAsync`. An off-screen 1×1 ToolWindow is created at position (−32000, −32000) to host the WebView2 control; it is closed after export completes. `CoreWebView2PrintSettings` sets A4 page size (8.27 × 11.69 in) with 20 mm margins (≈ 0.787 in). Chosen over PdfSharp/iTextSharp because the output is pixel-identical to the live preview, requires no additional NuGet packages, and handles CSS/emoji/web fonts correctly.

### DOCX Export (Phase 6)
Uses `DocumentFormat.OpenXml` 3.1.0. The Markdig AST is walked directly to produce Word paragraphs, runs, numbering, and tables — no intermediate HTML. Key style mappings:
- `HeadingBlock` (level 1–4) → Heading1–Heading4 paragraph styles (bold, dark blue `1F3864`, descending half-point sizes)
- `FencedCodeBlock`/`CodeBlock` → Code paragraph style (Courier New 10pt, gray `F2F4F7` shading), newlines become soft `Break`
- `ListBlock` → `NumberingProperties` referencing two AbstractNum definitions (abstract 0 = bullet, 1 = decimal)
- `MdTable` → `Table` with `TableBorders` (single-line), header row has `E8EDF2` shading and bold runs
- `EmphasisInline` → bold (delimiterCount ≥ 2), italic (== 1), bold+italic (== 3)
- `CodeInline` → Courier New 10pt run
- `LineBreakInline.IsHard = true` → hard `Break`; `IsHard = false` → space run

Type ambiguity between `System.Windows.Style` and `DocumentFormat.OpenXml.Wordprocessing.Style` (and `Color`) resolved via using aliases `OxStyle` and `OxColor`.

### HTML Export — Styled vs. Clean (Phase 6)
- **HTML styled**: calls `App.MarkdownService.ToHtml(markdown, isDark: false)` — full HTML skeleton with embedded CSS (light theme), same as the preview pane.
- **HTML clean**: calls `Markdig.Markdown.ToHtml(markdown, Pipeline)` directly — body markup only, no `<html>`/`<head>`/`<body>` wrapper, no CSS. Intended for pasting into CMSes or mail clients.

### Plain Text Export (Phase 6)
HTML styled output is post-processed: `<br>`/`<br/>` → `\n`, block-close tags → `\n`, all tags stripped via `<[^>]+>` regex, HTML entities decoded via `WebUtility.HtmlDecode`, runs of blank lines collapsed.

### Export Dialog (Phase 6)
`Controls/ExportDialog.xaml` / `ViewModels/ExportDialogViewModel.cs`. MaterialDesign `DialogHost` modal. `ExportFormatOption.All` is a static list built from `Enum.GetValues<ExportFormat>()`. ListBox `SelectedItem` two-way binds to `SelectedFormat`. Each item shows a radio icon toggled by a `DataTrigger` on `ListBoxItem.IsSelected`. The Export button closes the dialog via `DialogHost.CloseDialogCommand` with `CommandParameter="{Binding SelectedFormat.Format}"` — returns the `ExportFormat` enum value as the dialog result.

### Installer (Phase 6)
`Installer/setup.iss` — Inno Setup 6.x. `PrivilegesRequired = lowest` installs per-user to `%LocalAppData%\Programs\GHS Markdown Editor` without UAC; user can optionally elevate (`PrivilegesRequiredOverridesAllowed = dialog`). Source is a `dotnet publish -r win-x64 --self-contained true -p:PublishSingleFile=true` output folder. `.md` file association written to HKCU. `FileAssociationAsked=true` written to suppress in-app first-launch prompt. Advisory `InitializeWizard` Code section checks for WebView2 Evergreen runtime and shows a non-blocking info dialog if absent.

## Backlog

_(Empty — populated in future phases)_
