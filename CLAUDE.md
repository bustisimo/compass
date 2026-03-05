# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Compass is a Windows desktop spotlight-search assistant (like macOS Spotlight / Alfred). It lives in the system tray, activated via **Alt+Space**, and provides:
- App launching (scans `.lnk` shortcuts from Start Menu folders)
- Web search shortcuts (keyword → URL template, e.g. `yt <query>`)
- AI chat powered by Google Gemini (with WMI tool-calling for system info queries)
- Custom PowerShell command generation via AI ("Extensions")
- AI-driven UI personalization

## Build & Run

```bash
# Build (Debug)
dotnet build

# Build (Release)
dotnet build -c Release

# Run
dotnet run
```

The project targets `net8.0-windows` and requires Windows. There are no automated tests.

## Architecture

The entire application lives in two files:
- **`MainWindow.xaml`** — All WPF UI markup, styles, and layout for both views
- **`MainWindow.xaml.cs`** — All logic (~1900 lines), plus all data model classes at the bottom of the file

All classes are defined at the bottom of `MainWindow.xaml.cs`:
- `AppSettings` — persisted to `settings.json` (next to the exe), holds API key, model, all personalization properties
- `CustomShortcut` — persisted to `shortcuts.json` (next to the exe)
- `CompassExtension` — persisted as individual `{name}.json` files in `%AppData%\Compass\Extensions\`
- `AppSearchResult` — in-memory search result with either a bitmap icon (real apps) or a geometry/SVG icon (virtual commands)
- `PersonalizationProposal` — temporary object for AI-generated style changes (has nullable fields; only non-null fields are applied)
- `PersonalizationManager` — static helper with the system prompt for personalization AI and JSON parsing

### Two UI modes

The window toggles between two overlapping views via `Visibility`:
1. **SpotlightView** — The search bar + results list
2. **ManagerView** — The settings dashboard (tabbed: General, Shortcuts, Commands, Personalization)

Within SpotlightView, a **ChatScroll** panel (animated with `ScaleTransform`) expands below the input box when AI chat is active.

### Input dispatch logic (`InputBox_PreviewKeyDown` → Enter)

1. If search results are showing → `LaunchApp(selectedItem)`
2. If text matches a built-in keyword (`shortcuts`, `settings`, `commands`) → `EnterManagerMode(tab)`
3. If text matches a custom shortcut keyword → open URL in browser
4. Otherwise → `AnimateToChatMode()` + `ProcessLocalCommand()` + `AskGeminiAsync()`

`LaunchApp` dispatches by `FilePath` prefix:
- `"MATH"` → copy result to clipboard
- `"COMMAND:*"` → built-in settings/shortcuts/commands/resume actions
- `"EXTENSION:*"` → run PowerShell via `ExecuteExtension()`
- `"SHORTCUT:*"` → auto-fill keyword into input box
- Anything else → `Process.Start()` on the `.lnk` path

### Gemini API integration

All API calls go through `ExecuteGeminiRequest(object requestBody)` which POSTs to:
```
https://generativelanguage.googleapis.com/v1beta/models/{SelectedModel}:generateContent?key={ApiKey}
```

The response is deserialized into `GenerateContentResponse` from `Google.GenAI`. Chat context is maintained in `_chatHistory` (static `List<Content>`). The chat supports one round of function calling: if Gemini returns a `FunctionCall` for `execute_wmi_query`, the app runs `ExecuteWmiQuery()` locally and sends the result back in a second request.

### Personalization flow

1. User describes desired look in natural language
2. `GeneratePersonalizationPreview_Click` → `ExecuteGeminiRequest` with `PersonalizationManager.GetPersonalizationSystemPrompt()` → returns JSON
3. `PersonalizationManager.ParseResponse()` produces a `PersonalizationProposal` (only filled fields are applied)
4. `BackupCurrentSettings()` snapshots `_appSettings` fields
5. `ApplyProposalTemporarily()` updates `_appSettings` in-memory + calls `ApplyPersonalizationSettings()` (no file write)
6. User clicks Accept → `SaveSettings()` + clears backup; or Reject → `RestoreSettingsBackup()`

### Global hotkey & window lifecycle

- Win32 `RegisterHotKey` (Alt+Space) is registered in `OnSourceInitialized` via P/Invoke on `user32.dll`
- `WndProc` hook handles `WM_HOTKEY` → `ToggleWindow()` and suppresses `WM_SYSCOMMAND`/`SC_KEYMENU` (prevents Alt+Space opening system menu)
- `OnClosing` is cancelled (hides instead); actual exit only via tray → "Exit" which sets `_isExiting = true`

## Data Files (next to the exe)

| File | Purpose |
|---|---|
| `settings.json` | `AppSettings` — API key, model selection, all personalization |
| `shortcuts.json` | `List<CustomShortcut>` — web search keywords |
| `%AppData%\Compass\Extensions\*.json` | `CompassExtension` — AI-generated PowerShell commands |

## Key Dependencies

- `Google.GenAI` (1.2.0) — Gemini API client types (`Content`, `Part`, `FunctionDeclaration`, `GenerateContentResponse`)
- `Newtonsoft.Json` (13.0.3) — present but `System.Text.Json` is used for all actual serialization
- `System.Drawing.Common` — icon extraction via `Icon.ExtractAssociatedIcon`
- `System.Management` — WMI queries via `ManagementObjectSearcher`
- `System.Windows.Forms` — tray icon (`NotifyIcon`) only; `System.Windows.Forms` and `System.Drawing` namespaces are explicitly removed from implicit usings to avoid conflicts with WPF types
