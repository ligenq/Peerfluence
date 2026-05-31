using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PeerSharp.Clients;
using PeerSharp.Config;
using PeerSharp.Interfaces;
using Peerfluence.Properties;

namespace Peerfluence.Services;

public sealed class MagnetMetadataPreviewService : IMagnetMetadataPreviewService
{
    private readonly IAppSettingsService _settingsService;

    public MagnetMetadataPreviewService(IAppSettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public async Task<MagnetMetadataPreview?> FetchAsync(
        string magnetUri,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var ct = linkedCts.Token;

        var previewRoot = Path.Combine(Path.GetTempPath(), "Peerfluence", "MagnetMetadataPreview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(previewRoot);

        await using var engine = ClientEngineFactory.Create(CreatePreviewOptions(previewRoot));
        ITorrent? torrent = null;

        try
        {
            await engine.InitializeAsync(ct).ConfigureAwait(false);

            var metadataReceived = new TaskCompletionSource<ITorrent>(TaskCreationOptions.RunContinuationsAsynchronously);
            var events = new TorrentEventsBuilder()
                .OnMetadataReceived(t => metadataReceived.TrySetResult(t))
                .Build();

            torrent = await engine.AddMagnetAsync(
                MagnetLink.Parse(magnetUri),
                new AddTorrentOptions
                {
                    DownloadPath = previewRoot,
                    StartImmediately = true,
                    Events = events
                },
                ct).ConfigureAwait(false);

            if (!torrent.HasMetadata)
            {
                torrent = await metadataReceived.Task.WaitAsync(ct).ConfigureAwait(false);
            }

            if (!torrent.HasMetadata)
            {
                return null;
            }

            return CreatePreview(torrent);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (torrent != null)
            {
                try
                {
                    await engine.RemoveTorrentAsync(torrent, RemoveOptions.DeleteFiles, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // Best effort cleanup of the disposable preview torrent.
                }
            }

            try
            {
                Directory.Delete(previewRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup of the temporary preview folder.
            }
        }
    }

    private TorrentClientOptions CreatePreviewOptions(string previewRoot)
    {
        return new TorrentClientOptions
        {
            Settings = new Settings
            {
                Connection =
                {
                    TcpPort = 0,
                    UdpPort = 0,
                    EnableTcpIn = true,
                    EnableUtpIn = true,
                    UpnpPortMapping = false,
                    NatPmpPortMapping = false
                },
                Dht =
                {
                    Enabled = _settingsService.Current.Network.EnableDht
                },
                Files =
                {
                    DefaultDownloadPath = previewRoot
                },
                Session =
                {
                    Enabled = false
                }
            }
        };
    }

    private static MagnetMetadataPreview CreatePreview(ITorrent torrent)
    {
        var files = torrent
            .GetAllFileInfo()
            .Select(file => new MagnetMetadataPreviewFile(file.Index, file.Path, file.Size))
            .ToList();

        var trackers = torrent
            .Trackers
            .GetTrackers()
            .Select(tracker => tracker.Url)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new MagnetMetadataPreview(
            torrent.Name,
            torrent.Hash.IsEmpty ? torrent.HashV2.ToString() : torrent.Hash.ToString(),
            GetVersionLabel(torrent.Hash, torrent.HashV2),
            torrent.TotalSize,
            torrent.FileCount,
            torrent.PieceCount,
            torrent.PieceSize,
            IsPrivate: false,
            files,
            trackers);
    }

    private static string GetVersionLabel(InfoHash hash, InfoHash hashV2)
    {
        if (!hash.IsEmpty && !hashV2.IsEmpty)
        {
            return "V1 + V2";
        }

        if (!hashV2.IsEmpty)
        {
            return "V2";
        }

        return !hash.IsEmpty ? "V1" : Resources.Common_Unknown;
    }
}
