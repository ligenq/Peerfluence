namespace Peerfluence.Core.Config;

public sealed class NetworkSettings
{
    public bool EnableDht { get; set; } = true;

    /// <summary>
    /// Whether this node answers BEP 51 <c>sample_infohashes</c> queries, which let indexers
    /// enumerate the torrents it holds peers for. On by default, matching PeerSharp: the same
    /// hashes are already obtainable by asking us <c>get_peers</c>, and answering makes us a useful
    /// DHT participant rather than a dead end.
    /// </summary>
    public bool AnswerInfoHashSampling { get; set; } = true;

    /// <summary>
    /// Whether several peer connections may share one IP address. On by default, matching PeerSharp:
    /// carrier-grade NAT puts many unrelated subscribers behind a single address, so refusing them
    /// costs real peers, and the abuse the restriction guards against is already bounded by the
    /// overall connection limit.
    /// </summary>
    public bool AllowMultipleConnectionsPerIp { get; set; } = true;

    public bool EnableNatPmp { get; set; } = true;

    public bool EnableUpnp { get; set; } = false;

    public bool UseAutomaticListeningPort { get; set; } = false;

    public int ListeningPort { get; set; } = 55125;

    public long MaxDiskReadSpeedBytesPerSecond { get; set; } = 0;

    public long MaxDiskWriteSpeedBytesPerSecond { get; set; } = 0;
}

