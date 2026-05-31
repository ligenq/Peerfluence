# Peerfluence

Peerfluence is a Windows-first Avalonia desktop client for BitTorrent, built on top of
[PeerSharp](https://github.com/ligenq/PeerSharp). It focuses on giving a local user full
control over torrent downloads while also exposing a local MCP interface that can be used
by AI agents for diagnostics, automation, and repeatable UI testing.

The application is written in C#/.NET, uses Avalonia and SukiUI for the desktop UI,
Material.Icons.Avalonia for icons, Microsoft.Extensions.Hosting for application
lifecycle, and PeerSharp for BitTorrent engine behavior.

## Status

Peerfluence is actively being prepared for normal desktop distribution and Microsoft
Store distribution. The core app, settings system, torrent controls, MCP integration,
natural-language UI test harness, Velopack update path, and initial MSIX packaging path
are in place.

## Main Capabilities

- Add torrents from `.torrent` files, magnet links, or the clipboard.
- Preview add-torrent options before adding, including torrent name, size, files,
  destination, limits, trackers, and metadata.
- Fetch magnet metadata in the background so magnet links can show the same details as
  torrent files once metadata arrives.
- Optionally skip the add-torrent options dialog for future additions.
- Start, stop, resume, remove, and force-recheck torrents.
- Remove torrents with explicit choices: remove only, delete downloaded files, delete
  torrent metadata, or delete everything.
- Optionally remember the remove choice and skip the confirmation dialog.
- Show live aggregate download/upload speed, active torrent count, and peer count.
- Show torrent details: files, trackers, peers, pieces, status, progress, paths, hash,
  limits, ratio rules, seed time rules, and queue priority.
- Select files, set file priorities, and apply per-file download choices.
- Add/remove trackers and manually announce.
- Save resume data.
- Change a torrent's download path.
- Stream eligible files through the configured external media player.
- Show notifications for finished downloads, metadata readiness, torrent errors, and
  completion actions.
- Persist session data and settings across restarts.
- Use DHT, NAT-PMP, UPnP, proxy settings, encryption mode, blocklist, and GeoIP settings.
- Use fixed or automatic OS-assigned listening ports for TCP/uTP.
- Configure disk read/write limits separately from network settings.
- Configure queue management with maximum active downloads/seeds.
- Configure a completion action program/script that runs when a torrent finishes.
- Customize theme variant, color palette, background style, and language.
- Self-update through Velopack for direct-download builds.
- Use Microsoft Store-managed updates for Store builds.
- Expose a local MCP server for AI-assisted control and diagnostics.
- Run a UI-agent mode for natural-language, AI-executable UI test cases.
- Handle crashes with native platform dialogs and crash logs.

## User-Facing Workflows

### Adding Torrents

The main Downloads view supports adding `.torrent` files and magnet links. When the add
options dialog is enabled, Peerfluence shows the most useful details first: name, size,
destination, included files, and start-immediately choice. Advanced options are grouped
separately and include hash/version/piece metadata, limits, queue priority, ratio/seed
time limits, private flag, and trackers.

For magnet links, Peerfluence starts metadata discovery in the background. The dialog can
initially show pending metadata and then update once the network returns torrent metadata.

### Removing Torrents

Removal is deliberately explicit. The confirmation dialog offers:

- Remove torrent only.
- Remove and delete downloaded files.
- Remove and delete torrent metadata.
- Remove everything.

The user can remember the selected remove action and skip the dialog in the future. The
choice can be changed in Settings.

### Completion Actions

Peerfluence can run a configured program or script when a torrent finishes. The action can
use tokens such as `{name}`, `{hash}`, `{downloadPath}`, and `{totalSize}` in arguments
and working directory templates. The action supports a timeout and hidden-window mode.

## Settings

Settings are stored as JSON and loaded on startup. The major settings groups are:

- Storage and session: download folder, session folder, session persistence, add/remove
  dialog preferences.
- Network and connectivity: DHT, NAT-PMP, UPnP, automatic or fixed listening port,
  listening-port hints, and port-mapping status.
- Performance: disk read/write limits.
- Queue management: enable queueing, max active downloads, max active seeds.
- Security and privacy: encryption mode, blocklist, GeoIP, proxy type/host/port,
  credentials, proxy peers, and proxy trackers.
- Media player: external media player path.
- Completion action: program/script, arguments, working directory, timeout, run hidden.
- Updates: Velopack update URL for direct builds, or Microsoft Store-managed update
  messaging for Store builds.
- Appearance: system/light/dark theme, color theme, background style, language.

Default data locations:

- Settings: `%LocalAppData%\Peerfluence\settings.json`
- Session data: `%LocalAppData%\Peerfluence\Session`
- Downloads: `%UserProfile%\Downloads\Peerfluence`
- Logs: `%LocalAppData%\Peerfluence\peerfluence.log`

Use `--profile <path>` to run with an isolated profile. This is especially useful for
testing.

## MCP Integration

Peerfluence includes a local MCP server. It is disabled by default for normal use. Enable
it by setting `Mcp.Enabled` to `true` in `settings.json`.

Destructive MCP tools, such as torrent removal, settings updates, and application
shutdown, require `Mcp.AllowDestructiveTools` to be `true`, unless the app is launched in
UI-agent mode.

The app-side MCP server communicates over a local named pipe. The command-line `--mcp`
mode is a stdio JSON-RPC proxy that connects to the already-running app.

### MCP Tools

- `add_torrent`: add by magnet link, `.torrent` file path, or base64 `.torrent` data.
- `manage_torrent`: pause, resume, or remove by info hash.
- `take_screenshot`: capture the current application window.
- `shutdown_application`: gracefully shut down Peerfluence.
- `update_settings`: update application settings from JSON.
- `invoke_ui_action`: invoke UI actions such as `pause_all` or `resume_all`.
- `get_torrent_diagnostics`: inspect trackers, peers, missing pieces, and errors.
- `set_file_priority`: set the priority for a torrent file.

### MCP Resources

- `logs://latest`
- `engine://stats`
- `engine://torrents/active`
- `engine://alerts/recent`
- `torrent://{infoHash}/files`
- `torrent://{infoHash}/peers`

### MCP Prompts

- `performance_audit`
- `crash_investigator`
- `ui_test_case_runner`

## UI-Agent Testing

UI-agent mode is intended for AI-executable, natural-language test cases against the real
desktop app. It force-enables the local MCP server for that process, enables UI-agent
tools, skips the single-instance lock, and allows destructive test actions inside the
isolated profile.

Start the application:

```powershell
dotnet run --project C:\repos\Peerfluence\Peerfluence\Peerfluence.csproj -- --ui-agent --profile C:\temp\peerfluence-ai-test
```

Start the proxy in a second process:

```powershell
dotnet run --project C:\repos\Peerfluence\Peerfluence\Peerfluence.csproj -- --mcp --profile C:\temp\peerfluence-ai-test
```

The proxy uses newline-delimited JSON-RPC. Do not send `Content-Length` framing.

UI-agent tools include:

- `ui_agent_get_state`
- `ui_agent_load_torrent_file`
- `ui_agent_resume_torrent`
- `ui_agent_stop_torrent`
- `ui_agent_select_torrent`
- `ui_agent_wait_for_torrent`
- `ui_agent_assert_torrent`
- `ui_agent_get_timeline`
- `ui_agent_clear_timeline`
- `ui_agent_cleanup`

Natural-language test cases and runner guidance live in
`Testing\AI_UI_Test_Cases.md`. The `Testing` folder also contains fixture torrent data
and a magnet link used by the test cases.

## Build And Run

Requirements:

- Windows with .NET SDK 10.
- Windows SDK if building MSIX packages.

Run the app:

```powershell
dotnet run --project Peerfluence\Peerfluence.csproj
```

Run with an isolated profile:

```powershell
dotnet run --project Peerfluence\Peerfluence.csproj -- --profile C:\temp\peerfluence-profile
```

Run tests:

```powershell
dotnet test Peerfluence.Tests\Peerfluence.Tests.csproj
dotnet test Peerfluence.HeadlessTests\Peerfluence.HeadlessTests.csproj
```

If a Windows build fails with file-lock errors in `bin` or `obj`, rerun the command
serially. Parallel build/test commands can occasionally collide on generated files.

## Distribution

### Direct Downloads

Direct-download builds use Velopack for installation and self-updates. The Settings
Updates panel shows the update URL, update check, and restart/apply controls when the app
is installed through Velopack.

### Microsoft Store

Microsoft Store builds compile with:

```powershell
/p:DistributionChannel=MicrosoftStore
```

That switch:

- Defines `MICROSOFT_STORE`.
- Compiles out `VelopackApp.Build().Run()`.
- Registers `MicrosoftStoreUpdateService`.
- Hides Velopack update controls in Settings.
- Shows that updates are managed by Microsoft Store.

Build a local unsigned MSIX:

```powershell
.\StorePackaging\build-msix.ps1 `
  -PackageName "Peerfluence" `
  -Publisher "CN=Your Partner Center Publisher" `
  -PublisherDisplayName "Your Publisher Name"
```

The package is written to `artifacts\store\msix`.

Partner Center supplies the final package identity values after the app name is reserved.
Use those values for real Store submission builds. Local sideload testing requires a
trusted signing certificate; Store submission packages are re-signed by Microsoft after
certification.

Before Store submission, run the Windows App Certification Kit and test install, launch,
networking, downloads, notifications, settings persistence, and removal behavior on a
clean Windows user profile.

## Project Layout

- `Peerfluence`: Avalonia UI, app startup, services, MCP server, notifications, dialogs.
- `Peerfluence.Core`: UI-independent settings, service contracts, messages, engine
  services.
- `Peerfluence.Tests`: unit tests.
- `Peerfluence.HeadlessTests`: Avalonia/headless UI tests.
- `Testing`: AI-executable UI test cases and fixtures.
- `StorePackaging`: MSIX manifest template and packaging script.
- `DebuggerApp`: local helper/debug harness.

[PeerSharp](https://github.com/ligenq/PeerSharp) is consumed as a NuGet package by the
app. The separate PeerSharp source repo is useful when engine behavior needs deeper
analysis.

## Notes

- MCP is a local automation surface. Treat destructive MCP tools carefully.
- UI-agent tests should always use a disposable profile under `C:\temp` or another
  isolated location.
- Public swarm tests can be inconclusive when peers, trackers, NAT, or firewall conditions
  are unfavorable.
- Do not commit generated packages from `artifacts\store\msix`.
