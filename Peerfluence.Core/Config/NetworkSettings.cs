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

    /// <summary>
    /// How fast torrents may download, in bytes per second. Zero is unlimited.
    ///
    /// <para>
    /// Stored in bytes because that is what the engine takes. The setting is shown in kibibytes per
    /// second, which is the unit every other client uses and the one people think in.
    /// </para>
    /// </summary>
    public long MaxDownloadSpeedBytesPerSecond { get; set; } = 0;

    /// <summary>How fast torrents may upload, in bytes per second. Zero is unlimited.</summary>
    public long MaxUploadSpeedBytesPerSecond { get; set; } = 0;

    public long MaxDiskReadSpeedBytesPerSecond { get; set; } = 0;

    public long MaxDiskWriteSpeedBytesPerSecond { get; set; } = 0;
}

