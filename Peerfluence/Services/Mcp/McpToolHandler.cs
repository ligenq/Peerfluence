using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using Peerfluence.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Services.Mcp;

public class McpToolHandler : IMcpToolHandler
{
    private const int DefaultMaxTorrentPayloadBytes = 10 * 1024 * 1024;
    private readonly ITorrentService _torrentService;
    private readonly ITopLevelService _topLevelService;
    private readonly IAppSettingsService _appSettingsService;
    private readonly IHostApplicationLifetime _hostLifetime;
    private readonly IMcpRuntimeOptions? _runtimeOptions;

    public McpToolHandler(
        ITorrentService torrentService,
        ITopLevelService topLevelService,
        IAppSettingsService appSettingsService,
        IHostApplicationLifetime hostLifetime,
        IMcpRuntimeOptions? runtimeOptions = null)
    {
        _torrentService = torrentService;
        _topLevelService = topLevelService;
        _appSettingsService = appSettingsService;
        _hostLifetime = hostLifetime;
        _runtimeOptions = runtimeOptions;
    }

    public async Task<CallToolResult> AddTorrentAsync(string magnetOrFile, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(magnetOrFile))
        {
            return ToolError("Input cannot be empty.", "empty_input");
        }

        try
        {
            if (magnetOrFile.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
                // The null is checked as well as the bool: TryParse hands back a nullable without
                // promising the compiler that success means non-null, so this is the only thing
                // standing between a malformed magnet and a null reference further down.
                if (!MagnetLink.TryParse(magnetOrFile, out var magnet) || magnet == null)
                {
                    return ToolError("Invalid magnet link format.", "invalid_magnet");
                }

                if (!TorrentService.HasUsableInfoHash(magnet))
                {
                    return ToolError(TorrentService.MagnetWithoutInfoHashMessage, "magnet_without_info_hash");
                }

                await _torrentService.AddMagnetAsync(magnetOrFile, null, cancellationToken);
                return ToolSuccess("Successfully added torrent from magnet link.");
            }

            if (File.Exists(magnetOrFile))
            {
                if (!string.Equals(Path.GetExtension(magnetOrFile), ".torrent", StringComparison.OrdinalIgnoreCase))
                {
                    return ToolError("Only .torrent files can be imported by path.", "invalid_file_type");
                }

                var fileInfo = new FileInfo(magnetOrFile);
                if (fileInfo.Length > MaxTorrentPayloadBytes)
                {
                    return ToolError($"Torrent file exceeds the {MaxTorrentPayloadBytes} byte limit.", "payload_too_large");
                }

                await _torrentService.AddTorrentFileAsync(magnetOrFile, null, cancellationToken);
                return ToolSuccess("Successfully added torrent from file.");
            }

            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(magnetOrFile);
            }
            catch (FormatException)
            {
                return ToolError("Input is not a valid magnet link, .torrent file path, or base64 .torrent payload.", "invalid_input");
            }

            if (bytes.Length == 0)
            {
                return ToolError("Base64 payload is empty.", "empty_payload");
            }

            if (bytes.Length > MaxTorrentPayloadBytes)
            {
                return ToolError($"Base64 torrent payload exceeds the {MaxTorrentPayloadBytes} byte limit.", "payload_too_large");
            }

