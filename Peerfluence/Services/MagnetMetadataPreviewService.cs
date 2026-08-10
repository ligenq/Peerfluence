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

    public MagnetMetadataPreviewService(ITorrentEngineService engineService)
    {
        _engineService = engineService;
    }

    public async Task<MagnetMetadataPreview?> FetchAsync(
        string magnetUri,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var magnet = MagnetLink.Parse(magnetUri);

        // The engine still fetches metadata by adding a torrent, but since PeerSharp 3.1 that
        // torrent is transient in the engine's own sense: it emits no alerts, takes no session
        // entry, joins no queue and claims no info hash. Nothing downstream of the alert queue can
        // see it, so nothing here has to hide it.
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
