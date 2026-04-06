namespace GHSMarkdownEditor.Models;

/// <summary>
/// Controls whether the application uses a light palette, dark palette, or follows
/// the Windows system preference via the registry <c>AppsUseLightTheme</c> value.
/// </summary>
public enum ThemeMode
{
    Light,
    Dark,
    Auto
}

/// <summary>
/// The three layout modes of the main window. <see cref="Write"/> shows only the editor,
/// <see cref="Preview"/> shows only the rendered output, and <see cref="Split"/> shows
/// both panels side by side with a resizable splitter.
/// </summary>
public enum ViewMode
{
    Write,
    Split,
    Preview
}
