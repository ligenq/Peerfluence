using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Core.Messaging;

public sealed class TorrentAlertMessage
{
    public TorrentAlertMessage(ITorrent torrent, Alert alert)
    {
        Torrent = torrent;
        Alert = alert;
    }

    public ITorrent Torrent { get; }

    public Alert Alert { get; }
}
