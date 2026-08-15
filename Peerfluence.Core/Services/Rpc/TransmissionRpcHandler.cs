using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Peerfluence.Core.Config;
using PeerSharp.Config;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Core.Services.Rpc;

/// <summary>
/// Answers Transmission RPC, which is the protocol the tools built around torrent clients already
/// speak: Sonarr, Radarr and the rest drive a client over this, and implementing it is what makes
/// Peerfluence a thing they can be pointed at rather than a thing they cannot use.
///
/// <para>
/// JSON is read with <see cref="JsonDocument"/> and written with <see cref="Utf8JsonWriter"/> rather
/// than through serializable types, for two reasons. The shape is genuinely dynamic - torrent-get
/// returns exactly the fields the caller asked for and nothing else - and this application publishes
/// ahead-of-time compiled, where reflection-based serialization is trimmed away. Writing the bytes
/// directly is both the simpler fit and the one that survives the build.
/// </para>
/// </summary>
public sealed class TransmissionRpcHandler : ITransmissionRpcHandler
{
    /// <summary>
    /// The protocol version claimed. Clients gate features on this; 15 is old enough to promise only
    /// what is implemented here and new enough that nothing refuses to talk to it.
    /// </summary>
    private const int RpcVersion = 15;

    private const int RpcVersionMinimum = 14;

    private readonly ITorrentService _torrentService;
    private readonly IAppSettingsService _settingsService;
    private readonly ITorrentTransferSnapshots _snapshots;
    private readonly ITorrentCategoryService _categoryService;
    private readonly string _version;

    /// <summary>
    /// Transmission identifies torrents by a small integer as well as by hash, and clients remember
    /// the integer between calls. Info hashes are the identity here, so the numbers are handed out on
    /// first sight and kept for as long as the process lives.
    /// </summary>
    private readonly Dictionary<string, int> _ids = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _idLock = new();
    private int _nextId = 1;

    public TransmissionRpcHandler(
        ITorrentService torrentService,
        IAppSettingsService settingsService,
        ITorrentTransferSnapshots snapshots,
        ITorrentCategoryService categoryService,
        string version)
    {
        _torrentService = torrentService;
        _settingsService = settingsService;
        _snapshots = snapshots;
        _categoryService = categoryService;
        _version = version;
    }

