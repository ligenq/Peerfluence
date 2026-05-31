# Peerfluence AI-Executable Test Cases

These natural-language test cases are written for an AI agent testing the real Peerfluence desktop app through its local MCP UI-agent interface. The tests are meant to be executable by a local AI runner that can start processes, write JSON lines to standard input, read JSON lines from standard output, and inspect files.

MCP, in this repo, is a local JSON-RPC control channel. The Peerfluence app runs normally with a window. A second `--mcp` proxy process connects to that running app and exposes tools such as `ui_agent_load_torrent_file`, `ui_agent_get_state`, and `ui_agent_assert_torrent`.

Use the fixture files in this folder:

- Torrent file: `C:\repos\Peerfluence\Testing\2026-04-21-raspios-trixie-arm64-full.img.xz.torrent`
- Magnet link file: `C:\repos\Peerfluence\Testing\magnet link.txt`

## Runner Responsibilities

The AI runner should do four things:

1. Start Peerfluence in UI-agent mode with an isolated profile.
2. Start the `--mcp` proxy with the same profile.
3. Send newline-delimited JSON-RPC messages to the proxy's stdin and read newline-delimited JSON-RPC responses from stdout.
4. Execute each test case, recording enough evidence to explain pass, fail, or inconclusive results.

Always use an isolated profile under `C:\temp` or another disposable folder. UI-agent mode allows destructive actions, downloads files, saves settings, and creates logs inside that profile.

## General Runner Instructions

1. Start Peerfluence in UI-agent mode with an isolated profile, for example:
   `dotnet run --project C:\repos\Peerfluence\Peerfluence\Peerfluence.csproj -- --ui-agent --profile C:\temp\peerfluence-ai-test`
2. Connect the AI agent through the MCP proxy using the same profile:
   `dotnet run --project C:\repos\Peerfluence\Peerfluence\Peerfluence.csproj -- --mcp --profile C:\temp\peerfluence-ai-test`
3. Wait until the isolated profile contains `mcp.token`; this means the app-side MCP server is ready.
4. Speak MCP to the proxy as newline-delimited JSON-RPC messages. Do not use `Content-Length` headers with this transport.
5. Send `initialize`, then send the `notifications/initialized` notification before listing or calling tools.
6. Call `tools/list` and confirm the expected tools are present before starting a test.
7. Before every test case, call `ui_agent_get_state` and verify `WindowAvailable` is `true`.
8. Before every test case, call `ui_agent_clear_timeline`.
9. Prefer structured MCP/UI-agent tools for actions and assertions. Use `take_screenshot` only for visual confirmation or failure evidence.
10. On every failure, call `ui_agent_get_timeline`, `ui_agent_get_state`, read `logs://latest`, and call `take_screenshot`, then report the observed state.
11. Unless a test says otherwise, finish with `ui_agent_cleanup` using `removeTorrents=true` and `clearSelection=true`.
12. At the end of a run, call `shutdown_application`, wait for the UI-agent process to exit, and delete the isolated profile if the test artifacts are no longer needed.

## MCP Client Notes

The local `--mcp` proxy reads and writes one JSON-RPC message per line. Each message is one complete JSON object followed by `\n`.

Initialize the connection:

```json
{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"local-ai-test-runner","version":"0.1.0"}}}
```

Then send this notification. It has no `id` because notifications do not receive responses:

```json
{"jsonrpc":"2.0","method":"notifications/initialized","params":{}}
```

List tools:

```json
{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}
```

Call a tool:

```json
{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"ui_agent_get_state","arguments":{}}}
```

Call a tool with arguments:

```json
{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"ui_agent_load_torrent_file","arguments":{"path":"C:\\repos\\Peerfluence\\Testing\\2026-04-21-raspios-trixie-arm64-full.img.xz.torrent"}}}
```

Read a resource:

```json
{"jsonrpc":"2.0","id":4,"method":"resources/read","params":{"uri":"logs://latest"}}
```

Tool responses usually contain `content[0].text`. Many Peerfluence tools put JSON inside that text field, so the runner often needs to parse the outer JSON-RPC response first, then parse the `text` string as JSON.

