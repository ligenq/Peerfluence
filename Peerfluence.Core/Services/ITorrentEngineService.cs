using PeerSharp.Interfaces;

namespace Peerfluence.Core.Services;

public interface ITorrentEngineService : IAsyncDisposable
{
    IClientEngine Engine { get; }

    /// <summary>
    /// Whether the configured proxy cost this session DHT or uTP, because an HTTP proxy cannot
    /// carry UDP and PeerSharp refuses to send it directly.
    /// </summary>
    bool ProxyRestrictionApplied { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Pushes the current speed limits at the running engine.
    ///
    /// <para>
    /// Separate from creating the engine because these are the two settings people change while
    /// downloading - to get out of the way of a video call, and back afterwards - and restarting the
    /// engine to apply them would drop every connection. The engine re-reads them from its own loops,
    /// so this is a write rather than a restart.
    /// </para>
    /// </summary>
    void ApplySpeedLimits();

    Task LoadOptionalDataAsync(CancellationToken cancellationToken);

    Task ShutdownAsync(CancellationToken cancellationToken);
}
