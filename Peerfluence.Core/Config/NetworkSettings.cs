namespace Peerfluence.Core.Config;

public sealed class NetworkSettings
{
    /// <summary>The per-direction value meaning "use AppSettings.EncryptionMode".</summary>
    public const string InheritEncryption = "Inherit";

    public const string DefaultI2pSamHost = "127.0.0.1";

    /// <summary>
    /// The modes an encryption policy can name. The one list everything checks against: the
    /// stored overrides, what an MCP client may set, and what the engine is handed.
    /// </summary>
    public static bool IsEncryptionMode(string? mode) => mode is "Allow" or "Require" or "Refuse";

    public static string NormalizeI2pSamHost(string? host) =>
        string.IsNullOrWhiteSpace(host) ? DefaultI2pSamHost : host.Trim();

    public static int ClampI2pSamPort(int port) => Math.Clamp(port, 1, 65535);

    /// <summary>Per-direction overrides, or Inherit to use AppSettings.EncryptionMode.</summary>
    public string IncomingEncryptionMode { get; set; } = InheritEncryption;

    public string OutgoingEncryptionMode { get; set; } = InheritEncryption;

    /// <summary>Keep MSE payloads encrypted by default; false permits negotiated plaintext under Allow.</summary>
    public bool PreferRc4 { get; set; } = true;

    /// <summary>Repairs unknown stored overrides without weakening the shared encryption policy.</summary>
    public bool NormalizeEncryption()
    {
        var before = (IncomingEncryptionMode, OutgoingEncryptionMode);
        IncomingEncryptionMode = NormalizeMode(IncomingEncryptionMode);
        OutgoingEncryptionMode = NormalizeMode(OutgoingEncryptionMode);
        return before != (IncomingEncryptionMode, OutgoingEncryptionMode);
    }

    private static string NormalizeMode(string mode) => IsEncryptionMode(mode) ? mode : InheritEncryption;

    public bool EnableI2p { get; set; }

    public string I2pSamHost { get; set; } = DefaultI2pSamHost;

    public int I2pSamPort { get; set; } = 7656;

    /// <summary>Opt in to public IP peers for I2P torrents. Disabled to preserve network isolation.</summary>
    public bool I2pAllowMixedNetworks { get; set; }

    public bool NormalizeI2p()
    {
        var before = (I2pSamHost, I2pSamPort);
        I2pSamHost = NormalizeI2pSamHost(I2pSamHost);
        I2pSamPort = ClampI2pSamPort(I2pSamPort);
        return before != (I2pSamHost, I2pSamPort);
    }

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

    public bool EnableUpnp { get; set; }

    public bool UseAutomaticListeningPort { get; set; }

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
    public int MaxConnectionsPerIp { get; set; }

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
    public long MaxDownloadSpeedBytesPerSecond { get; set; }

    /// <summary>How fast torrents may upload, in bytes per second. Zero is unlimited.</summary>
    public long MaxUploadSpeedBytesPerSecond { get; set; }

    public long MaxDiskReadSpeedBytesPerSecond { get; set; }

    public long MaxDiskWriteSpeedBytesPerSecond { get; set; }
}
