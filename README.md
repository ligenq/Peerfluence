# Peerfluence

Peerfluence is a cross-platform Avalonia desktop client for BitTorrent, with Windows
installers, portable Linux builds, and Debian packages. It is built on top of
[PeerSharp](https://github.com/ligenq/PeerSharp). It focuses on giving a local user full
control over torrent downloads while also exposing a local MCP interface that can be used
by AI agents for diagnostics, automation, and repeatable UI testing.

The application is written in C#/.NET, uses Avalonia Fluent with the Peerfluence theme for the desktop UI,
Material.Icons.Avalonia for icons, Microsoft.Extensions.Hosting for application
lifecycle, and PeerSharp for BitTorrent engine behavior.

## Main Capabilities

- Add torrents from `.torrent` files, magnet links, or the clipboard.
- Create `.torrent` files from a file or folder.
- Find torrents through a configured Torznab endpoint, such as Jackett or Prowlarr, and
  add a result without leaving the application. Nothing is shipped preconfigured.
- Optionally associate `.torrent` files and `magnet:` links with Peerfluence on Windows.
- Preview add-torrent options before adding, including torrent name, size, files,
  destination, limits, trackers, and metadata.
- Fetch magnet metadata in the background so magnet links can show the same details as
  torrent files once metadata arrives.
- Optionally skip the add-torrent options dialog for future additions.
- Start, stop, resume, remove, and force-recheck torrents.
- Remove torrents with explicit choices: remove only, delete downloaded files, delete
  torrent metadata, or delete everything.
- Optionally remember the remove choice and skip the confirmation dialog.
- Choose between a simple and an advanced interface on first launch, and switch at any
  time from settings. Simple mode is one screen — add, watch, open the folder — with no
  dashboard, details pane or side menu.
- Show live aggregate download/upload speed, active torrent count, and peer count in a
  single status line above the list.
- Search downloads by name and filter by state: all, downloading, seeding, or completed.
- Show torrent details: files, trackers, peers, pieces, status, progress, paths, hash,
  limits, ratio rules, seed time rules, and queue priority. The details pane is opened from
  the toolbar and closed by default, so the list has the whole window until it is wanted.
- Select files, set file priorities, and apply per-file download choices.
- Rename a file within a torrent, taking what has already been downloaded with it.
- Add/remove trackers, manually announce, and scrape trackers for seeder/leecher counts.
- Add and remove BEP 19 web seeds, including ones a torrent's own metadata declared.
- Offer peer addresses by hand, for a peer no tracker, DHT node or exchange has mentioned.
- Save resume data.
- Change a torrent's download path, moving the downloaded data along with it.
- Pause and resume the whole session in one action, which starts back exactly the torrents
  it stopped rather than everything.
- Set per-torrent connection and upload-slot limits, and turn on BEP 16 super-seeding for
  an initial seed introducing content to an empty swarm.
- Stream eligible files through the configured external media player.
- Share token-protected streaming links with another device on a configured local address.
- Tune streaming read-ahead, buffering queues and timeouts without changing the torrent's
  configured download strategy.
- Show notifications for finished downloads, metadata readiness, torrent errors, and
  completion actions.
- Persist session data and settings across restarts.
- Use DHT, NAT-PMP, UPnP, proxy settings, encryption mode, blocklist, and GeoIP settings.
- Download I2P torrents through a configured SAM router, and SSL torrents using a certificate
  issued by the torrent's embedded certificate authority.
- Use fixed or automatic OS-assigned listening ports for TCP/uTP.
- Bind every socket to a single local address, so traffic stops rather than leaving by
  another route when a VPN goes away.
- Limit how many connections one address may hold on a single torrent.
- Configure disk read/write limits separately from network settings.
- Configure queue management with maximum active downloads/seeds.
- Configure a completion action program/script that runs when a torrent finishes.
- Customize theme variant, accent color, and language.
- Self-update through Velopack for direct-download builds.
- Expose a local MCP server for AI-assisted control and diagnostics.
- Run a UI-agent mode for natural-language, AI-executable UI test cases.
- Handle crashes with native platform dialogs and crash logs.

## User-Facing Workflows

### Streaming while downloading

Open the details pane and use Play beside a supported audio or video file. PeerSharp
prioritizes the pieces that open streams need and restores ordinary selection when the last
stream closes; playing a file does not overwrite the configured download strategy. Cancel
stops preparation, and Stop streaming closes the listener and its connections.

To play on another device, set this computer's address under Settings → Storage & session →
Media player, then use Copy streaming link beside the file. Sharing produces a link with a
generated access token. While the address is blank nothing is shared and Copy streaming link
reports what to set, rather than copying a loopback link no other device could reach. The link
remains available until streaming stops, another file starts streaming, the selected torrent
changes, or Peerfluence exits. Anyone with the link can access the stream while it is active. A
shared port of zero selects a free port automatically. Local Play always stays on loopback,
regardless of the sharing address.

The streaming options expand to show minimum read-ahead in MiB, adaptive read-ahead in seconds,
the peer queue while buffering, and data/metadata timeouts. Zero disables a timeout. Policy
changes apply to streams opened afterward. The MCP settings tool accepts the same policy as a
`Streaming` object with `ReadAheadBytes`, `ReadAheadSeconds`, `RequestQueueSecondsWhileBuffering`,
`DataWaitTimeoutSeconds`, `MetadataWaitTimeoutSeconds`, `BindAddress`, and `Port`.

### Adding Torrents

The main Downloads view supports adding `.torrent` files and magnet links. When the add
options dialog is enabled, Peerfluence shows the most useful details first: name, size,
destination, included files, and start-immediately choice. Advanced options are grouped
separately and include hash/version/piece metadata, limits, queue priority, ratio/seed
time limits, private flag, and trackers.

For magnet links, Peerfluence starts metadata discovery in the background. The dialog can
initially show pending metadata and then update once the network returns torrent metadata.

On Windows, Settings can register Peerfluence as the per-user handler for `.torrent`
files and `magnet:` links. Launch arguments are forwarded to the running instance, so
double-clicking a torrent file or opening a browser magnet link adds it to the existing
Peerfluence window.

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
- Network and connectivity: DHT, NAT-PMP, UPnP, whether several connections may share one
  IP address, how many connections one address may hold on a torrent, the local address to
  bind to, automatic or fixed listening port, listening-port hints, and port-mapping
  status.
- Performance: disk read/write limits.
- Queue management: enable queueing, max active downloads, max active seeds.
- Security and privacy: default encryption mode, independent incoming/outgoing overrides,
  encrypted MSE payload preference, blocklist, GeoIP, proxy type/host/port,
  credentials, proxy peers, and proxy trackers.
- Search: Torznab endpoint URL and API key used by the Find Torrents page.
- Media player: external media player path.
- Completion action: program/script, arguments, working directory, timeout, run hidden.
- Updates: Velopack update URL and restart/apply controls for direct builds.
- Appearance: interface mode (simple or advanced), Midnight or Midnight Light theme,
  automatic switching with the system preference, accent color, and language.

Encryption changes apply to new peer connections. Incoming and outgoing policies inherit the
default mode unless explicitly overridden. Encrypted MSE payloads remain preferred by default;
turning off that preference permits negotiated plaintext only when encryption is optional.
The MCP settings tool accepts `Network.IncomingEncryptionMode`, `Network.OutgoingEncryptionMode`
(`Inherit`, `Allow`, `Require`, or `Refuse`) and `Network.PreferRc4` too, and applies them to new
connections after saving. The metadata wait timeout also controls magnet previews; zero waits
until the dialog is cancelled or the magnet is added.

PeerSharp 5.2.0's reduced resume/autosave allocations, pooled streaming read-ahead, larger magnet
metadata budget, HTTP seed support, and protocol/lifecycle fixes apply through the existing engine
integration. Session persistence uses PeerSharp's implementation directly.

For I2P, enable **I2P** in Security and privacy and enter the SAM router host and port
(default `127.0.0.1:7656`). A running I2P router with SAM enabled is required. Restart Peerfluence
after changing I2P settings. I2P torrents stay isolated from public-network peers unless you
explicitly enable mixed networks, which can expose your public IP address. These options are also
available through MCP under `Network.EnableI2p`, `Network.I2pSamHost`, `Network.I2pSamPort`, and
`Network.I2pAllowMixedNetworks`.

For SSL torrents, open the `.torrent` file and select a `.pfx` or `.p12` certificate containing
its private key, signed by the torrent's authority and authorized for its name. The certificate
dialog appears even when ordinary add options are skipped. Passwords and private keys are not
stored in settings or session files. After restarting, SSL torrents remain stopped and visible;
press **Start** to select their certificate again. Attaching a certificate preserves downloaded
files, resume progress, file selections, and per-torrent limits. Certificate errors remain in the
dialog so they can be corrected.

Settings apply and persist as they are changed; there is no Save button. Reset restores
the defaults. In simple mode the screen shows only the download folder and appearance —
the rest is hidden rather than turned off, and comes back with advanced mode.

Default data locations use the operating system's application-data and Downloads folders.
On Linux, the configured XDG Downloads directory is honored. Typical Windows locations are:

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
- `search_torrents`: search the configured Torznab endpoint for torrents.
- `manage_torrent`: pause, resume, or remove by info hash.
- `take_screenshot`: capture the current application window.
- `shutdown_application`: gracefully shut down Peerfluence.
- `update_settings`: update application settings from JSON.
- `invoke_ui_action`: invoke UI actions such as `pause_all` or `resume_all`.
- `get_torrent_diagnostics`: inspect trackers, peers, missing pieces, and errors.
- `set_file_priority`: set the priority for a torrent file.
- `configure_torrent`: set super-seeding, maximum connections, and maximum upload slots.
- `manage_web_seeds`: list, add, or remove a torrent's BEP 19 web seed URLs.
- `scrape_trackers`: ask a torrent's trackers for current seeder and leecher counts.
- `rename_torrent_file`: store one of a torrent's files under a different name. Destructive.
- `move_torrent_storage`: move a torrent's data to a new directory. Destructive.

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
dotnet run --project Peerfluence/Peerfluence.csproj -- --ui-agent --profile C:\temp\peerfluence-ai-test
```

Start the proxy in a second process:

```powershell
dotnet run --project Peerfluence/Peerfluence.csproj -- --mcp --profile C:\temp\peerfluence-ai-test
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
[Testing/AI_UI_Test_Cases.md](Testing/AI_UI_Test_Cases.md). The `Testing` folder also contains fixture torrent data
and a magnet link used by the test cases.

## Development Quality Gates

The repository pins the .NET SDK in `global.json`, treats compiler and analyzer warnings as
errors in every configuration, audits NuGet dependencies during restore, and verifies formatting
with `dotnet format`. The normal CI workflow builds Debug and Release, runs unit/headless tests on
Linux and Windows, executes an isolated smoke test, and publishes Cobertura coverage artifacts.

Security and depth checks are separate workflows: CodeQL and dependency review run for pull
requests, while scheduled deep validation runs mutation analysis with Stryker and the interactive
Windows UI suite.

Before opening a pull request, the shortest local gate is:

```powershell
dotnet restore Peerfluence.slnx
dotnet format Peerfluence.slnx --verify-no-changes --no-restore
dotnet build Peerfluence.slnx --configuration Debug --warnaserror
dotnet test --project Peerfluence.Tests/Peerfluence.Tests.csproj --configuration Debug
dotnet test --project Peerfluence.HeadlessTests/Peerfluence.HeadlessTests.csproj --configuration Debug
```

Live Torznab contract tests are opt-in so a local configuration cannot make ordinary builds depend
on a running indexer. Set `PEERFLUENCE_RUN_LIVE_TESTS=1` when intentionally running them.

## Build And Run

Requirements:

- Windows, Linux, or macOS with the .NET SDK selected by `global.json` (10.0.400 or a
  compatible patch).

Run the app:

```shell
dotnet run --project Peerfluence/Peerfluence.csproj
```

Builds use the published **PeerSharp 5.2.0** NuGet package, including when a sibling engine
checkout exists. No local package feed is needed. For engine development, explicitly opt into
the sibling checkout (including the headless tests):

```shell
dotnet run --project Peerfluence/Peerfluence.csproj -p:UseLocalPeerSharp=true
```

Set `-p:PeerSharpProjectPath=/path/to/PeerSharp.csproj` as well to use another checkout.

Run with an isolated profile:

```shell
dotnet run --project Peerfluence/Peerfluence.csproj -- --profile /path/to/peerfluence-profile
```

Run tests:

```shell
dotnet test --project Peerfluence.Tests/Peerfluence.Tests.csproj
dotnet test --project Peerfluence.HeadlessTests/Peerfluence.HeadlessTests.csproj
```

Create a self-contained portable build by selecting a runtime identifier:

```shell
dotnet publish Peerfluence/Peerfluence.csproj -c Release -r linux-x64 --self-contained
```

Supported publish targets include `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`,
`osx-x64`, and `osx-arm64`. Native AOT publishing must be performed on the target OS (or
in a matching CI runner with the required native toolchain). A direct publish for Linux or macOS
produces a portable application directory; use the packaging scripts below for Linux
archives and Debian desktop integration.

## Distribution

Windows direct-download builds use Velopack for installation and self-updates. The Settings
Updates panel shows the update URL, update check, and restart/apply controls when the app
is installed through Velopack.

Linux releases provide portable AppDir archives and Debian packages for x64 and ARM64.
The Debian package registers the application launcher, `.torrent` MIME handler, and
`magnet:` URL handler. Build one locally with:

```shell
./ReleasePackaging/build-linux.sh --version 2.1.0 --rid linux-x64
```

Build a Velopack release:

```powershell
.\ReleasePackaging\build-velopack.ps1 -Version 2.1.0
```

The script publishes the app, creates the Velopack release, and writes artifacts to
`artifacts\velopack\releases\win-x64`. Upload the full contents of that directory to a
GitHub release on [ligenq/Peerfluence](https://github.com/ligenq/Peerfluence). Users
download `Peerfluence.Desktop-win-Setup.exe`; the remaining files are used by the
auto-update feed. The default update URL is `https://github.com/ligenq/Peerfluence`.

See [ReleasePackaging/README.md](ReleasePackaging/README.md) for Linux formats, AppImage generation, MSI generation,
release notes, update hosting, and optional signing details. GitHub Actions workflows run
the unit/headless test suite on Windows and Linux and can create a combined GitHub release.

## Project Layout

- `Peerfluence`: Avalonia UI, app startup, services, MCP server, notifications, dialogs.
- `Peerfluence.Core`: UI-independent settings, service contracts, messages, engine
  services.
- `Peerfluence.Theme`: shared Avalonia theme, palettes, and control styles.
- `Peerfluence.Theme.Gallery`: control gallery and visual capture tool.
- `Peerfluence.Tests`: unit tests.
- `Peerfluence.HeadlessTests`: Avalonia/headless UI tests.
- `Peerfluence.UiTests`: Windows UI Automation tests that drive the built application.
- `Testing`: AI-executable UI test cases and fixtures.
- `ReleasePackaging`: Windows and Linux packaging scripts and instructions.
- `DebuggerApp`: local helper/debug harness.
- `tools/DumpAnalyzer`: managed memory dump analysis helper.

## Notes

- MCP is a local automation surface. Treat destructive MCP tools carefully.
- UI-agent tests should always use a disposable profile under `C:\temp` or another
  isolated location.
- Public swarm tests can be inconclusive when peers, trackers, NAT, or firewall conditions
  are unfavorable.
