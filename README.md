# Peerfluence

Peerfluence is a cross-platform Avalonia desktop client for BitTorrent built around PeerSharp. It uses
SukiUI for the look-and-feel, Material.Icons.Avalonia for icons, Lamar for DI, and
Microsoft.Extensions.Hosting for app lifecycle management.

## Current status

The application is production-ready (v1.0.0) with a complete feature set. Core torrent controls, notifications,
session persistence, error handling, and an MCP integration for AI assistants are all in place.

## How the application works

- App startup creates an `IHost` with Lamar, then starts Avalonia.
- Settings are loaded from app data on startup and saved on shutdown.
- A PeerSharp engine is created using the persisted settings and started as a hosted service.
- Alerts from PeerSharp are monitored in the background and pushed into the UI thread.
- The UI is structured as a SukiWindow with navigation for Downloads, Details, and Settings.
- View models are registered by convention and resolved from DI.

## Features

- Add torrent by file picker (".torrent") or magnet link
- Start, stop, and remove a selected torrent
- Live list updates via PeerSharp alerts
- Engine-wide stats (total up/down, active torrents, peers)
- Torrent details: files, trackers, peers, and basic status
- Per-torrent download/upload limits and download strategy
- File selection and priority editing (with presets)
- Theme variant and palette selection (light/dark/system + color themes)
- Notifications/toasts for torrent events and errors
- Human-friendly size/speed formatting across the UI
- Queue management with configurable active download/seed limits
- Per-torrent ratio limit and seed time auto-stop
- Session persistence and resume data management
- Self-update support via Velopack
- MCP server for AI assistant integration (Claude, etc.)
- UI-agent mode for natural-language assisted application testing
- Cross-platform crash handling with native dialogs

## Configuration & data locations

Default locations are created under the user's profile/app data, and can be changed in Settings:

- Settings file: `%LocalAppData%/Peerfluence/settings.json`
- Session data: `%LocalAppData%/Peerfluence/Session`
- Downloads: `%UserProfile%/Downloads/Peerfluence`

Settings are saved to JSON and reloaded on startup.

MCP is disabled by default. To expose the local MCP server, set `Mcp.Enabled` to `true` in `settings.json`.
Destructive MCP tools such as torrent removal, settings updates, and application shutdown also require
`Mcp.AllowDestructiveTools` to be `true`.

For AI-assisted UI testing, launch the real application with `--ui-agent`. This enables the local MCP server
for that process only, adds UI test tools, skips the single-instance lock, and allows destructive test actions
without persisting those MCP settings to `settings.json`. Use `--profile <path>` to isolate settings, logs,
session data, token files, and downloads for a test run. Connect the agent through the existing `--mcp` stdio
proxy with the same profile path.

UI-agent tools include loading torrent files, selecting/stopping torrents, waiting for torrent conditions,
asserting state/progress, capturing structured app state, reading a test timeline, clearing the timeline, and
cleaning up torrents after a test.

## Build notes

If builds fail with access denied to `obj` or temp files, your system may be blocking `dotnet` writes
(Windows Defender Controlled Folder Access). Allow `dotnet.exe` and `MSBuild.exe` or whitelist the repo
folder, then rebuild.

## Key projects

- `Peerfluence`: Avalonia UI and application logic
- `Peerfluence.Core`: UI-agnostic services, contracts, settings, and messages
- `PeerSharp`: BitTorrent engine library

## Running

```powershell
# from repo root

dotnet run --project Peerfluence\Peerfluence.csproj

dotnet run --project Peerfluence\Peerfluence.csproj -- --ui-agent --profile C:\temp\peerfluence-test

dotnet run --project Peerfluence\Peerfluence.csproj -- --mcp --profile C:\temp\peerfluence-test
```
