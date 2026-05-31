using PeerSharp.Interfaces;

namespace Peerfluence.Core.Services;

public interface ITorrentSelectionService
{
    ITorrent? SelectedTorrent { get; set; }
}