            var tempPath = Path.Combine(Path.GetTempPath(), $"peerfluence-{Guid.NewGuid():N}.torrent");
            try
            {
                await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
                await _torrentService.AddTorrentFileAsync(tempPath, null, cancellationToken);
                return ToolSuccess("Successfully added torrent from base64 data.");
            }
            finally
            {
                TryDelete(tempPath);
            }
        }
        catch (OperationCanceledException)
        {
            return ToolError("Add torrent operation was cancelled.", "cancelled");
        }
        catch (Exception ex)
        {
            return ToolError($"Error adding torrent: {ex.Message}", "add_torrent_failed");
        }
    }

    public async Task<CallToolResult> ManageTorrentAsync(string infoHashHex, string action, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!InfoHash.TryFromHex(infoHashHex, out var infoHash))
            {
                return ToolError("Invalid info hash format.", "invalid_info_hash");
            }

            var torrents = _torrentService.GetTorrents();
            var torrent = torrents.FirstOrDefault(t => TorrentIdentity.HasHash(t, infoHash));

            if (torrent == null)
            {
                return ToolError("Torrent not found.", "torrent_not_found");
            }

            switch (action.ToLowerInvariant())
            {
                case "pause":
                    if (torrent.State == TorrentState.Stopped)
                    {
                        return ToolSuccess($"Torrent {infoHashHex} is already paused.");
                    }

                    await TorrentService.StopAsync(torrent, cancellationToken);
                    return ToolSuccess($"Successfully paused torrent {infoHashHex}.");
                case "resume":
                    if (torrent.State == TorrentState.Active)
                    {
                        return ToolSuccess($"Torrent {infoHashHex} is already active.");
                    }

                    await TorrentService.StartAsync(torrent, cancellationToken);
                    return ToolSuccess($"Successfully resumed torrent {infoHashHex}.");
                case "remove":
                    if (!DestructiveToolsAllowed)
                    {
                        return ToolError("Destructive MCP tools are disabled in settings.", "destructive_tools_disabled");
                    }

                    await _torrentService.RemoveAsync(torrent, PeerSharp.Config.RemoveOptions.None, cancellationToken);
                    return ToolSuccess($"Successfully removed torrent {infoHashHex}.");
                default:
                    return ToolError($"Unknown action '{action}'. Valid actions are pause, resume, remove.", "unknown_action");
            }
        }
        catch (OperationCanceledException)
        {
            return ToolError("Manage torrent operation was cancelled.", "cancelled");
        }
        catch (Exception ex)
        {
            return ToolError($"Error managing torrent: {ex.Message}", "manage_torrent_failed");
        }
    }

    public async Task<CallToolResult> TakeScreenshotAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_topLevelService.IsWindowAvailable)
            {
                return ToolError("UI window not available.", "ui_unavailable");
            }

            var bytes = await _topLevelService.CaptureWindowPngAsync(cancellationToken);
            if (bytes == null)
            {
                return ToolError("Invalid window dimensions.", "invalid_window_dimensions");
            }

            return new CallToolResult
            {
                Content = new List<ContentBlock>
                {
                    ImageContentBlock.FromBytes(bytes, "image/png")
                }
            };
        }
        catch (OperationCanceledException)
        {
            return ToolError("Screenshot operation was cancelled.", "cancelled");
        }
        catch (Exception ex)
        {
            return ToolError($"Error taking screenshot: {ex.Message}", "screenshot_failed");
        }
    }

    public Task<CallToolResult> ShutdownApplicationAsync()
    {
        if (!DestructiveToolsAllowed)
        {
            return Task.FromResult(ToolError("Destructive MCP tools are disabled in settings.", "destructive_tools_disabled"));
        }

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Dispatcher.UIThread.Post(() => desktop.Shutdown());
        }

        _hostLifetime.StopApplication();
        return Task.FromResult(ToolSuccess("Application shutdown requested."));
    }

    public async Task<CallToolResult> UpdateSettingsAsync(string settingsJson, CancellationToken cancellationToken = default)
    {
        if (!DestructiveToolsAllowed)
        {
            return ToolError("Destructive MCP tools are disabled in settings.", "destructive_tools_disabled");
        }

        try
        {
            var current = _appSettingsService.Current;
            using var doc = JsonDocument.Parse(settingsJson);

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                ApplySetting(current, prop.Name, prop.Value);
            }

            await _appSettingsService.SaveAsync(cancellationToken);
            return ToolSuccess("Settings updated successfully.");
        }
        catch (OperationCanceledException)
        {
            return ToolError("Update settings operation was cancelled.", "cancelled");
        }
        catch (Exception ex)
        {
            return ToolError($"Error updating settings: {ex.Message}", "update_settings_failed");
        }
    }

    private static void ApplySetting(AppSettings current, string propertyName, JsonElement value)
    {
        switch (propertyName.ToLowerInvariant())
        {
            case "storage":
                ApplyStorageSettings(current, value);
                return;
            case "network":
                ApplyNetworkSettings(current, value);
                return;
            case "theme":
                ApplyThemeSettings(current, value);
                return;
            case "queue":
                ApplyQueueSettings(current, value);
                return;
            case "proxy":
                ApplyProxySettings(current, value);
                return;
            case "update":
                ApplyUpdateSettings(current, value);
                return;
            case "mcp":
                ApplyMcpSettings(current, value);
                return;
        }

        switch (propertyName.ToLowerInvariant())
        {
            case "downloadpath": current.Storage.DownloadPath = value.GetString() ?? current.Storage.DownloadPath; break;
            case "sessionpath": current.Storage.SessionPath = value.GetString() ?? current.Storage.SessionPath; break;
            case "enablesessionpersistence": current.Storage.EnableSessionPersistence = value.GetBoolean(); break;
            case "enabledht": current.Network.EnableDht = value.GetBoolean(); break;
            case "answerinfohashsampling": current.Network.AnswerInfoHashSampling = value.GetBoolean(); break;
            case "allowmultipleconnectionsperip": current.Network.AllowMultipleConnectionsPerIp = value.GetBoolean(); break;
            case "maxconnectionsperip": current.Network.MaxConnectionsPerIp = value.GetInt32(); break;
            case "bindaddress":
                var bindAddress = value.GetString();
                if (bindAddress is null)
                {
                    break;
                }

                bindAddress = bindAddress.Trim();
                if (bindAddress.Length > 0
                    && (!System.Net.IPAddress.TryParse(bindAddress, out var parsed)
                        || parsed.Equals(System.Net.IPAddress.Any)
                        || parsed.Equals(System.Net.IPAddress.IPv6Any)))
                {
                    throw new ArgumentException(
                        "Bind address must be a specific IPv4 or IPv6 address, or blank to use every interface.",
                        nameof(value));
                }

                current.Network.BindAddress = bindAddress;
                break;
            case "enablenatpmp": current.Network.EnableNatPmp = value.GetBoolean(); break;
            case "enableupnp": current.Network.EnableUpnp = value.GetBoolean(); break;
            case "useautomaticlisteningport": current.Network.UseAutomaticListeningPort = value.GetBoolean(); break;
            case "listeningport": current.Network.ListeningPort = value.GetInt32(); break;
            case "maxdiskreadspeedbytespersecond": current.Network.MaxDiskReadSpeedBytesPerSecond = value.GetInt64(); break;
            case "maxdiskwritespeedbytespersecond": current.Network.MaxDiskWriteSpeedBytesPerSecond = value.GetInt64(); break;
            case "themevariant": current.Theme.ThemeVariant = value.GetString() ?? current.Theme.ThemeVariant; break;
            case "colortheme": current.Theme.ColorTheme = value.GetString() ?? current.Theme.ColorTheme; break;
            case "backgroundstyle": current.Theme.BackgroundStyle = value.GetString() ?? current.Theme.BackgroundStyle; break;
            case "language": current.Language = value.GetString() ?? current.Language; break;
            case "showremovetorrentoptions": current.ShowRemoveTorrentOptions = value.GetBoolean(); break;
            case "defaultremovetorrentaction": current.DefaultRemoveTorrentAction = value.GetString() ?? current.DefaultRemoveTorrentAction; break;
            case "encryptionmode": current.EncryptionMode = value.GetString() ?? current.EncryptionMode; break;
            case "enableblocklist": current.EnableBlocklist = value.GetBoolean(); break;
            case "blocklistpath": current.BlocklistPath = value.GetString() ?? current.BlocklistPath; break;
            case "enablegeoip": current.EnableGeoIp = value.GetBoolean(); break;
            case "geoippath": current.GeoIpPath = value.GetString() ?? current.GeoIpPath; break;
            case "mediaplayerpath": current.MediaPlayerPath = value.GetString() ?? current.MediaPlayerPath; break;
            case "enablequeuemanagement": current.Queue.EnableQueueManagement = value.GetBoolean(); break;
            case "maxactivedownloads": current.Queue.MaxActiveDownloads = value.GetInt32(); break;
            case "maxactiveseeds": current.Queue.MaxActiveSeeds = value.GetInt32(); break;
            case "proxytype": current.Proxy.ProxyType = value.GetString() ?? current.Proxy.ProxyType; break;
            case "proxyhost": current.Proxy.ProxyHost = value.GetString() ?? current.Proxy.ProxyHost; break;
            case "proxyport": current.Proxy.ProxyPort = value.GetInt32(); break;
            case "proxyusername": current.Proxy.ProxyUsername = value.GetString() ?? current.Proxy.ProxyUsername; break;
            case "proxypassword": current.Proxy.ProxyPassword = value.GetString() ?? current.Proxy.ProxyPassword; break;
            case "proxypeers": current.Proxy.ProxyPeers = value.GetBoolean(); break;
            case "proxytrackers": current.Proxy.ProxyTrackers = value.GetBoolean(); break;
            case "updateurl": current.Update.UpdateUrl = value.GetString() ?? current.Update.UpdateUrl; break;
            case "checkforupdatesonstartup": current.Update.CheckForUpdatesOnStartup = value.GetBoolean(); break;
            case "mcpenabled": current.Mcp.Enabled = value.GetBoolean(); break;
            case "mcpallowdestructivetools": current.Mcp.AllowDestructiveTools = value.GetBoolean(); break;
            case "mcpmaxtorrentpayloadbytes": current.Mcp.MaxTorrentPayloadBytes = Math.Max(1, value.GetInt32()); break;
        }
    }

    private static void ApplyStorageSettings(AppSettings current, JsonElement value) => ApplyNested(current, value);

    private static void ApplyNetworkSettings(AppSettings current, JsonElement value) => ApplyNested(current, value);

    private static void ApplyThemeSettings(AppSettings current, JsonElement value) => ApplyNested(current, value);

    private static void ApplyQueueSettings(AppSettings current, JsonElement value) => ApplyNested(current, value);

    private static void ApplyProxySettings(AppSettings current, JsonElement value) => ApplyNested(current, value);

    private static void ApplyUpdateSettings(AppSettings current, JsonElement value) => ApplyNested(current, value);

    private static void ApplyMcpSettings(AppSettings current, JsonElement value) => ApplyNested(current, value);

    private static void ApplyNested(AppSettings current, JsonElement value)
    {
        foreach (var prop in value.EnumerateObject())
        {
            ApplySetting(current, prop.Name, prop.Value);
        }
    }

    public async Task<CallToolResult> InvokeUiActionAsync(string actionName, CancellationToken cancellationToken = default)
    {
        try
        {
            switch (actionName.ToLowerInvariant())
            {
                // The engine's own pause, not a loop over every torrent. The loop's resume_all
                // started everything, including torrents the user had deliberately stopped, because
                // it had no way of knowing which were running when it paused. This remembers.
                case "pause_all":
                    await _torrentService.PauseSessionAsync(cancellationToken);
                    return ToolSuccess("Paused every running torrent.");
                case "resume_all":
                    await _torrentService.ResumeSessionAsync(cancellationToken);
                    return ToolSuccess("Resumed the torrents that were running when the session was paused.");
                default:
                    return ToolError($"Unknown UI action: {actionName}. Supported actions: pause_all, resume_all.", "unknown_action");
            }
        }
        catch (OperationCanceledException)
        {
            return ToolError("UI action operation was cancelled.", "cancelled");
        }
        catch (Exception ex)
        {
            return ToolError($"Error invoking UI action: {ex.Message}", "ui_action_failed");
        }
    }

    /// <summary>
    /// Finds a torrent by info hash, or says why it could not.
    /// </summary>
    private bool TryResolveTorrent(string infoHashHex, out ITorrent torrent, out CallToolResult? error)
    {
        torrent = null!;

        if (!InfoHash.TryFromHex(infoHashHex, out var infoHash))
        {
            error = ToolError("Invalid info hash format.", "invalid_info_hash");
            return false;
        }

        var found = _torrentService.GetTorrents().FirstOrDefault(t => TorrentIdentity.HasHash(t, infoHash));
        if (found == null)
        {
            error = ToolError("Torrent not found.", "torrent_not_found");
            return false;
        }

        torrent = found;
        error = null;
        return true;
    }

    public Task<CallToolResult> ConfigureTorrentAsync(
        string infoHashHex,
        bool? superSeeding = null,
        int? maxConnections = null,
        int? maxUploadSlots = null)
    {
        try
        {
            if (!TryResolveTorrent(infoHashHex, out var torrent, out var error))
            {
                return Task.FromResult(error!);
            }

            if (maxConnections is < 0 || maxUploadSlots is < 0)
            {
                return Task.FromResult(ToolError("Connection and upload slot limits cannot be negative.", "invalid_limit"));
            }

            var changed = new List<string>();

            if (superSeeding is { } seed)
            {
                torrent.SuperSeeding = seed;
                changed.Add($"superSeeding={seed}");
            }

            if (maxConnections is { } connections)
            {
                torrent.MaxConnections = connections;
                changed.Add($"maxConnections={connections}");
            }

            if (maxUploadSlots is { } slots)
            {
                torrent.MaxUploadSlots = slots;
                changed.Add($"maxUploadSlots={slots}");
            }

            return Task.FromResult(changed.Count == 0
                ? ToolSuccess("Nothing to change: no values were supplied.")
                : ToolSuccess($"Updated {infoHashHex}: {string.Join(", ", changed)}."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolError($"Error configuring torrent: {ex.Message}", "configure_torrent_failed"));
        }
    }

    public Task<CallToolResult> ManageWebSeedsAsync(string infoHashHex, string action, string? url = null)
    {
        try
        {
            if (!TryResolveTorrent(infoHashHex, out var torrent, out var error))
            {
                return Task.FromResult(error!);
            }

            switch (action.ToLowerInvariant())
            {
                case "list":
                    break;

                case "add":
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        return Task.FromResult(ToolError("A url is required to add a web seed.", "missing_url"));
                    }

                    if (!torrent.WebSeeds.Add(url))
                    {
                        return Task.FromResult(ToolError(
                            "The url is blank, malformed, not http/https/ftp, or already in use by this torrent.",
                            "web_seed_rejected"));
                    }

                    break;

                case "remove":
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        return Task.FromResult(ToolError("A url is required to remove a web seed.", "missing_url"));
                    }

                    if (!torrent.WebSeeds.Remove(url))
                    {
                        return Task.FromResult(ToolError("That url was not in use by this torrent.", "web_seed_not_found"));
                    }

                    break;

                default:
                    return Task.FromResult(ToolError(
                        $"Unknown action '{action}'. Valid actions are list, add, remove.",
                        "unknown_action"));
            }

            var response = new McpConstants.WebSeedsResponse(infoHashHex, torrent.WebSeeds.GetAll().ToList());
            return Task.FromResult(ToolSuccess(
                JsonSerializer.Serialize(response, McpJsonContext.Default.WebSeedsResponse)));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolError($"Error managing web seeds: {ex.Message}", "manage_web_seeds_failed"));
        }
    }

    public async Task<CallToolResult> ScrapeTrackersAsync(string infoHashHex, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!TryResolveTorrent(infoHashHex, out var torrent, out var error))
            {
                return error!;
            }

            // Waits for the replies, unlike an announce: the counts are the only reason to ask. A
            // tracker that refuses or does not implement scrape is recorded in its own status rather
            // than failing the call, so the response can still carry the ones that answered.
            await torrent.Trackers.ScrapeAsync(cancellationToken: cancellationToken);

            var trackers = torrent.Trackers
                .GetTrackers()
                .Select(tracker => new McpConstants.TrackerScrapeEntry(
                    tracker.Url,
                    tracker.Status.ToString(),
                    (int)tracker.SeedCount,
                    (int)tracker.LeechCount,
                    tracker.LastError))
                .ToList();

            var response = new McpConstants.TrackerScrapeResponse(infoHashHex, trackers);
            return ToolSuccess(JsonSerializer.Serialize(response, McpJsonContext.Default.TrackerScrapeResponse));
        }
        catch (OperationCanceledException)
        {
            return ToolError("Scrape was cancelled.", "cancelled");
        }
        catch (Exception ex)
        {
            return ToolError($"Error scraping trackers: {ex.Message}", "scrape_failed");
        }
    }

    public async Task<CallToolResult> RenameTorrentFileAsync(
        string infoHashHex,
        int fileIndex,
        string newPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!DestructiveToolsAllowed)
            {
                return ToolError("Destructive MCP tools are disabled in settings.", "destructive_tools_disabled");
            }

            if (!TryResolveTorrent(infoHashHex, out var torrent, out var error))
            {
                return error!;
            }

            if (string.IsNullOrWhiteSpace(newPath))
            {
                return ToolError("A new path is required.", "missing_path");
            }

            if (torrent.State != TorrentState.Stopped)
            {
                return ToolError("The torrent must be stopped before a file can be renamed.", "torrent_running");
            }

            await torrent.RenameFileAsync(fileIndex, newPath, cancellationToken);
            return ToolSuccess($"Renamed file {fileIndex} of {infoHashHex} to '{newPath}'.");
        }
        catch (ArgumentException ex)
        {
            // An absolute path, or one climbing out of the download directory with "..".
            return ToolError($"Invalid path: {ex.Message}", "invalid_path");
        }
        catch (OperationCanceledException)
        {
            return ToolError("Rename was cancelled.", "cancelled");
        }
        catch (Exception ex)
        {
            return ToolError($"Error renaming file: {ex.Message}", "rename_failed");
        }
    }

    public async Task<CallToolResult> MoveTorrentStorageAsync(
        string infoHashHex,
        string path,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!DestructiveToolsAllowed)
            {
                return ToolError("Destructive MCP tools are disabled in settings.", "destructive_tools_disabled");
            }

            if (!TryResolveTorrent(infoHashHex, out var torrent, out var error))
            {
                return error!;
            }

            if (string.IsNullOrWhiteSpace(path))
            {
                return ToolError("A destination path is required.", "missing_path");
            }

            if (torrent.State != TorrentState.Stopped)
            {
                return ToolError("The torrent must be stopped before its data can be moved.", "torrent_running");
            }

            // Moves the files and continues from the new path. A move within a volume is a rename;
            // one that crosses volumes copies, and takes as long as the data is large. If it fails
            // partway what was already moved is put back, so the torrent is never split in two.
            await torrent.MoveStorageAsync(path, cancellationToken);
            return ToolSuccess($"Moved the data for {infoHashHex} to '{path}'.");
        }
        catch (OperationCanceledException)
        {
            return ToolError("Move was cancelled.", "cancelled");
        }
        catch (Exception ex)
        {
            return ToolError($"Error moving torrent storage: {ex.Message}", "move_storage_failed");
        }
    }

    public Task<CallToolResult> GetTorrentDiagnosticsAsync(string infoHashHex)
    {
        try
        {
            if (!InfoHash.TryFromHex(infoHashHex, out var infoHash))
            {
                return Task.FromResult(ToolError("Invalid info hash format.", "invalid_info_hash"));
            }

            var torrents = _torrentService.GetTorrents();
            var torrent = torrents.FirstOrDefault(t => TorrentIdentity.HasHash(t, infoHash));

            if (torrent == null)
            {
                return Task.FromResult(ToolError("Torrent not found.", "torrent_not_found"));
            }

            var trackers = torrent.Trackers.GetTrackers().Select(t => new McpConstants.TrackerDiagnostic(
                Url: t.Url,
                Status: t.Status.ToString(),
                ConsecutiveFailures: t.ConsecutiveFailures,
                LastError: t.LastError,
                NextAnnounce: t.NextAnnounce
            )).ToList();

            var peers = torrent.Peers.GetConnectedPeers().Select(p => new McpConstants.PeerDiagnostic(
                EndPoint: p.EndPoint.ToString(),
                ClientName: p.ClientName,
                IsUtp: p.IsUtp,
                IsEncrypted: p.IsEncrypted,
                DownloadSpeed: p.DownloadSpeed,
                UploadSpeed: p.UploadSpeed,
                Flags: new McpConstants.PeerFlags(p.AmChoking, p.AmInterested, p.PeerChoking, p.PeerInterested),
                Progress: p.Progress
            )).ToList();

            var pieceAvailability = torrent.Peers.GetPieceAvailability();
            var missingPieces = pieceAvailability.Count(a => a == 0);

            var diagnostics = new McpConstants.TorrentDiagnosticsResponse(
                Name: torrent.Name,
                Hash: torrent.Hash.ToHexString(),
                State: torrent.State.ToString(),
                Exception: torrent.LastException?.ToString(),
                PieceCount: torrent.PieceCount,
                MissingPieces: missingPieces,
                Trackers: trackers,
                Peers: peers
            );

            var json = JsonSerializer.Serialize(diagnostics, McpJsonContext.Default.TorrentDiagnosticsResponse);
            return Task.FromResult(ToolText(json));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ToolError($"Error getting diagnostics: {ex.Message}", "diagnostics_failed"));
        }
    }

    public async Task<CallToolResult> SetFilePriorityAsync(string infoHashHex, int fileIndex, string priorityStr, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!InfoHash.TryFromHex(infoHashHex, out var infoHash))
            {
                return ToolError("Invalid info hash format.", "invalid_info_hash");
            }

            var torrent = _torrentService.GetTorrents().FirstOrDefault(t => TorrentIdentity.HasHash(t, infoHash));
            if (torrent == null)
            {
                return ToolError("Torrent not found.", "torrent_not_found");
            }

            if (fileIndex < 0 || fileIndex >= torrent.FileCount)
            {
                return ToolError($"Invalid file index {fileIndex}. Torrent has {torrent.FileCount} files.", "invalid_file_index");
            }

            Priority priority = priorityStr.ToLowerInvariant() switch
            {
                "null" => Priority.DoNotDownload,
                "skip" => Priority.DoNotDownload,
                "low" => Priority.Low,
                "normal" => Priority.Normal,
                "high" => Priority.High,
                _ => Priority.Normal
            };

            await torrent.SetFilePriorityAsync(fileIndex, priority, cancellationToken);
            return ToolSuccess($"Successfully set priority of file {fileIndex} to {priority}.");
        }
        catch (OperationCanceledException)
        {
            return ToolError("Set file priority operation was cancelled.", "cancelled");
        }
        catch (Exception ex)
        {
            return ToolError($"Error setting file priority: {ex.Message}", "set_file_priority_failed");
        }
    }

    private int MaxTorrentPayloadBytes => _appSettingsService?.Current?.Mcp?.MaxTorrentPayloadBytes > 0
        ? _appSettingsService.Current.Mcp.MaxTorrentPayloadBytes
        : DefaultMaxTorrentPayloadBytes;

    private bool DestructiveToolsAllowed =>
        _runtimeOptions?.ForceAllowDestructiveTools == true
        || _appSettingsService?.Current?.Mcp?.AllowDestructiveTools == true;

    private static CallToolResult ToolSuccess(string message) => McpResultFactory.Success(message);

    private static CallToolResult ToolError(string message, string code) => McpResultFactory.Error(message, code);

    private static CallToolResult ToolText(string text, bool isError = false) => McpResultFactory.Text(text, isError);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
