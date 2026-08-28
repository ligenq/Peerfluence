using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PeerSharp.Core;

namespace Peerfluence.Services;

public interface IMagnetMetadataPreviewService
{
    /// <summary>
    /// Fetches a magnet's metadata without adding it to the engine.
    /// </summary>
    /// <param name="magnetUri">The magnet to resolve.</param>
    /// <param name="timeout">How long to wait before giving up.</param>
    /// <param name="progress">
    /// Receives how much of the metadata has arrived, from 0 to 1. Optional: the fetch used to be
    /// unobservable, so a caller that only wants the result passes nothing and gets what it always
    /// got.
    /// </param>
    /// <param name="cancellationToken">Cancels the fetch.</param>
    Task<MagnetMetadataPreview?> FetchAsync(
        string magnetUri,
        TimeSpan timeout,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default);
}

public sealed record MagnetMetadataPreview(
    string Name,
    string Hash,
    string VersionLabel,
    long TotalSizeBytes,
    int FileCount,
    int PieceCount,
    long PieceSizeBytes,
    bool IsPrivate,
    IReadOnlyList<MagnetMetadataPreviewFile> Files,
    IReadOnlyList<string> Trackers,
    TorrentFile? TorrentFile = null);

public sealed record MagnetMetadataPreviewFile(
    int Index,
    string Path,
    long SizeBytes);
