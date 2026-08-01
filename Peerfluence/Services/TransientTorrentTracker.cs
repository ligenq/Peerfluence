using System;
using System.Collections.Generic;

namespace Peerfluence.Services;

public sealed class TransientTorrentTracker : ITransientTorrentTracker
{
    /// <summary>
    /// How long a completed fetch keeps suppressing after its scope closed, when the alert that
    /// would normally release it never arrives.
    ///
    /// <para>
    /// The release is normally exact: a metadata fetch always ends by removing its transient
    /// torrent, so seeing that <see cref="AlertId.TorrentRemoved"/> means every alert the fetch could
    /// produce has now been drained. The alert queue is polled every 100 ms, so the scope closing is
    /// not by itself proof that the queue is empty - releasing on the scope alone would let the tail
    /// of a short fetch through.
    /// </para>
    ///
    /// <para>
    /// An add that throws before registering the torrent - most often because the user already has
    /// this hash - produces no alerts at all, and so no removal to release on. This bounds how long
    /// that entry can go on shadowing a hash the user may legitimately add a moment later.
    /// </para>
    /// </summary>
    internal static readonly TimeSpan RetentionAfterCompletion = TimeSpan.FromSeconds(5);

    private readonly Dictionary<InfoHash, Entry> _entries = [];
    private readonly object _gate = new();
    private readonly TimeProvider _timeProvider;

    public TransientTorrentTracker()
        : this(TimeProvider.System)
    {
    }

    internal TransientTorrentTracker(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    public IDisposable Track(InfoHash infoHash)
    {
        if (infoHash.IsEmpty)
        {
            return NullScope.Instance;
        }

        lock (_gate)
        {
            PruneExpired();

            if (!_entries.TryGetValue(infoHash, out var entry))
            {
                entry = new Entry();
                _entries[infoHash] = entry;
            }

            entry.Active++;
            entry.RetiredAt = null;
        }

        return new Scope(this, infoHash);
    }

    public bool ShouldSuppress(Alert alert)
    {
        var torrent = alert switch
        {
            TorrentAlert torrentAlert => torrentAlert.Torrent,
            MetadataAlert metadataAlert => metadataAlert.Torrent,
            _ => null
        };

        if (torrent == null)
        {
            return false;
        }

        lock (_gate)
        {
            return IsSuppressed(torrent.Hash, alert.Id) || IsSuppressed(torrent.HashV2, alert.Id);
        }
    }

    private bool IsSuppressed(InfoHash infoHash, AlertId alertId)
    {
        if (infoHash.IsEmpty || !_entries.TryGetValue(infoHash, out var entry))
        {
            return false;
        }

        if (entry.Active == 0)
        {
            if (entry.RetiredAt is { } retiredAt && _timeProvider.GetUtcNow() - retiredAt > RetentionAfterCompletion)
            {
                _entries.Remove(infoHash);
                return false;
            }

            // The fetch is over and its torrent is gone, so nothing further can be queued for
            // this hash. Release it now rather than making the next add wait out the retention.
            if (alertId == AlertId.TorrentRemoved)
            {
                _entries.Remove(infoHash);
            }
        }

        return true;
    }

    /// <summary>
    /// Drops entries whose retention has run out. Reading an alert for the hash releases it first in
    /// the normal case; this is for fetches that produced no alerts at all, which would otherwise sit
    /// in the dictionary for the life of the process.
    /// </summary>
    private void PruneExpired()
    {
        if (_entries.Count == 0)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        foreach (var (infoHash, entry) in _entries)
        {
            if (entry.Active == 0 && entry.RetiredAt is { } retiredAt && now - retiredAt > RetentionAfterCompletion)
            {
                _entries.Remove(infoHash);
            }
        }
    }

    private void Release(InfoHash infoHash)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(infoHash, out var entry))
            {
                return;
            }

            entry.Active--;
            if (entry.Active <= 0)
            {
                entry.Active = 0;
                entry.RetiredAt = _timeProvider.GetUtcNow();
            }
        }
    }

    private sealed class Entry
    {
        public int Active { get; set; }

        public DateTimeOffset? RetiredAt { get; set; }
    }

    private sealed class Scope : IDisposable
    {
        private readonly TransientTorrentTracker _tracker;
        private readonly InfoHash _infoHash;
        private bool _disposed;

        public Scope(TransientTorrentTracker tracker, InfoHash infoHash)
        {
            _tracker = tracker;
            _infoHash = infoHash;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _tracker.Release(_infoHash);
        }
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
