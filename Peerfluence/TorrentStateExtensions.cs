using PeerSharp.Interfaces;

namespace Peerfluence;

public static class TorrentStateExtensions
{
    public static string ToDisplayString(this TorrentState state) => state switch
    {
        TorrentState.Active => Properties.Resources.TorrentState_Active,
        TorrentState.Stopping => Properties.Resources.TorrentState_Stopping,
        TorrentState.Stopped => Properties.Resources.TorrentState_Stopped,
        TorrentState.CheckingFiles => Properties.Resources.TorrentState_CheckingFiles,
        TorrentState.DownloadingMetadata => Properties.Resources.TorrentState_DownloadingMetadata,
        _ => state.ToString(),
    };
}
