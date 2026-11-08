using PeerSharp.Interfaces;

namespace Peerfluence.Core.Messaging;

/// <summary>
/// A start that failed after whoever asked for it had stopped waiting, because it spent that time
/// checking the torrent's files.
/// </summary>
public sealed class TorrentStartFailedMessage
{
    public TorrentStartFailedMessage(ITorrent torrent, Exception exception)
    {
        Torrent = torrent;
        Exception = exception;
    }

    public ITorrent Torrent { get; }

    public Exception Exception { get; }
}
