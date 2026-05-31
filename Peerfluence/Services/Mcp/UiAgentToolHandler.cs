using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Peerfluence.Services.Mcp;

public sealed class UiAgentToolHandler : IUiAgentToolHandler
{
    private readonly ITorrentService _torrentService;
    private readonly ITorrentSelectionService _selectionService;
    private readonly ITopLevelService _topLevelService;
    private readonly IUiAgentTimeline _timeline;

    public UiAgentToolHandler(
        ITorrentService torrentService,
        ITorrentSelectionService selectionService,
        ITopLevelService topLevelService,
        IUiAgentTimeline timeline)
    {
        _torrentService = torrentService;
        _selectionService = selectionService;
        _topLevelService = topLevelService;
        _timeline = timeline;
    }

    public Task<ModelContextProtocol.Protocol.CallToolResult> GetUiTestStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var selected = _selectionService.SelectedTorrent;
        var state = new McpConstants.UiAgentStateResponse(
            WindowAvailable: _topLevelService.GetTopLevel() != null,
            SelectedTorrentHash: selected?.Hash.ToHexString(),
            SelectedTorrentName: selected?.Name,
            Torrents: _torrentService.GetTorrents().Select(ToSummary).ToList());

        var json = JsonSerializer.Serialize(state, McpJsonContext.Default.UiAgentStateResponse);
        _timeline.Record("state", $"Captured UI test state with {state.Torrents.Count} torrents.");
        return Task.FromResult(McpResultFactory.Text(json));
    }

    public async Task<ModelContextProtocol.Protocol.CallToolResult> LoadTorrentFileAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return McpResultFactory.Error("Torrent file path cannot be empty.", "empty_path");
        }

        if (!string.Equals(Path.GetExtension(path), ".torrent", StringComparison.OrdinalIgnoreCase))
        {
            return McpResultFactory.Error("Only .torrent files can be loaded by the UI-agent harness.", "invalid_file_type");
        }

        if (!File.Exists(path))
        {
            return McpResultFactory.Error("Torrent file does not exist.", "file_not_found");
        }

        try
        {
            var torrent = await _torrentService.AddTorrentFileAsync(path, null, cancellationToken);
            _selectionService.SelectedTorrent = torrent;
            _timeline.Record("action", $"Loaded torrent file and selected {torrent.Hash.ToHexString()}.");
            return McpResultFactory.Text(JsonSerializer.Serialize(
                ToSummary(torrent),
                McpJsonContext.Default.TorrentSummary));
        }
        catch (OperationCanceledException)
        {
            return McpResultFactory.Error("Load torrent file operation was cancelled.", "cancelled");
        }
        catch (Exception ex)
        {
            return McpResultFactory.Error($"Error loading torrent file: {ex.Message}", "load_torrent_failed");
        }
    }

    public async Task<ModelContextProtocol.Protocol.CallToolResult> StopTorrentAsync(string identifier, CancellationToken cancellationToken = default)
    {
        try
        {
            var torrent = FindTorrent(identifier);
            if (torrent == null)
            {
                return McpResultFactory.Error("Torrent not found.", "torrent_not_found");
            }

            await TorrentService.StopAsync(torrent, cancellationToken);
            _selectionService.SelectedTorrent = torrent;
            _timeline.Record("action", $"Stopped torrent {torrent.Hash.ToHexString()}.");
            return McpResultFactory.Success($"Stopped torrent {torrent.Hash.ToHexString()}.");
        }
        catch (InvalidOperationException ex)
        {
            return McpResultFactory.Error(ex.Message, "ambiguous_torrent");
        }
        catch (OperationCanceledException)
        {
            return McpResultFactory.Error("Stop torrent operation was cancelled.", "cancelled");
        }
        catch (Exception ex)
        {
            return McpResultFactory.Error($"Error stopping torrent: {ex.Message}", "stop_torrent_failed");
        }
    }

    public async Task<ModelContextProtocol.Protocol.CallToolResult> ResumeTorrentAsync(string identifier, CancellationToken cancellationToken = default)
    {
        try
        {
            var torrent = FindTorrent(identifier);
            if (torrent == null)
            {
                return McpResultFactory.Error("Torrent not found.", "torrent_not_found");
            }

            if (torrent.State != PeerSharp.Interfaces.TorrentState.Active)
            {
                await TorrentService.StartAsync(torrent, cancellationToken);
            }

            _selectionService.SelectedTorrent = torrent;
            _timeline.Record("action", $"Resumed torrent {torrent.Hash.ToHexString()}.");
            return McpResultFactory.Success($"Resumed torrent {torrent.Hash.ToHexString()}.");
        }
        catch (InvalidOperationException ex)
        {
            return McpResultFactory.Error(ex.Message, "ambiguous_torrent");
        }
        catch (OperationCanceledException)
        {
            return McpResultFactory.Error("Resume torrent operation was cancelled.", "cancelled");
        }
        catch (Exception ex)
        {
            return McpResultFactory.Error($"Error resuming torrent: {ex.Message}", "resume_torrent_failed");
        }
    }

    public Task<ModelContextProtocol.Protocol.CallToolResult> SelectTorrentAsync(string identifier)
    {
        try
        {
            var torrent = FindTorrent(identifier);
            if (torrent == null)
            {
                return Task.FromResult(McpResultFactory.Error("Torrent not found.", "torrent_not_found"));
            }

            _selectionService.SelectedTorrent = torrent;
            _timeline.Record("action", $"Selected torrent {torrent.Hash.ToHexString()}.");
            return Task.FromResult(McpResultFactory.Success($"Selected torrent {torrent.Hash.ToHexString()}."));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(McpResultFactory.Error(ex.Message, "ambiguous_torrent"));
        }
    }

    public Task<ModelContextProtocol.Protocol.CallToolResult> AssertTorrentAsync(
        string identifier,
        string? state = null,
        double? minProgressPercent = null,
        double? maxProgressPercent = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var torrent = FindTorrent(identifier);
            if (torrent == null)
            {
                return Task.FromResult(AssertionResult(false, "Torrent not found.", null, isError: true));
            }

            var summary = ToSummary(torrent);
            var passed = Matches(summary, state, minProgressPercent, maxProgressPercent);
            var message = passed
                ? "Torrent matched requested assertion."
                : "Torrent did not match requested assertion.";

            _timeline.Record(passed ? "assertion_passed" : "assertion_failed", $"{message} {summary.Hash} state={summary.State} progress={summary.Progress:P1}.");
            return Task.FromResult(AssertionResult(passed, message, summary, isError: !passed));
        }
        catch (InvalidOperationException ex)
        {
            return Task.FromResult(McpResultFactory.Error(ex.Message, "ambiguous_torrent"));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(McpResultFactory.Error("Assert torrent operation was cancelled.", "cancelled"));
        }
    }

    public async Task<ModelContextProtocol.Protocol.CallToolResult> WaitForTorrentAsync(
        string identifier,
        string? state = null,
        double? minProgressPercent = null,
        int timeoutSeconds = 60,
        int pollIntervalMilliseconds = 500,
        CancellationToken cancellationToken = default)
    {
        if (timeoutSeconds <= 0)
        {
            return McpResultFactory.Error("Timeout must be greater than zero seconds.", "invalid_timeout");
        }

        if (pollIntervalMilliseconds < 100)
        {
            return McpResultFactory.Error("Poll interval must be at least 100 milliseconds.", "invalid_poll_interval");
        }

        var deadline = DateTimeOffset.UtcNow.AddSeconds(timeoutSeconds);
        McpConstants.TorrentSummary? last = null;

        try
        {
            while (DateTimeOffset.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var torrent = FindTorrent(identifier);
                if (torrent != null)
                {
                    last = ToSummary(torrent);
                    if (Matches(last, state, minProgressPercent))
                    {
                        _timeline.Record("wait_matched", $"Torrent {last.Hash} matched state={state ?? "*"} minProgress={minProgressPercent?.ToString() ?? "*"}.");
                        return WaitResult(true, "Torrent matched requested condition.", last);
                    }
                }

                await Task.Delay(pollIntervalMilliseconds, cancellationToken);
            }

            _timeline.Record("wait_timeout", $"Timed out waiting for {identifier} state={state ?? "*"} minProgress={minProgressPercent?.ToString() ?? "*"}.");
            return WaitResult(false, "Timed out waiting for torrent condition.", last, isError: true);
        }
        catch (InvalidOperationException ex)
        {
            return McpResultFactory.Error(ex.Message, "ambiguous_torrent");
        }
        catch (OperationCanceledException)
        {
            return McpResultFactory.Error("Wait operation was cancelled.", "cancelled");
        }
    }

    public async Task<ModelContextProtocol.Protocol.CallToolResult> CleanupAsync(
        bool removeTorrents = true,
        bool clearSelection = true,
        CancellationToken cancellationToken = default)
    {
        var removed = 0;

        try
        {
            if (removeTorrents)
            {
                foreach (var torrent in _torrentService.GetTorrents().ToList())
                {
                    await _torrentService.RemoveAsync(torrent, PeerSharp.Config.RemoveOptions.None, cancellationToken);
                    removed++;
                }
            }

            if (clearSelection)
            {
                _selectionService.SelectedTorrent = null;
            }

            var result = new McpConstants.UiAgentCleanupResult(removed, clearSelection);
            _timeline.Record("cleanup", $"Removed {removed} torrents; selectionCleared={clearSelection}.");
            return McpResultFactory.Text(JsonSerializer.Serialize(result, McpJsonContext.Default.UiAgentCleanupResult));
        }
        catch (OperationCanceledException)
        {
            return McpResultFactory.Error("Cleanup operation was cancelled.", "cancelled");
        }
        catch (Exception ex)
        {
            return McpResultFactory.Error($"Error cleaning up UI-agent state: {ex.Message}", "cleanup_failed");
        }
    }

    public Task<ModelContextProtocol.Protocol.CallToolResult> GetTimelineAsync()
    {
        var json = JsonSerializer.Serialize(_timeline.GetEvents().ToList(), McpJsonContext.Default.ListUiAgentTimelineEvent);
        return Task.FromResult(McpResultFactory.Text(json));
    }

    public Task<ModelContextProtocol.Protocol.CallToolResult> ClearTimelineAsync()
    {
        _timeline.Clear();
        return Task.FromResult(McpResultFactory.Success("UI-agent timeline cleared."));
    }

    private PeerSharp.Interfaces.ITorrent? FindTorrent(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return null;
        }

        var torrents = _torrentService.GetTorrents();
        var trimmed = identifier.Trim();

        if (InfoHash.TryFromHex(trimmed, out var hash))
        {
            return torrents.FirstOrDefault(t => t.Hash == hash);
        }

        var exact = torrents.FirstOrDefault(t => string.Equals(t.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
        {
            return exact;
        }

        var partialMatches = torrents
            .Where(t => t.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return partialMatches.Count switch
        {
            0 => null,
            1 => partialMatches[0],
            _ => throw new InvalidOperationException("Torrent identifier matched multiple torrents. Use an info hash or exact name.")
        };
    }

    private static bool Matches(
        McpConstants.TorrentSummary torrent,
        string? state,
        double? minProgressPercent,
        double? maxProgressPercent = null)
    {
        if (!string.IsNullOrWhiteSpace(state)
            && !string.Equals(torrent.State, state, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (minProgressPercent.HasValue && torrent.Progress * 100.0 < minProgressPercent.Value)
        {
            return false;
        }

        if (maxProgressPercent.HasValue && torrent.Progress * 100.0 > maxProgressPercent.Value)
        {
            return false;
        }

        return true;
    }

    private static ModelContextProtocol.Protocol.CallToolResult AssertionResult(
        bool passed,
        string message,
        McpConstants.TorrentSummary? torrent,
        bool isError)
    {
        var json = JsonSerializer.Serialize(
            new McpConstants.UiAgentAssertionResult(passed, message, torrent),
            McpJsonContext.Default.UiAgentAssertionResult);

        return McpResultFactory.Text(json, isError);
    }

    private static ModelContextProtocol.Protocol.CallToolResult WaitResult(
        bool matched,
        string message,
        McpConstants.TorrentSummary? torrent,
        bool isError = false)
    {
        var json = JsonSerializer.Serialize(
            new McpConstants.UiAgentWaitResult(matched, message, torrent),
            McpJsonContext.Default.UiAgentWaitResult);

        return McpResultFactory.Text(json, isError);
    }

    private static McpConstants.TorrentSummary ToSummary(PeerSharp.Interfaces.ITorrent torrent)
    {
        var peers = torrent.Peers.GetConnectedPeers();
        return new McpConstants.TorrentSummary(
            Name: torrent.Name,
            Hash: torrent.Hash.ToHexString(),
            State: torrent.State.ToString(),
            Progress: torrent.Progress,
            DownloadSpeed: peers.Sum(p => p.DownloadSpeed),
            UploadSpeed: peers.Sum(p => p.UploadSpeed),
            Peers: peers.Count,
            Seeds: peers.Count(p => p.Progress >= 1.0f),
            Size: torrent.TotalSize);
    }
}
