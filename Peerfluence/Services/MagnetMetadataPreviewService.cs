using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Peerfluence.Core.Services;
using Peerfluence.Properties;
using PeerSharp.Core;

namespace Peerfluence.Services;

public sealed class MagnetMetadataPreviewService : IMagnetMetadataPreviewService
{
    private readonly ITorrentEngineService _engineService;
    private readonly ITransientTorrentTracker _transientTorrentTracker;

    public MagnetMetadataPreviewService(
        ITorrentEngineService engineService,
        ITransientTorrentTracker transientTorrentTracker)
    {
        _engineService = engineService;
        _transientTorrentTracker = transientTorrentTracker;
    }

    public async Task<MagnetMetadataPreview?> FetchAsync(
        string magnetUri,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var magnet = MagnetLink.Parse(magnetUri);

        // Entered before the fetch, because the engine adds a real torrent to do it and raises
        // TorrentAdded from inside the add.
        using var scope = _transientTorrentTracker.Track(
            magnet.InfoHash.IsEmpty ? magnet.InfoHashV2 : magnet.InfoHash);

        try
        {
            var torrentFile = await _engineService.Engine.GetMagnetMetadataAsync(magnet, linkedCts.Token)
                .ConfigureAwait(false);
            return CreatePreview(torrentFile);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    private static MagnetMetadataPreview CreatePreview(TorrentFile torrentFile)
    {
        var files = torrentFile
            .GetFiles()
            .Select(file => new MagnetMetadataPreviewFile(file.Index, file.Path, file.Size))
            .ToList();

        var trackers = torrentFile
            .Trackers
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MagnetMetadataPreview(
            torrentFile.Name,
            torrentFile.InfoHash.IsEmpty ? torrentFile.InfoHashV2.ToString() : torrentFile.InfoHash.ToString(),
            GetVersionLabel(torrentFile.IsV1, torrentFile.IsV2, torrentFile.IsHybrid),
            torrentFile.TotalSize,
            torrentFile.FileCount,
            torrentFile.PieceCount,
            torrentFile.PieceSize,
            torrentFile.IsPrivate,
            files,
            trackers,
            torrentFile);
    }

    private static string GetVersionLabel(bool isV1, bool isV2, bool isHybrid)
    {
        if (isHybrid)
        {
            return "V1 + V2";
        }

        if (isV2)
        {
            return "V2";
        }

        return isV1 ? "V1" : Resources.Common_Unknown;
    }
}