The proxy must be launched while the UI-agent application is already running with the same profile path. If `initialize` times out, check `peerfluence.log` in the isolated profile. A log entry saying the server failed to parse a message starting with `C` usually means the client accidentally sent `Content-Length` framing.

## Common Tool Names

Use these tools for most test actions:

- `ui_agent_get_state`: Returns window availability, selected torrent, and torrent summaries.
- `ui_agent_load_torrent_file`: Loads a `.torrent` file and selects it.
- `ui_agent_resume_torrent`: Starts or resumes a torrent by hash, exact name, or unique partial name.
- `ui_agent_stop_torrent`: Stops a torrent by hash, exact name, or unique partial name.
- `ui_agent_select_torrent`: Selects a torrent by hash, exact name, or unique partial name.
- `ui_agent_wait_for_torrent`: Polls until a torrent reaches a state and/or progress threshold.
- `ui_agent_assert_torrent`: Performs a pass/fail assertion on torrent state and progress.
- `ui_agent_get_timeline`: Returns UI-agent actions, waits, and assertions for failure diagnosis.
- `ui_agent_clear_timeline`: Clears the timeline before a test.
- `ui_agent_cleanup`: Removes torrents and/or clears selection.
- `get_torrent_diagnostics`: Returns trackers, peers, piece counts, missing pieces, and errors for a torrent hash.
- `take_screenshot`: Returns a screenshot image of the app window.
- `shutdown_application`: Requests app shutdown. Use this only at the end of a disposable test session.

## Data Conventions

Torrent summaries use these important fields:

- `Name`: Torrent display name.
- `Hash`: Lowercase hex info hash. Prefer this identifier after loading a torrent.
- `State`: Usually `Active`, `Stopped`, `Stopping`, `CheckingFiles`, or `DownloadingMetadata`.
- `Progress`: A fraction from `0.0` to `1.0`. For example, `0.10` means 10%.
- `DownloadSpeed` and `UploadSpeed`: Bytes per second.
- `Peers` and `Seeds`: Connected peer and seed counts at the time of the snapshot.
- `Size`: Total torrent size in bytes.

Tool arguments named `minProgressPercent` and `maxProgressPercent` use percent values, not fractions. Use `minProgressPercent=10` to mean at least 10%, even though the returned `Progress` field will be around `0.10`.

## Result Classification

Use `PASS` when every pass criterion is satisfied. Use `FAIL` when Peerfluence returns an unexpected error, reports incorrect state, crashes, or violates the pass criteria. Use `INCONCLUSIVE` when the only blocker is an external condition such as unavailable BitTorrent traffic, no peers, blocked firewall/NAT, or a public swarm timeout.

For every result, report the test case name, final status, key tool calls, final `ui_agent_get_state`, and any relevant timeline/log excerpts. For networked tests, also report latest progress, speed, peer count, seed count, and tracker status.

## Test Case 1: UI-Agent Startup And Empty State

Purpose: Verify that UI-agent mode exposes test tools and reports a usable application window.

Steps:

1. Call `ui_agent_get_state`.
2. Confirm that `WindowAvailable` is `true`.
3. Confirm that the `Torrents` collection is present.
4. If any torrents already exist, call `ui_agent_cleanup` with `removeTorrents=true` and `clearSelection=true`, then call `ui_agent_get_state` again.
5. Confirm that the final `Torrents` collection is empty and `SelectedTorrentName` is null or absent.

Pass criteria:

- The application window is available through MCP.
- Cleanup can leave the UI-agent profile with zero torrents.
- No MCP tool call returns an unexpected error.

## Test Case 2: Load Torrent File Through UI-Agent

Purpose: Verify that `ui_agent_load_torrent_file` can load a local `.torrent` file, select it, and expose structured state.

Steps:

1. Call `ui_agent_load_torrent_file` with path `C:\repos\Peerfluence\Testing\2026-04-21-raspios-trixie-arm64-full.img.xz.torrent`.
2. Confirm the tool returns a torrent summary.
3. Record the returned torrent name and hash.
4. Call `ui_agent_get_state`.
5. Confirm exactly one torrent is present, or at least confirm the returned hash appears in the torrent list if the environment already had torrents.
6. Confirm `SelectedTorrentHash` equals the loaded torrent hash.
7. Confirm the torrent name contains `raspios-trixie-arm64-full.img.xz`.
8. Call `ui_agent_assert_torrent` using the returned hash and `minProgressPercent=0`.

Pass criteria:

- The fixture torrent loads without a validation error.
- The loaded torrent becomes the selected torrent.
- The structured state includes name, hash, state, progress, peers, seeds, and size.
- The progress assertion passes at or above 0%.

## Test Case 3: Select, Stop, And Assert Loaded Torrent

Purpose: Verify torrent selection and stop behavior through UI-agent tools.

Setup:

- Execute Test Case 2 first, or load the torrent file fixture and record its hash.

Steps:

1. Call `ui_agent_select_torrent` using the unique partial name `raspios`.
2. Call `ui_agent_get_state`.
3. Confirm the selected torrent name contains `raspios`.
4. Call `ui_agent_stop_torrent` using the selected torrent hash.
5. Call `ui_agent_wait_for_torrent` using the hash, `state=Stopped`, `minProgressPercent=0`, `timeoutSeconds=15`, and `pollIntervalMilliseconds=500`.
6. Call `ui_agent_assert_torrent` using the hash, `state=Stopped`, `minProgressPercent=0`, and `maxProgressPercent=100`.
7. Call `ui_agent_get_timeline`.

Pass criteria:

- Selecting by unique partial name succeeds.
- Stopping by hash succeeds.
- The torrent reaches or remains in `Stopped` state within 15 seconds.
- The timeline contains entries for selection, stop, wait, and assertion.

## Test Case 4: Add Torrent From Magnet Link Through General MCP Tool

Purpose: Verify the general `add_torrent` MCP tool accepts a magnet link fixture.

Steps:

1. Read the magnet link from `C:\repos\Peerfluence\Testing\magnet link.txt`.
2. Call `add_torrent` with the full magnet link text.
3. Confirm the tool reports success.
4. Call `ui_agent_get_state`.
5. Locate a torrent whose name is either `2026-04-21-raspios-trixie-arm64-full.img.xz` or whose hash matches the magnet info hash.
6. If the torrent initially has only metadata from the magnet, wait up to 30 seconds for it to appear in `ui_agent_get_state`; do not require download progress.
7. Call `ui_agent_stop_torrent` for the matching torrent.
8. Call `ui_agent_assert_torrent` with `minProgressPercent=0` and `maxProgressPercent=100`.

Pass criteria:

- The magnet link is accepted as valid input.
- The torrent appears in structured UI-agent state.
- The test does not require external network speed or full download completion.

## Test Case 4A: Download Fixture Torrent To 10 Percent

Purpose: Verify the real BitTorrent path can load the fixture torrent, connect to peers, download data, report progress, and be stopped cleanly.

Preconditions:

- Run this only in an environment where external BitTorrent traffic is allowed.
- Use an isolated profile with enough free disk space for the Raspberry Pi OS image.
- Expect this test to take longer than the smoke tests. The fixture payload is about 2 GB, so 10% is about 200 MB.
- Do not treat slow public swarm conditions as an application bug unless diagnostics show Peerfluence is failing internally.

Steps:

1. Call `ui_agent_clear_timeline`.
2. Call `ui_agent_load_torrent_file` with path `C:\repos\Peerfluence\Testing\2026-04-21-raspios-trixie-arm64-full.img.xz.torrent`.
3. Record the returned torrent hash and name.
4. Call `ui_agent_get_state` and record the current state, progress, peer count, seed count, and download speed.
5. Call `ui_agent_resume_torrent` with the recorded hash. This should succeed whether the torrent was stopped or already active.
6. Call `ui_agent_wait_for_torrent` with the hash, `minProgressPercent=10`, `timeoutSeconds=1800`, and `pollIntervalMilliseconds=5000`.
7. During the wait, periodically record progress, download speed, peer count, and seed count so a timeout can distinguish application failure from slow swarm behavior.
8. While waiting, if the condition has not matched after 2 minutes, call `get_torrent_diagnostics` and confirm trackers, peers, and missing pieces are being reported.
9. When the wait matches, call `ui_agent_assert_torrent` with the hash, `minProgressPercent=10`, and `maxProgressPercent=100`.
10. Call `get_torrent_diagnostics` and confirm the torrent hash, state, piece count, missing pieces, trackers, and peer list are returned.
11. Call `ui_agent_stop_torrent` with the hash.
12. Call `ui_agent_wait_for_torrent` with the hash, `state=Stopped`, `timeoutSeconds=30`, and `pollIntervalMilliseconds=1000`.
13. Call `ui_agent_get_timeline`.
14. Call `ui_agent_cleanup` with `removeTorrents=true` and `clearSelection=true`.

Pass criteria:

- The fixture torrent loads and is or can be made active.
- The torrent reaches at least 10% progress within 30 minutes.
- Structured state and assertions report progress at or above 10%.
- Diagnostics are available before or after the download reaches 10%.
- The torrent can be stopped and cleaned up without leaving a selected torrent or active torrent behind.

Failure reporting:

- If the torrent does not reach 10% before timeout, report this as an inconclusive network/swarm failure unless `logs://latest`, `get_torrent_diagnostics`, or the UI-agent timeline show an application error.
- Include the latest observed progress percentage, state, peer count, seed count, tracker statuses, timeline entries, and latest log excerpt.
- If progress changes between an active snapshot and a stopped snapshot, report both values. Treat the test as passed if the explicit 10% assertion passed before stopping and the torrent then reaches `Stopped`.

## Test Case 5: Add Torrent Validation Errors

Purpose: Verify that invalid `add_torrent` inputs fail clearly and safely.

Steps:

1. Call `add_torrent` with an empty string.
2. Confirm the result is an error and mentions empty input.
3. Call `add_torrent` with `not a magnet and not a path`.
4. Confirm the result is an error and indicates the input is not a valid magnet link, `.torrent` path, or base64 payload.
5. Call `add_torrent` with path `C:\repos\Peerfluence\Testing\magnet link.txt`.
6. Confirm the result is an error because only `.torrent` files can be imported by path.

Pass criteria:

- Invalid inputs produce MCP errors instead of application crashes.
- The error messages are specific enough for an AI agent to correct the action.

## Test Case 6: Diagnostics And Torrent Resources

Purpose: Verify diagnostics and resource reads for a loaded torrent.

Setup:

- Load the torrent file fixture and record its hash.

Steps:

1. Call `get_torrent_diagnostics` with the loaded torrent hash.
2. Confirm the response includes torrent name, hash, state, piece count, missing pieces, trackers, and peers.
3. Confirm at least one tracker is present and includes the Raspberry Pi tracker URL or another tracker from the fixture.
4. Read `engine://stats` and confirm it returns aggregate engine statistics.
5. Read `engine://torrents/active` and confirm the loaded torrent is represented if its state is active, or confirm the resource is valid even if the torrent is stopped.
6. Read `torrent://{hash}/files` and confirm at least one file entry is returned.
7. Read `torrent://{hash}/peers` and confirm it returns a peer list, allowing the list to be empty in offline or low-connectivity environments.

Pass criteria:

- Diagnostics are returned as structured JSON-like content.
- Resource reads do not fail for a valid loaded torrent hash.
- The test tolerates zero connected peers.

## Test Case 7: Pause, Resume, And Remove Permissions

Purpose: Verify general torrent management and destructive-tool behavior.

Setup:

- Load the torrent file fixture and record its hash.
- Confirm Peerfluence is running with `--ui-agent`, which force-enables destructive tools for the isolated test profile.

Steps:

1. Call `manage_torrent` with the loaded hash and action `pause`.
2. Confirm success.
3. Call `manage_torrent` with the loaded hash and action `resume`.
4. Confirm success.
5. Call `manage_torrent` with the loaded hash and action `remove`.
6. Confirm success.
7. Call `ui_agent_get_state`.
8. Confirm the removed hash no longer appears in the torrent list.

Pass criteria:

- Pause and resume actions use the recorded info hash successfully.
- Remove is allowed in UI-agent mode and removes the torrent from state.
- The test profile is left clean.

## Test Case 8: File Priority Tool On Fixture Torrent

Purpose: Verify `set_file_priority` can change file priorities for a real loaded torrent.

Setup:

- Load the torrent file fixture and record its hash.
- Read `torrent://{hash}/files` and record a valid file index. Prefer index `0` if present.

Steps:

1. Call `set_file_priority` with the hash, the chosen file index, and priority `low`.
2. Confirm success.
3. Read `torrent://{hash}/files`.
4. Confirm the chosen file now reports low priority, or report the exact observed priority if the engine maps display values differently.
5. Call `set_file_priority` with the same hash, same file index, and priority `normal`.
6. Confirm success.

Pass criteria:

- A valid file index can be updated without errors.
- The final priority is restored to normal for later tests.

## Test Case 9: Timeline Captures Failure Evidence

Purpose: Verify the UI-agent timeline helps an AI diagnose failed assertions.

Steps:

1. Call `ui_agent_clear_timeline`.
2. Load the torrent file fixture and record its hash.
3. Call `ui_agent_assert_torrent` with the hash, `state=DefinitelyNotARealState`, and `minProgressPercent=0`.
4. Confirm the assertion returns an error or a failed assertion result.
5. Call `ui_agent_get_timeline`.
6. Confirm the timeline includes an `assertion_failed` event.
7. Call `take_screenshot` and confirm an image result is returned.

Pass criteria:

- Failed assertions are reported as failures, not silent success.
- Timeline output contains enough detail to identify the failed torrent and observed state.
- Screenshot capture works while the UI window is available.

## Test Case 10: Settings Update Through MCP

Purpose: Verify `update_settings` can update nested settings when destructive tools are enabled by UI-agent mode.

Steps:

1. Call `update_settings` with this JSON:
   `{"queue":{"enableQueueManagement":true,"maxActiveDownloads":1,"maxActiveSeeds":1},"network":{"enableDht":true}}`
2. Confirm the tool reports success.
3. Read `logs://latest` and confirm no settings-save exception was logged.
4. Optionally restart Peerfluence with the same isolated profile and confirm the application still starts.

Pass criteria:

- Nested settings JSON is accepted.
- The application remains healthy after saving settings.
- The test uses an isolated profile so user settings are not modified.

## Test Case 11: Invalid Identifiers And Ambiguous Names

Purpose: Verify the UI-agent tools fail safely when a torrent cannot be identified.

Steps:

1. Call `ui_agent_select_torrent` with `this torrent does not exist`.
2. Confirm the result is an error with code or message indicating the torrent was not found.
3. Call `manage_torrent` with `not-a-hash` and action `pause`.
4. Confirm the result is an error indicating invalid info hash format.
5. If two torrents with similar names are available, call `ui_agent_select_torrent` with their shared partial name.
6. Confirm the result reports ambiguity and asks for an info hash or exact name.

Pass criteria:

- Missing and malformed identifiers fail without side effects.
- Ambiguous partial names do not select an arbitrary torrent.

## Test Case 12: Shutdown Tool In Isolated UI-Agent Profile

Purpose: Verify controlled application shutdown through MCP.

Setup:

- Run this as the final test in a disposable UI-agent session.

Steps:

1. Call `ui_agent_cleanup` with `removeTorrents=true` and `clearSelection=true`.
2. Call `shutdown_application`.
3. Confirm the tool reports that shutdown was requested.
4. Confirm the Peerfluence UI-agent process exits.
5. Confirm the MCP proxy disconnects or stops responding because the application has shut down.

Pass criteria:

- Shutdown is only executed at the end of the test run.
- The isolated profile has been cleaned up before shutdown.
- The application exits gracefully.
