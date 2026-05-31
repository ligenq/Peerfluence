using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using Peerfluence.Core.Messaging;

namespace Peerfluence.Services.Mcp;

public sealed class McpResourceHandler : IMcpResourceHandler, IDisposable
{
    private readonly ITorrentEngineService _torrentEngineService;
    private readonly IAppPaths _appPaths;
    private readonly ILogger<McpResourceHandler> _logger;
    private readonly ConcurrentQueue<McpConstants.AlertSummary> _recentAlerts = new();
    private const int MaxAlerts = 100;

    public McpResourceHandler(ITorrentEngineService torrentEngineService, IAppPaths appPaths, ILogger<McpResourceHandler> logger)
    {
        _torrentEngineService = torrentEngineService;
        _appPaths = appPaths;
        _logger = logger;
        WeakReferenceMessenger.Default.Register<TorrentAlertMessage>(this, (_, msg) => OnTorrentAlert(msg));
    }

    private void OnTorrentAlert(TorrentAlertMessage msg)
    {
        _recentAlerts.Enqueue(new McpConstants.AlertSummary(
            Timestamp: DateTimeOffset.Now,
            AlertType: msg.Alert.GetType().Name,
            TorrentHash: msg.Torrent.Hash.ToHexString(),
            TorrentName: msg.Torrent.Name,
            Message: msg.Alert.ToString()
        ));

        while (_recentAlerts.Count > MaxAlerts)
        {
            _recentAlerts.TryDequeue(out _);
        }
    }

    public async Task<string> GetLatestLogsAsync()
    {
        try
        {
            var logDir = _appPaths.AppDataDirectory;
            if (!Directory.Exists(logDir))
            {
                return ResourceError("No logs directory found.", "logs_directory_missing");
            }

            var latestLogFile = new DirectoryInfo(logDir)
                .GetFiles("*.log")
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();

            if (latestLogFile == null)
            {
                return ResourceError("No log files found.", "logs_missing");
            }

            await using var stream = new FileStream(latestLogFile.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);

            var content = await reader.ReadToEndAsync();
            var lines = content.Split(Environment.NewLine);
            var tailLines = lines.Skip(Math.Max(0, lines.Length - 1000)).ToArray();

            return string.Join(Environment.NewLine, tailLines);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read latest logs for MCP.");
            return ResourceError($"Error reading logs: {ex.Message}", "read_logs_failed");
        }
    }

    public Task<string> GetEngineStatsAsync()
    {
        try
        {
            var stats = _torrentEngineService.Engine.GetStats();
            var response = new McpConstants.EngineStatsResponse(
                stats.TotalDownloaded,
                stats.TotalUploaded,
                stats.TotalPeers,
                stats.ActiveTorrents,
                stats.DownloadSpeed,
                stats.UploadSpeed,
                stats.TorrentCount
            );
            var json = JsonSerializer.Serialize(response, McpJsonContext.Default.EngineStatsResponse);
            return Task.FromResult(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get engine stats for MCP.");
            return Task.FromResult(ResourceError($"Error getting stats: {ex.Message}", "engine_stats_failed"));
        }
    }

    public Task<string> GetActiveTorrentsAsync()
    {
        try
        {
            var torrents = _torrentEngineService.Engine.GetTorrents();
            var list = torrents.Select(t =>
            {
                var connectedPeers = t.Peers.GetConnectedPeers();
                return new McpConstants.TorrentSummary(
                    Name: t.Name,
                    Hash: t.Hash.ToHexString(),
                    State: t.State.ToString(),
                    Progress: t.Progress,
                    DownloadSpeed: connectedPeers.Sum(p => p.DownloadSpeed),
                    UploadSpeed: connectedPeers.Sum(p => p.UploadSpeed),
                    Peers: connectedPeers.Count,
                    Seeds: connectedPeers.Count(p => p.Progress >= 1.0f),
                    Size: t.TotalSize
                );
            }).ToList();
            var json = JsonSerializer.Serialize(list, McpJsonContext.Default.ListTorrentSummary);
            return Task.FromResult(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active torrents for MCP.");
            return Task.FromResult(ResourceError($"Error getting active torrents: {ex.Message}", "active_torrents_failed"));
        }
    }

    public Task<string> GetRecentAlertsAsync()
    {
        try
        {
            var alertsArray = _recentAlerts.ToList();
            var json = JsonSerializer.Serialize(alertsArray, McpJsonContext.Default.ListAlertSummary);
            return Task.FromResult(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize recent alerts for MCP.");
            return Task.FromResult(ResourceError($"Error getting recent alerts: {ex.Message}", "recent_alerts_failed"));
        }
    }

    public Task<string> GetTorrentFilesAsync(string infoHash)
    {
        try
        {
            if (!InfoHash.TryFromHex(infoHash, out var parsedInfoHash))
            {
                return Task.FromResult(ResourceError("Invalid info hash format.", "invalid_info_hash"));
            }

            var torrent = _torrentEngineService.Engine.GetTorrents().FirstOrDefault(t => t.Hash == parsedInfoHash);
            if (torrent == null)
            {
                return Task.FromResult(ResourceError("Torrent not found.", "torrent_not_found"));
            }

            var files = torrent.GetAllFileInfo().Zip(torrent.GetAllFileSelections(), (info, selection) => new McpConstants.TorrentFileSummary(
                Index: info.Index,
                Path: info.Path,
                Size: info.Size,
                Priority: selection.Priority.ToString(),
                Progress: info.Progress,
                IsFinished: info.Progress >= 1.0f
            )).ToList();

            var json = JsonSerializer.Serialize(files, McpJsonContext.Default.ListTorrentFileSummary);
            return Task.FromResult(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get torrent files for MCP.");
            return Task.FromResult(ResourceError($"Error getting torrent files: {ex.Message}", "torrent_files_failed"));
        }
    }

    public Task<string> GetTorrentPeersAsync(string infoHash)
    {
        try
        {
            if (!InfoHash.TryFromHex(infoHash, out var parsedInfoHash))
            {
                return Task.FromResult(ResourceError("Invalid info hash format.", "invalid_info_hash"));
            }

            var torrent = _torrentEngineService.Engine.GetTorrents().FirstOrDefault(t => t.Hash == parsedInfoHash);
            if (torrent == null)
            {
                return Task.FromResult(ResourceError("Torrent not found.", "torrent_not_found"));
            }

            var peers = torrent.Peers.GetConnectedPeers().Select(p => new McpConstants.TorrentPeerSummary(
                EndPoint: p.EndPoint.ToString(),
                ClientName: p.ClientName,
                Country: p.Country,
                DownloadSpeed: p.DownloadSpeed,
                UploadSpeed: p.UploadSpeed,
                Progress: p.Progress,
                IsUtp: p.IsUtp,
                IsEncrypted: p.IsEncrypted,
                Flags: new McpConstants.PeerFlags(p.AmChoking, p.AmInterested, p.PeerChoking, p.PeerInterested)
            )).ToList();

            var json = JsonSerializer.Serialize(peers, McpJsonContext.Default.ListTorrentPeerSummary);
            return Task.FromResult(json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get torrent peers for MCP.");
            return Task.FromResult(ResourceError($"Error getting torrent peers: {ex.Message}", "torrent_peers_failed"));
        }
    }

    public void Dispose()
    {
        WeakReferenceMessenger.Default.Unregister<TorrentAlertMessage>(this);
    }

    private static string ResourceError(string message, string code)
    {
        return JsonSerializer.Serialize(
            new McpConstants.McpOperationResult(false, message, code),
            McpJsonContext.Default.McpOperationResult);
    }
}
