using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Peerfluence.Services;

public interface IMagnetMetadataPreviewService
{
    Task<MagnetMetadataPreview?> FetchAsync(
        string magnetUri,
        TimeSpan timeout,
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
    IReadOnlyList<string> Trackers);

public sealed record MagnetMetadataPreviewFile(
    int Index,
    string Path,
    long SizeBytes);
