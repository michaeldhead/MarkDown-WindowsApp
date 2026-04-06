using CommunityToolkit.Mvvm.ComponentModel;

namespace GHSMarkdownEditor.ViewModels;

/// <summary>
/// Holds editor-specific state. Expanded in future phases (caret position, folding, etc.).
/// </summary>
public partial class EditorViewModel : ObservableObject
{
    [ObservableProperty]
    private int _caretLine = 1;

    [ObservableProperty]
    private int _caretColumn = 1;
}
