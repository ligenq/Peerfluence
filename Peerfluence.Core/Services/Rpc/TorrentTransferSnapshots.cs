using System.Collections.Concurrent;
using PeerSharp.Core;

namespace Peerfluence.Core.Services.Rpc;

public sealed class TorrentTransferSnapshots : ITorrentTransferSnapshots
{
    // Concurrent because alerts arrive on the engine's thread and are read on whichever thread is
    // answering a remote request.
    private readonly ConcurrentDictionary<string, TorrentTransferSnapshot> _snapshots = new(StringComparer.OrdinalIgnoreCase);

    public TorrentTransferSnapshot GetSnapshot(InfoHash hash)
    {
        return Key(hash) is { } key && _snapshots.TryGetValue(key, out var snapshot)
            ? snapshot
            : default;
    }

    public void Record(InfoHash hash, TorrentTransferSnapshot snapshot)
    {
        if (Key(hash) is { } key)
        {
            _snapshots[key] = snapshot;
        }
    }

    private static string? Key(InfoHash hash) => hash.IsEmpty ? null : hash.ToHexStringUpper();
}
