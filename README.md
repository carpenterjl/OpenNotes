# OpenNotes

> A fully offline Windows desktop app for tasks, rich notes, and freeform canvas diagrams.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-0078D4?logo=windows)
![Build](https://img.shields.io/badge/build-0%20errors%20%C2%B7%200%20warnings-brightgreen)
![Tests](https://img.shields.io/badge/tests-246%20passing-brightgreen)
![License](https://img.shields.io/badge/license-MIT-blue)

---

> **AI-assisted project.** OpenNotes was built in close collaboration with [Claude Code](https://claude.ai/code), Anthropic's AI coding assistant. The architecture, requirements, and design decisions were driven by a human developer; Claude Code handled significant portions of the implementation. The codebase is real, tested, and fully functional — this project is also an exploration of what's achievable when a developer works alongside an AI coding tool on a non-trivial desktop application.

---

## What is OpenNotes?

OpenNotes is a productivity app that lives entirely on your machine. No accounts, no sync, no telemetry — your data is JSON files on your own disk.

It combines three things in one window:

- **Task management** with a rich block-based editor (Markdown, LaTeX, code, Mermaid diagrams, checklists)
- **A freeform multi-page canvas** for diagrams, visual notes, and linked task nodes
- **Workspace organization** so different projects stay cleanly separated

<!-- 
  SCREENSHOTS: Add 3–4 screenshots here once you have them.
  Suggested shots:
    1. Task editor showing mixed content blocks (Markdown + code + Mermaid)
    2. Canvas editor with connected nodes and ink markup
    3. Command palette (Ctrl+P) open over the main window
    4. Custom theme colors dialog
  
  Format:
  ![Task editor](docs/screenshots/task-editor.png)
-->

---

## Features

### Task management
- Create tasks with status, priority, due dates, tags, and completion percentage
- **Rich block-based editor** — mix any content types freely in one task
- Kanban board (Not Started → In Progress → Blocked → Review → Completed)
- Live dashboard — Due Today / Upcoming / Overdue / Completed, updating in real time
- Full-text search across all tasks in a workspace

### Content blocks
| Block | Renderer |
|---|---|
| Markdown | Markdig.Wpf — full CommonMark + tables, code fences, links |
| LaTeX math | WpfMath with a preprocessor for multi-line, `\mathbf`, amsmath environments |
| Code | AvalonEdit with theme-adaptive syntax colors and a language picker |
| Mermaid diagram | WebView2 + mermaid.js, auto-fits on resize |
| Checklist | Native WPF with completion tracking |
| Image | Lazy-loading `BitmapImage` |

### Canvas editor
- Multi-page documents stored as `.taskcanvas` (a renamed ZIP)
- Nodes: shapes, text, sticky notes (rendered Markdown), LaTeX, code (SVG), Mermaid (PNG), images, task links
- Anchored connectors with click-to-select and right-click-to-delete
- 8-handle resize on every node; drag-to-size shape creation
- Ink markup layer — pen, marker, highlighter, eraser; node-bound ink moves with its node
- Per-document color theming; canvas re-brushes instantly when the app theme changes
- High-fidelity PDF export via QuestPDF (selectable text, vector shapes/connectors/ink, 300 DPI LaTeX)
- Right-click empty canvas for a quick-create menu; z-order and border-toggle per node

### App-wide
- **Four themes** — Light, Dark, High Contrast, and a fully customizable theme with 16 editable color slots and a live color picker
- **Command palette** (Ctrl+P) — navigate, switch themes, run parameterized commands (New Task, Open Canvas, etc.) with guided token entry and autofill
- Undo/Redo stack (up to 200 entries) — Ctrl+Z / Ctrl+Y
- Windows toast notifications with per-task reminders
- Autosave and versioned ZIP backups
- Crash-safe atomic writes (write to `.tmp`, rename over target)
- Structured logging via Serilog (`%APPDATA%\OpenNotes\logs\`)

---

## Prerequisites

| Requirement | Notes |
|---|---|
| Windows 10 1809+ or Windows 11 | Required for WinRT toast notifications |
| .NET 8 SDK | [Download](https://dotnet.microsoft.com/download/dotnet/8.0) |
| Microsoft Edge WebView2 Runtime | Pre-installed on Windows 11; [download for Windows 10](https://developer.microsoft.com/microsoft-edge/webview2/) |
| Internet (first run only) | Mermaid loads `mermaid@11` from jsDelivr on first render, then WebView2 caches it. All other rendering is fully offline. |

---

## Build & Run

```powershell
git clone <repo-url>
cd OpenNotes

dotnet restore
dotnet build OpenNotes.sln
dotnet run --project OpenNotes.App

# Run the test suite
dotnet test OpenNotes.Tests
```

---

## Data storage

All data stays on your machine — no cloud, no telemetry.

```
%APPDATA%\OpenNotes\
├── app-settings.json          Theme selection and custom colors
├── logs\                      Daily rolling log files (Serilog)
└── workspaces\
    └── {workspace-guid}\
        ├── metadata.json
        ├── tasks\             One JSON file per task (atomic writes)
        ├── diagrams\          Canvas documents (.taskcanvas — ZIP archive)
        ├── attachments\
        ├── backups\           Versioned ZIP snapshots
        └── cache\             SQLite search index + extracted canvas assets
```

---

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+P` | Open command palette |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` / `Ctrl+Shift+Z` | Redo |
| `Ctrl+S` | Save (task editor) |
| `Delete` | Delete selected canvas node |
| `Ctrl+D` | Navigate to Dashboard |
| `Escape` | Close palette / dismiss dialogs |

---

## Architecture

The app uses:
- **CommunityToolkit.Mvvm** — `ObservableProperty`, `RelayCommand`, source-generated MVVM
- **Microsoft.Extensions.Hosting** — DI container, `IHostedService` lifecycle (autosave, reminder scheduler)
- **AvalonDock** — dockable panel layout
- **Serilog** — structured logging

---

## Roadmap

| Phase | Status |
|---|---|
| Foundation: DI, workspaces, themes, navigation | ✅ |
| Task CRUD, dashboard, kanban, search | ✅ |
| Rich content blocks (Markdown, LaTeX, code, Mermaid, image, checklist) | ✅ |
| Command palette with guided argument entry | ✅ |
| Notifications, reminders, export, autosave, backups | ✅ |
| Canvas: multi-page, nodes, connectors, ink, PDF export | ✅ |
| Canvas: per-document theming, app-theme following, z-order, drag-to-size | ✅ |
| Custom theme engine with 16 editable color slots | ✅ |
| SQLite FTS5 search index | Planned |
| Calendar and timeline views | Planned |
| Tray icon | Planned |
| Workspace import (ZIP and Markdown) | Planned |
| Offline Mermaid bundling (no CDN dependency) | Planned |
| AI assistant sidebar (`IAiAssistant` stub is wired) | Planned |
| Plugin system (`IPlugin` stub is wired) | Planned |

---

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Process runs but no window appears | Startup blocked before the window was created — almost always a DI cycle hanging in `_host.StartAsync()`. Check `%APPDATA%\OpenNotes\logs\` for the last log line before the hang. |
| Mermaid block shows "Syntax error in text" | Newer diagram types (e.g. `radar-beta`) need mermaid v11.3+. The app loads `mermaid@11` from CDN; verify WebView2 is installed and the machine had internet on first render. |
| Mermaid block is blank | WebView2 runtime missing — install it (see Prerequisites). First render also needs internet to fetch the CDN bundle. |
| Canvas node shows "Mermaid (render failed)" | The off-screen export render couldn't fetch mermaid.js. Double-click the node to retry once online. |
| Stale image on canvas after re-rendering | Restart the app — this was fixed in a recent build (WPF URI bitmap cache bypass). |

---

## License

MIT — see [LICENSE](LICENSE).
