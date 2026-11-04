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

    /// <summary>
    /// Cancelled when the application begins shutting down. Long engine work has to be handed this,
    /// because disposing the engine waits for a torrent that is checking its files rather than
    /// ending the check.
    /// </summary>
    CancellationToken ShutdownToken { get; }

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

    /// <summary>Updates the streaming policy used by streams opened after this call.</summary>
    void ApplyStreamingSettings();

    /// <summary>Applies encryption preferences to future peer handshakes.</summary>
    void ApplyEncryptionSettings();

    /// <summary>Sets how far the next tracker and web seed requests are kept from local services.</summary>
    void ApplySsrfMitigation();

    Task LoadOptionalDataAsync(CancellationToken cancellationToken);

    Task ShutdownAsync(CancellationToken cancellationToken);
}
