using PeerSharp.Interfaces;

namespace Peerfluence.Core.Messaging;

public sealed class TorrentSelectionChangedMessage
{
    public TorrentSelectionChangedMessage(ITorrent? selectedTorrent)
    {
        SelectedTorrent = selectedTorrent;
    }

    public ITorrent? SelectedTorrent { get; }
}
