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

    /// <summary>
    /// The port to listen on for TCP and UDP.
    ///
    /// <para>
    /// 6881 rather than the 55125 this defaulted to before. 55125 sits in the dynamic range
    /// (49152-65535), which the OS allocates outbound connections from and which Windows reserves
    /// blocks of for Hyper-V, WSL and Docker - a bind inside a reserved block fails with a permission
    /// error although nothing is listening, and the blocks move between reboots. 6881 is the first of
    /// the range BitTorrent has used since the original client, and the default in libtorrent,
    /// qBittorrent and Deluge.
    /// </para>
    ///
    /// <para>
    /// Only new profiles get this. Anyone who already has 55125 stored keeps it, along with whatever
    /// they forwarded through their router for it.
    /// </para>
    /// </summary>
    public int ListeningPort { get; set; } = DefaultListeningPort;

    /// <summary>
    /// The port a profile that has never chosen one gets. A constant because three places used to
    /// state it - this property, the defaults the settings service builds, and the value it repairs
    /// an out-of-range stored port to - and they are only ever meant to agree.
    /// </summary>
    public const int DefaultListeningPort = 6881;

    /// <summary>
    /// How many connections one address may hold on a single torrent. Zero is unlimited, which is
    /// the default: the count includes connections a single logical peer briefly holds more than one
    /// of, while a dial tries both transports or a reconnect overlaps the connection it replaces, so
    /// any non-zero value has to be chosen for the network it runs on.
    /// </summary>
    public int MaxConnectionsPerIp { get; set; } = 0;

    /// <summary>
    /// A single local address to bind every socket to, or blank to listen on everything.
    ///
    /// <para>
    /// This is what makes a VPN a kill switch rather than a preference. PeerSharp fails socket
    /// creation outright rather than falling back to an unbound socket, so traffic stops when the
    /// tunnel goes away instead of leaving through the default route. Port mapping (UPnP and NAT-PMP)
    /// is turned off while it is set, because those open their own interface-selected sockets and
    /// cannot honour the guarantee.
    /// </para>
    /// </summary>
    public string BindAddress { get; set; } = string.Empty;

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

