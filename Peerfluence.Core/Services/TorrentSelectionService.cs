using Peerfluence.Core.Messaging;
using PeerSharp.Interfaces;

namespace Peerfluence.Core.Services;

public sealed class TorrentSelectionService : ITorrentSelectionService
{
    private readonly IAppMessenger _messenger;
    private ITorrent? _selectedTorrent;

    public TorrentSelectionService(IAppMessenger messenger)
    {
        _messenger = messenger;
    }

    public ITorrent? SelectedTorrent
    {
        get => _selectedTorrent;
        set
        {
            if (!ReferenceEquals(_selectedTorrent, value))
            {
                _selectedTorrent = value;
                _messenger.Publish(new TorrentSelectionChangedMessage(value));
            }
        }
    }
}

