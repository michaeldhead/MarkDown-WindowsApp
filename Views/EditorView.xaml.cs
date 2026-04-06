using AvalonEditB;
using AvalonEditB.Highlighting;
using AvalonEditB.Highlighting.Xshd;
using GHSMarkdownEditor.ViewModels;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml;

namespace GHSMarkdownEditor.Views;

/// <summary>
/// Code-behind for the AvalonEdit Markdown editor pane. Owns the custom XSHD syntax
/// definition and the two-way content sync between the editor control and the active
/// <see cref="DocumentTabViewModel"/>. A guard flag prevents the sync from firing
/// recursively when content is written programmatically from the view model.
/// </summary>
public partial class EditorView : UserControl
{
    /// <summary>
    /// True while <see cref="SyncFromViewModel"/> is writing to the editor, so that
    /// <see cref="OnEditorTextChanged"/> knows to skip the corresponding change notification
    /// and avoid a feedback loop.
    /// </summary>
    private bool _isUpdatingFromViewModel;

    // Exposed for SplitView scroll sync
    internal TextEditor Editor => textEditor;

    /// <summary>
    /// Inline XSHD syntax definition for Markdown highlighting. Defined here rather than
    /// as an embedded resource so it is easier to maintain alongside the editor code.
    /// All colour rules use <c>&lt;Rule&gt;</c> (line-level patterns) rather than
    /// <c>&lt;Span&gt;</c> because AvalonEditB crashes at runtime if a Span's Begin
    /// pattern can match zero characters — a risk with the built-in "MarkDown" definition
    /// that ships with the library. Rules use <c>.+</c> (one-or-more) deliberately; <c>.*</c>
    /// would allow zero-character matches and trigger the same crash.
    /// </summary>
    private const string MarkdownXshd = """
        <?xml version="1.0"?>
        <SyntaxDefinition name="Markdown" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Heading"    foreground="#569CD6" fontWeight="bold"/>
          <Color name="Bold"       foreground="#DCDCAA" fontWeight="bold"/>
          <Color name="Italic"     foreground="#DCDCAA" fontStyle="italic"/>
          <Color name="BoldItalic" foreground="#DCDCAA" fontWeight="bold" fontStyle="italic"/>
          <Color name="Code"       foreground="#9CDCFE"/>
          <Color name="CodeFence"  foreground="#CE9178"/>
          <Color name="Link"       foreground="#4EC9B0"/>
          <Color name="List"       foreground="#C586C0"/>
          <Color name="Blockquote" foreground="#6A9955"/>
          <Color name="HRule"      foreground="#808080"/>

          <RuleSet>
            <!-- Fenced code fence lines: ``` or ```lang  (Span Begin=^ crashes AvalonEdit; use Rule instead) -->
            <Rule color="CodeFence">^```.*</Rule>

            <!-- Indented code block lines (4-space indent) -->
            <Rule color="CodeFence">^    .+</Rule>

            <!-- ATX headings -->
            <Rule color="Heading">^\#{1,6}[^\n]*</Rule>

            <!-- Bold + Italic (must precede bold and italic alone) -->
            <Rule color="BoldItalic">\*{3}[^*\n]+\*{3}|_{3}[^_\n]+_{3}</Rule>

            <!-- Bold -->
            <Rule color="Bold">\*{2}[^*\n]+\*{2}|_{2}[^_\n]+_{2}</Rule>

            <!-- Italic -->
            <Rule color="Italic">\*[^*\n]+\*|_[^_\n]+_</Rule>

            <!-- Inline code -->
            <Rule color="Code">`[^`\n]+`</Rule>

            <!-- Images (before links) -->
            <Rule color="Link">!\[[^\]]*\]\([^)]*\)</Rule>

            <!-- Links -->
            <Rule color="Link">\[[^\]]*\]\([^)]*\)|\[[^\]]*\]\[[^\]]*\]</Rule>

            <!-- Blockquotes -->
            <Rule color="Blockquote">^[ \t]*&gt;\s[^\n]*</Rule>

            <!-- Unordered list -->
            <Rule color="List">^[ \t]*[-*+]\s</Rule>

            <!-- Ordered list -->
            <Rule color="List">^[ \t]*\d+\.\s</Rule>

            <!-- Task list -->
            <Rule color="List">^[ \t]*[-*+]\s\[[ xX]\]\s</Rule>

            <!-- Horizontal rules -->
            <Rule color="HRule">^(\*{3,}|-{3,}|_{3,})\s*$</Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    /// <summary>
    /// Parsed highlighting definition, cached after first load.
    /// Loaded directly from <see cref="MarkdownXshd"/> rather than registered with
    /// <see cref="HighlightingManager"/> because the manager's lookup is case-insensitive:
    /// our name "Markdown" would silently resolve to AvalonEditB's built-in "MarkDown"
    /// definition, which contains zero-character Span patterns that crash the engine.
    /// </summary>
    private static IHighlightingDefinition? _markdownHighlighting;

    public EditorView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
        Loaded += OnLoaded;
    }

    private static IHighlightingDefinition GetMarkdownHighlighting()
    {
        if (_markdownHighlighting != null) return _markdownHighlighting;
        using var reader = new StringReader(MarkdownXshd);
        using var xmlReader = XmlReader.Create(reader);
        _markdownHighlighting = HighlightingLoader.Load(xmlReader, HighlightingManager.Instance);
        return _markdownHighlighting;
    }

    /// <summary>
    /// Completes editor setup after the control is in the visual tree: applies syntax
    /// highlighting, subscribes to text-change events, and performs the initial sync from
    /// the view model. Deferred to <c>Loaded</c> (not the constructor) because
    /// <see cref="DataContext"/> is not set until after the control is added to the tree.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        textEditor.SyntaxHighlighting = GetMarkdownHighlighting();
        textEditor.TextChanged += OnEditorTextChanged;
        textEditor.MouseDoubleClick += OnEditorMouseDoubleClick;
        SubscribeToViewModel();
        SyncFromViewModel();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if ((bool)e.NewValue)
            SyncFromViewModel();
    }

    private MainViewModel? GetViewModel() => DataContext as MainViewModel;

    private void SubscribeToViewModel()
    {
        var vm = GetViewModel();
        if (vm == null) return;
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ActiveTab))
            SyncFromViewModel();
    }

    /// <summary>
    /// Writes the active tab's content into the editor. The <see cref="_isUpdatingFromViewModel"/>
    /// guard is set for the duration of the write so <see cref="OnEditorTextChanged"/> knows
    /// the change originated here and must not propagate back to the view model.
    /// An equality check avoids a redundant document replacement that would clear undo history.
    /// </summary>
    private void SyncFromViewModel()
    {
        var vm = GetViewModel();
        var content = vm?.ActiveTab?.Content ?? string.Empty;
        if (textEditor.Text == content) return;
        _isUpdatingFromViewModel = true;
        textEditor.Text = content;
        _isUpdatingFromViewModel = false;
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isUpdatingFromViewModel) return;
        var vm = GetViewModel();
        if (vm?.ActiveTab == null) return;
        vm.ActiveTab.Content = textEditor.Text;
    }

    private static readonly Regex CodeFenceRegex = new(@"^```\w*$", RegexOptions.Compiled);

    private void OnEditorMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        var lineNumber = textEditor.TextArea.Caret.Line;
        var doc = textEditor.Document;
        if (lineNumber < 1 || lineNumber > doc.LineCount) return;

        var line = doc.GetLineByNumber(lineNumber);
        var lineText = doc.GetText(line.Offset, line.Length);

        if (!CodeFenceRegex.IsMatch(lineText)) return;

        // Suppress default word-selection and open the language picker instead.
        e.Handled = true;
        var vm = GetViewModel();
        vm?.OpenLanguagePickerForReplaceCommand.Execute(lineNumber);
    }
}
