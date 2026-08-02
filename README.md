# MarkDesk

A lightweight Markdown viewer and editor for Windows. Open a `.md` file, read it, edit it, save it, export to PDF — nothing more, nothing less.

![Version](https://img.shields.io/badge/version-0.1.0-blue)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)
![License](https://img.shields.io/badge/license-MIT-green)
![Downloads](https://img.shields.io/github/downloads/wolfgangmay/MarkDesk/total)

## What it is

MarkDesk is a no-fuss Markdown app. It is not a notebook, not a knowledge base, not a sync service. It does one thing well: **let you open, read, and edit Markdown files locally**, with a faithful live preview and offline rendering of everything that matters in a technical document.

- **Open** any `.md` / `.markdown` file (dialog, recent list, drag-drop, command line, or set MarkDesk as the Windows default).
- **Edit** with a real code editor (syntax highlighting, find/replace, word wrap).
- **Preview** rendered side-by-side or full-screen, updating as you type.
- **Export** to PDF (always printed on a clean light theme, regardless of the app theme).

## Features

| Area | Details |
| --- | --- |
| Editing | AvalonEdit-based editor, Markdown syntax highlighting, find & replace, word wrap, line/column caret, word count |
| Live preview | Side-by-side / preview-only / editor-only layouts, 150 ms debounced re-render, scroll sync |
| Rendering | GitHub-flavored Markdown via Markdig — tables, task lists, footnotes, strikethrough, emoji (`:smile:`), heading anchors, custom containers (`:::warning`), GitHub-style alerts (`> [!NOTE]`) |
| Rich content | Syntax highlighting (highlight.js), math via KaTeX, diagrams via Mermaid — **all bundled locally, fully offline** |
| Themes | Light / Dark / Follow system; applies to title bar, menus, popups, dialogs, and message boxes |
| Files | Open / save / save-as, recent files, external-change detection with reload prompt, large-file warning |
| PDF export | A4 / Letter, printed on a forced light layout via a dedicated offscreen WebView2 (the on-screen preview is never disturbed) |
| Images | Paste images from clipboard (saved next to the document, inserted as Markdown links) |
| File association | Register MarkDesk as the Windows default for `.md` from the Tools menu (user-level, no admin) |

## Tech stack

| Layer | Technology |
| --- | --- |
| Language | C# 14 / .NET 10 |
| UI framework | WPF (Windows Presentation Foundation) |
| Architecture | MVVM (`CommunityToolkit.Mvvm` — source generators, observable objects, relay commands) |
| Markdown parsing | [Markdig](https://github.com/xoofx/markdig) 1.3.2 |
| Code editor | [AvalonEdit](https://github.com/icsharpcode/AvalonEdit) 6.3.1.120 |
| Preview engine | Microsoft Edge WebView2 (Chromium) |
| Dependency injection | `Microsoft.Extensions.DependencyInjection` |
| Front-end assets | highlight.js 11.9.0, KaTeX 0.16.11, Mermaid 11.16.0 (vendored, offline) |
| Native interop | `dwmapi` (immersive dark title bar), `shell32` (file-association refresh) |

## Architecture

```
MarkDesk
├── App.xaml(.cs)            Application bootstrap, DI container, CLI arg handling
├── MainWindow.xaml(.cs)     Shell: toolbar, menu, status bar, layout/theme wiring
├── ViewModels/
│   ├── MainViewModel.cs     Document state, open/save, view mode, theme
│   └── SettingsViewModel.cs Settings dialog backing model
├── Models/
│   └── AppSettings.cs       Persisted settings (JSON) + enums (ViewMode, ThemeMode…)
├── Services/
│   ├── MarkdownRenderer.cs  Markdig pipeline (HTML output)
│   ├── PreviewTemplate.cs   HTML template + CSS + JS (alerts, anchors, mermaid init)
│   ├── FileService.cs       Load/save with encoding detection
│   ├── EncodingDetector.cs  BOM / heuristic encoding detection
│   ├── FileWatcher.cs       External-change notifications
│   ├── ImagePasterService.cs Clipboard image → file → Markdown link
│   ├── DialogService.cs     Themed message boxes & file dialogs
│   ├── ThemeService.cs      System light/dark detection
│   ├── WindowTheme.cs       DWM dark title bar helper (shared by all windows)
│   ├── FileAssociationService.cs  Register .md handler (HKCU)
│   └── SettingsService.cs   JSON settings persistence
├── Controls/
│   ├── MarkdownEditor.xaml(.cs)    AvalonEdit wrapper + FindReplacePanel host
│   ├── PreviewView.xaml(.cs)       WebView2 host (on-screen + offscreen print)
│   ├── FindReplacePanel.xaml(.cs)  Find/replace floating panel
│   └── ThemedMessageBox.xaml(.cs)  Theme-aware replacement for MessageBox
├── Views/
│   └── SettingsDialog.xaml(.cs)    Settings window
└── Assets/web/vendor/      Vendored front-end libs (highlight, katex, mermaid)
```

**Data flow:** Editor → `DocumentText` (binding) → debounced → `MarkdownRenderer` → `PreviewTemplate.Build` → `PreviewView.UpdateAsync` (WebView2).

## Getting started

### Prerequisites
- Windows 10 1809+ (x64)
- Microsoft Edge WebView2 Runtime (preinstalled on Windows 11; on Windows 10 install it from [aka.ms/webview2](https://aka.ms/webview2))
- For the framework-dependent build: .NET 10 Desktop Runtime

### Run from source
```bash
dotnet build MarkDesk.slnx -c Release
src\MarkDesk\bin\Release\net10.0-windows\MarkDesk.exe samples\mermaid-demo.md
```

### Publish
Two profiles ship in `src/MarkDesk/Properties/PublishProfiles/`:
- **Portable** — self-contained single-file `.exe` (~78 MB, nothing else needed).
- **PortableFramework** — framework-dependent, ~70 small files (~8 MB), requires the .NET 10 Desktop Runtime.

```bash
dotnet publish src/MarkDesk -c Release -p:PublishProfile=Portable
dotnet publish src/MarkDesk -c Release -p:PublishProfile=PortableFramework
```

## Development notes

This project was built pair-programming with AI assistants through the [opencode](https://opencode.ai) CLI.

Models used during development:
- **GLM-5.2** (Zhipu AI)
- **DeepSeek V4 Flash 0731**

## License

MIT — see [LICENSE](LICENSE).