    public async Task<string> HandleAsync(string requestJson, CancellationToken cancellationToken = default)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(requestJson);
        }
        catch (JsonException)
        {
            return Failure("invalid json", tag: null);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Failure("invalid json", tag: null);
            }

            var tag = root.TryGetProperty("tag", out var tagElement) && tagElement.TryGetInt32(out var parsedTag)
                ? parsedTag
                : (int?)null;

            if (!root.TryGetProperty("method", out var methodElement) || methodElement.ValueKind != JsonValueKind.String)
            {
                return Failure("no method name", tag);
            }

            var arguments = root.TryGetProperty("arguments", out var argumentsElement) &&
                            argumentsElement.ValueKind == JsonValueKind.Object
                ? argumentsElement
                : default;

            try
            {
                return await DispatchAsync(methodElement.GetString()!, arguments, tag, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The caller is a program, not a person: it needs the failure as a result rather than
                // as a dropped connection it has to guess about.
                return Failure(ex.Message, tag);
            }
        }
    }

    private async Task<string> DispatchAsync(string method, JsonElement arguments, int? tag, CancellationToken cancellationToken)
    {
        return method switch
        {
            "session-get" => SessionGet(tag),
            "session-stats" => SessionStats(tag),
            "free-space" => FreeSpace(arguments, tag),
            "torrent-get" => TorrentGet(arguments, tag),
            "torrent-add" => await TorrentAddAsync(arguments, tag, cancellationToken).ConfigureAwait(false),
            "torrent-remove" => await TorrentRemoveAsync(arguments, tag, cancellationToken).ConfigureAwait(false),
            "torrent-start" or "torrent-start-now" => await TorrentStartAsync(arguments, tag, cancellationToken).ConfigureAwait(false),
            "torrent-stop" => await TorrentStopAsync(arguments, tag, cancellationToken).ConfigureAwait(false),
            "torrent-set" => await TorrentSetAsync(arguments, tag, cancellationToken).ConfigureAwait(false),
            "torrent-set-location" => await TorrentSetLocationAsync(arguments, tag, cancellationToken).ConfigureAwait(false),
            "session-set" => Success(tag),
            _ => Failure($"unsupported method: {method}", tag)
        };
    }

    private string SessionGet(int? tag)
    {
        var settings = _settingsService.Current;

        return Write(tag, writer =>
        {
            writer.WriteString("version", _version);
            writer.WriteNumber("rpc-version", RpcVersion);
            writer.WriteNumber("rpc-version-minimum", RpcVersionMinimum);
            writer.WriteString("download-dir", settings.Storage.DownloadPath);
            writer.WriteBoolean("seedRatioLimited", false);
            writer.WriteNumber("seedRatioLimit", 0);
            writer.WriteBoolean("speed-limit-down-enabled", settings.Network.MaxDownloadSpeedBytesPerSecond > 0);
            writer.WriteNumber("speed-limit-down", settings.Network.MaxDownloadSpeedBytesPerSecond / 1024);
            writer.WriteBoolean("speed-limit-up-enabled", settings.Network.MaxUploadSpeedBytesPerSecond > 0);
            writer.WriteNumber("speed-limit-up", settings.Network.MaxUploadSpeedBytesPerSecond / 1024);
        });
    }

    private string SessionStats(int? tag)
    {
        var torrents = _torrentService.GetTorrents();
        long down = 0, up = 0;
        var active = 0;

        foreach (var torrent in torrents)
        {
            var snapshot = _snapshots.Get(torrent.Hash);
            down += snapshot.DownloadSpeed;
            up += snapshot.UploadSpeed;
            if (torrent.Started)
            {
                active++;
            }
        }

        return Write(tag, writer =>
        {
            writer.WriteNumber("torrentCount", torrents.Count);
            writer.WriteNumber("activeTorrentCount", active);
            writer.WriteNumber("pausedTorrentCount", torrents.Count - active);
            writer.WriteNumber("downloadSpeed", down);
            writer.WriteNumber("uploadSpeed", up);
        });
    }

    /// <summary>
    /// Clients check there is room before sending something large. Answering with the real figure
    /// for the path asked about, because answering with a guess would have them send it anyway.
    /// </summary>
    private string FreeSpace(JsonElement arguments, int? tag)
    {
        var path = ReadString(arguments, "path") ?? _settingsService.Current.Storage.DownloadPath;

        long free;
        try
        {
            free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? path).AvailableFreeSpace;
        }
        catch (Exception)
        {
            // An unmapped drive or a path that does not exist yet. Minus one is the protocol's way of
            // saying it could not be determined.
            free = -1;
        }

        return Write(tag, writer =>
        {
            writer.WriteString("path", path);
            writer.WriteNumber("size-bytes", free);
        });
    }

    private string TorrentGet(JsonElement arguments, int? tag)
    {
        var fields = ReadStringArray(arguments, "fields");
        if (fields.Count == 0)
        {
            // The protocol requires fields; answering with everything would be a guess at what the
            // caller wanted and a different shape from what it asked for.
            return Failure("no fields specified", tag);
        }

        var selected = Resolve(arguments);

        return Write(tag, writer =>
        {
            writer.WriteStartArray("torrents");
            foreach (var torrent in selected)
            {
                writer.WriteStartObject();
                foreach (var field in fields)
                {
                    WriteField(writer, torrent, field);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });
    }

    private void WriteField(Utf8JsonWriter writer, ITorrent torrent, string field)
    {
        var snapshot = _snapshots.Get(torrent.Hash);

        switch (field)
        {
            case "id":
                writer.WriteNumber(field, IdFor(torrent.Hash));
                break;
            case "hashString":
                writer.WriteString(field, torrent.Hash.ToHexString());
                break;
            case "name":
                writer.WriteString(field, torrent.Name);
                break;
            case "status":
                writer.WriteNumber(field, StatusOf(torrent));
                break;
            case "totalSize":
            case "sizeWhenDone":
                writer.WriteNumber(field, torrent.TotalSize);
                break;
            case "leftUntilDone":
                writer.WriteNumber(field, torrent.DataLeft);
                break;
            case "percentDone":
                writer.WriteNumber(field, Math.Round(torrent.Progress, 4));
                break;
            case "isFinished":
                writer.WriteBoolean(field, torrent.Finished);
                break;
            case "downloadDir":
                writer.WriteString(field, DownloadDirOf(torrent));
                break;
            case "rateDownload":
                writer.WriteNumber(field, snapshot.DownloadSpeed);
                break;
            case "rateUpload":
                writer.WriteNumber(field, snapshot.UploadSpeed);
                break;
            case "downloadedEver":
                writer.WriteNumber(field, snapshot.Downloaded);
                break;
            case "uploadedEver":
                writer.WriteNumber(field, snapshot.Uploaded);
                break;
            case "peersConnected":
                writer.WriteNumber(field, snapshot.ConnectedPeers);
                break;
            case "eta":
                writer.WriteNumber(field, EtaOf(torrent, snapshot));
                break;
            case "errorString":
                writer.WriteString(field, torrent.LastException?.Message ?? string.Empty);
                break;
            case "error":
                writer.WriteNumber(field, torrent.LastException == null ? 0 : 3);
                break;
            case "fileCount":
                writer.WriteNumber(field, torrent.FileCount);
                break;
            case "addedDate":
                writer.WriteNumber(field, torrent.TimeAdded.ToUnixTimeSeconds());
                break;
            case "isPrivate":
                writer.WriteBoolean(field, false);
                break;
            case "labels":
                writer.WriteStartArray(field);
                if (_categoryService.GetCategory(torrent.Hash) is { } category)
                {
                    // Categories are the nearest thing this application has to Transmission's labels,
                    // and the automation tools use labels to tell their downloads from everyone's.
                    writer.WriteStringValue(category);
                }

                writer.WriteEndArray();
                break;
            case "seedRatioLimit":
                writer.WriteNumber(field, torrent.RatioLimit ?? 0);
                break;
            case "seedRatioMode":
                writer.WriteNumber(field, torrent.RatioLimit.HasValue ? 1 : 0);
                break;
            default:
                // Unknown fields are skipped rather than refused: clients ask for supersets of what
                // any one server implements, and failing the call would break every one of them.
                break;
        }
    }

    /// <summary>
    /// Transmission's status numbers. Clients branch on these, so the mapping matters more than the
    /// names on either side of it.
    /// </summary>
    private static int StatusOf(ITorrent torrent)
    {
        if (torrent.State == TorrentState.CheckingFiles)
        {
            return 2;
        }

        if (!torrent.Started || torrent.State is TorrentState.Stopped or TorrentState.Stopping)
        {
            return 0;
        }

        return torrent.Finished ? 6 : 4;
    }

    private static int EtaOf(ITorrent torrent, TorrentTransferSnapshot snapshot)
    {
        if (torrent.Finished)
        {
            return 0;
        }

        // Minus one is the protocol's "unknown", which is honest while nothing is moving.
        return snapshot.DownloadSpeed > 0
            ? (int)Math.Min(int.MaxValue, torrent.DataLeft / snapshot.DownloadSpeed)
            : -1;
    }

    private string DownloadDirOf(ITorrent torrent)
    {
        // The engine holds a path per torrent including its own folder; clients want the directory
        // the download sits in, which is its parent.
        var path = torrent.Files.DownloadPath;
        return string.IsNullOrEmpty(path)
            ? _settingsService.Current.Storage.DownloadPath
            : Path.GetDirectoryName(path) ?? path;
    }

    private async Task<string> TorrentAddAsync(JsonElement arguments, int? tag, CancellationToken cancellationToken)
    {
        var options = new AddTorrentOptions
        {
            StartImmediately = !(ReadBool(arguments, "paused") ?? false)
        };

        if (ReadString(arguments, "download-dir") is { Length: > 0 } directory)
        {
            options.DownloadPath = directory;
        }

        ITorrent added;
        if (ReadString(arguments, "metainfo") is { Length: > 0 } metainfo)
        {
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(metainfo);
            }
            catch (FormatException)
            {
                return Failure("metainfo is not valid base64", tag);
            }

            added = await _torrentService.AddTorrentAsync(TorrentFile.Parse(bytes), options, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (ReadString(arguments, "filename") is { Length: > 0 } filename)
        {
            added = filename.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase)
                ? await _torrentService.AddMagnetAsync(filename, options, cancellationToken).ConfigureAwait(false)
                : filename.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? await _torrentService.AddTorrentFromUrlAsync(filename, options, cancellationToken).ConfigureAwait(false)
                    : await _torrentService.AddTorrentFileAsync(filename, options, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            return Failure("no metainfo or filename", tag);
        }

        // Labels arrive with the add, and are how the automation tools mark what is theirs.
        if (ReadStringArray(arguments, "labels").FirstOrDefault() is { Length: > 0 } label)
        {
            await _categoryService.AssignAsync(added.Hash, label, cancellationToken).ConfigureAwait(false);
        }

        return Write(tag, writer =>
        {
            writer.WriteStartObject("torrent-added");
            writer.WriteNumber("id", IdFor(added.Hash));
            writer.WriteString("name", added.Name);
            writer.WriteString("hashString", added.Hash.ToHexString());
            writer.WriteEndObject();
        });
    }

    private async Task<string> TorrentRemoveAsync(JsonElement arguments, int? tag, CancellationToken cancellationToken)
    {
        var deleteData = ReadBool(arguments, "delete-local-data") ?? false;
        var options = deleteData ? RemoveOptions.DeleteFiles : RemoveOptions.None;

        foreach (var torrent in Resolve(arguments))
        {
            await _torrentService.RemoveAsync(torrent, options, cancellationToken).ConfigureAwait(false);
        }

        return Success(tag);
    }

    private async Task<string> TorrentStartAsync(JsonElement arguments, int? tag, CancellationToken cancellationToken)
    {
        foreach (var torrent in Resolve(arguments))
        {
            await torrent.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        return Success(tag);
    }

    private async Task<string> TorrentStopAsync(JsonElement arguments, int? tag, CancellationToken cancellationToken)
    {
        foreach (var torrent in Resolve(arguments))
        {
            await torrent.StopAsync(cancellationToken).ConfigureAwait(false);
        }

        return Success(tag);
    }

    private async Task<string> TorrentSetAsync(JsonElement arguments, int? tag, CancellationToken cancellationToken)
    {
        var labels = ReadStringArray(arguments, "labels");
        var hasLabels = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("labels", out _);

        foreach (var torrent in Resolve(arguments))
        {
            if (hasLabels)
            {
                // An empty labels array means "no label", which is how a client clears one.
                await _categoryService.AssignAsync(torrent.Hash, labels.FirstOrDefault(), cancellationToken)
                    .ConfigureAwait(false);
            }

            if (ReadLong(arguments, "seedRatioLimit") is { } ratio)
            {
                torrent.RatioLimit = ratio > 0 ? ratio : null;
            }
        }

        return Success(tag);
    }

    /// <summary>
    /// Accepted and reported honestly rather than silently ignored: the engine cannot move a
    /// torrent's data, so a client told this succeeded would believe files are somewhere they are not.
    /// </summary>
    private Task<string> TorrentSetLocationAsync(JsonElement arguments, int? tag, CancellationToken cancellationToken)
    {
        _ = arguments;
        _ = cancellationToken;
        return Task.FromResult(Failure("moving a torrent's location is not supported", tag));
    }

    /// <summary>
    /// Which torrents a call is about. An absent or empty ids argument means all of them, which is
    /// the protocol's rule and the reason torrent-get with no ids is the usual polling call.
    /// </summary>
    private List<ITorrent> Resolve(JsonElement arguments)
    {
        var torrents = _torrentService.GetTorrents();
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty("ids", out var ids))
        {
            return torrents.ToList();
        }

        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wantedIds = new HashSet<int>();

        switch (ids.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var entry in ids.EnumerateArray())
                {
                    Collect(entry);
                }

                break;
            case JsonValueKind.String when ids.GetString() == "recently-active":
                return torrents.ToList();
            default:
                Collect(ids);
                break;
        }

        if (wanted.Count == 0 && wantedIds.Count == 0)
        {
            return torrents.ToList();
        }

        return torrents
            .Where(torrent => wanted.Contains(torrent.Hash.ToHexString()) || wantedIds.Contains(IdFor(torrent.Hash)))
            .ToList();

        void Collect(JsonElement entry)
        {
            if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { Length: > 0 } hash)
            {
                wanted.Add(hash);
            }
            else if (entry.ValueKind == JsonValueKind.Number && entry.TryGetInt32(out var id))
            {
                wantedIds.Add(id);
            }
        }
    }

    private int IdFor(InfoHash hash)
    {
        var key = hash.IsEmpty ? string.Empty : hash.ToHexStringUpper();

        lock (_idLock)
        {
            if (_ids.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var id = _nextId++;
            _ids[key] = id;
            return id;
        }
    }

    private static string? ReadString(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object &&
               arguments.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool? ReadBool(JsonElement arguments, string name)
    {
        if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static float? ReadLong(JsonElement arguments, string name)
    {
        return arguments.ValueKind == JsonValueKind.Object &&
               arguments.TryGetProperty(name, out var value) &&
               value.ValueKind == JsonValueKind.Number &&
               value.TryGetSingle(out var parsed)
            ? parsed
            : null;
    }

    private static List<string> ReadStringArray(JsonElement arguments, string name)
    {
        var values = new List<string>();
        if (arguments.ValueKind != JsonValueKind.Object ||
            !arguments.TryGetProperty(name, out var array) ||
            array.ValueKind != JsonValueKind.Array)
        {
            return values;
        }

        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { } value)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static string Success(int? tag) => Write(tag, _ => { });

    private static string Failure(string message, int? tag)
    {
        return Envelope(message, tag, _ => { });
    }

    private static string Write(int? tag, Action<Utf8JsonWriter> arguments)
    {
        return Envelope("success", tag, arguments);
    }

    private static string Envelope(string result, int? tag, Action<Utf8JsonWriter> arguments)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("result", result);

            writer.WriteStartObject("arguments");
            arguments(writer);
            writer.WriteEndObject();

            if (tag.HasValue)
            {
                writer.WriteNumber("tag", tag.Value);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
