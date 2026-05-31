using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Core.Services;

public sealed class TorrentAlertEventArgs : EventArgs
{
    public TorrentAlertEventArgs(ITorrent torrent, Alert alert)
    {
        Torrent = torrent;
        Alert = alert;
    }

    public ITorrent Torrent { get; }

    public Alert Alert { get; }
}

