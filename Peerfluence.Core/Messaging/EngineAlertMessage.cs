using PeerSharp.Core;

namespace Peerfluence.Core.Messaging;

/// <summary>
/// An alert about the engine rather than about one torrent.
/// </summary>
/// <remarks>
/// <see cref="TorrentAlertMessage"/> cannot carry these: it names the torrent an alert belongs to,
/// and these have none. The listener that binds a port is the whole session's, not any one
/// download's.
/// </remarks>
public sealed class EngineAlertMessage
{
    public EngineAlertMessage(Alert alert)
    {
        Alert = alert;
    }

    public Alert Alert { get; }
}
