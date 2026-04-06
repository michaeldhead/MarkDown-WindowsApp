# GHS Markdown Editor — Windows Desktop App

A native Windows Markdown editor built with C# / WPF, featuring a live split preview,
full toolbar, sidebar panels, and Windows-native integrations.

**Version:** 1.0.0  
**Platform:** Windows 10 (build 17763+) / Windows 11  
**Companion web app:** [md.theheadfamily.com](https://md.theheadfamily.com) · [Web app repo](https://github.com/michaeldhead/MarkDown-React-Firebase.git)

---

## Features

### Editor
- AvalonEdit-powered editor with Markdown syntax highlighting and line numbers
- Write, Split, and Preview modes with synchronized scroll
- Draggable split divider with persisted ratio
- Word wrap and adjustable font size
- Auto-save for dirty files at configurable intervals

### Toolbar & Formatting
- Full toolbar ribbon: Bold, Italic, Strikethrough, Inline Code, H1–H4, Bullet List,
  Numbered List, Code Block, Table, Horizontal Rule, Link
- Smart cursor placement — formatting wraps selections or inserts markers with cursor ready to type
- Code block language picker — chip grid with 14 languages, double-click to change language
- Keyboard shortcuts: Ctrl+B, Ctrl+I, Ctrl+S, Ctrl+N, Ctrl+O, Ctrl+W, Ctrl+1/2/3

### Menu Bar
- File menu: New, Open, Save, Save As, Recent Files, Export As, Print, Exit
- Format menu: all formatting actions with keyboard shortcut hints
- View menu: Write / Split / Preview, Detach Preview
- Help menu: About, Companion Web App

### Preview
- WebView2-powered live Markdown preview
- Light and dark CSS themes matching the app theme
- Scroll position preserved during re-render
- Detachable preview window for dual monitor use

### Sidebar
- **Document Outline** — live H1–H4 heading tree, click to scroll editor to heading
- **Recent Files** — last 10 files, click to open, strikethrough for missing files
- **Snippets** — save, insert, and delete reusable text snippets
- **Settings** — theme (Light/Dark/Auto), font size, word wrap, auto-save interval

### Export
- PDF — A4, full styling
- DOCX — native Word styles (Heading1–4, lists, code blocks, tables)
- HTML styled — self-contained single file with embedded CSS
- HTML clean — semantic HTML body content only
- Plain text — all Markdown syntax stripped

### Windows-Native
- Register as default `.md` file handler
- Drag and drop `.md` files from Windows Explorer
- Windows taskbar Jump List with recent files
- Command palette (Ctrl+P) — fuzzy search across tabs, headings, recent files, and commands
- Single-instance enforcement — second launch passes file to running instance
- Print via native Windows print dialog (Ctrl+Shift+P)

### Theme
- Light, Dark, and Auto (follows Windows system theme)
- Persisted across sessions

---

## Installation

Download `GHSMarkdownEditor-Setup-1.0.0.exe` from the [Install](https://github.com/michaeldhead/MarkDown-WindowsApp/tree/main/install) page and run the installer.

The installer will:
- Install the app to `%LocalAppData%\Programs\GHS Markdown Editor`
- Create a Start Menu entry and optional desktop shortcut
- Register GHS Markdown Editor as the default handler for `.md` files
- Verify WebView2 runtime availability (required for preview)

**WebView2 Runtime** — ships with Windows 11 and most Windows 10 builds post-2022.
If the preview does not work, download the runtime from
[Microsoft](https://developer.microsoft.com/en-us/microsoft-edge/webview2/).

---

## Building from Source

**Prerequisites:**
- Visual Studio 2022 with .NET Desktop workload
- .NET 8 SDK
- Inno Setup 6.x (for installer only)

**Steps:**
```bash
git clone https://github.com/michaeldhead/MarkDown-WindowsApp.git
```
Open `GHSMarkdownEditor.sln` in Visual Studio 2022, set configuration to Release,
and build (`Ctrl+Shift+B`). Zero errors and zero warnings is the required build gate.

To compile the installer, open `Installer/setup.iss` in Inno Setup and press `Ctrl+F9`.

---

## Tech Stack

| Concern | Library |
|---|---|
| UI Framework | WPF / .NET 8 |
| MVVM | CommunityToolkit.Mvvm |
| Styling | MaterialDesignInXAML |
| Editor | AvalonEdit |
| Markdown Rendering | Markdig |
| Preview | Microsoft WebView2 |
| PDF Export | PdfSharp + HtmlRenderer.PdfSharp |
| DOCX Export | Xceed.Words.NET |
| Settings | System.Text.Json |
| Installer | Inno Setup 6 |

---

## Companion Web App

This app is the Windows desktop counterpart to the GHS Markdown Editor web app,
built with React 18, TypeScript, Tailwind CSS, and CodeMirror 6.

- **Live:** [md.theheadfamily.com](https://md.theheadfamily.com)
- **Repo:** [MarkDown-React-Firebase](https://github.com/michaeldhead/MarkDown-React-Firebase.git)

---

## About

A Head & CC Production · Mike & The Machine · 2026