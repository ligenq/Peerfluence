using Peerfluence.Core.Config;
using PeerSharp.Config;
using ProxySettings = Peerfluence.Core.Config.ProxySettings;

namespace Peerfluence.Core.Services;

/// <summary>
/// Which UDP-carried features the engine may be given, once the configured proxy has had its say.
/// </summary>
/// <remarks>
/// <para>
/// PeerSharp 4.0 refuses to send UDP that a configured proxy cannot carry, rather than falling
/// through to an ordinary socket as it used to. Only SOCKS5 tunnels UDP; with an HTTP proxy the old
/// behaviour sent tracker and peer traffic through the proxy while the DHT announced the machine's
/// real address, which is the leak a proxy is bought to prevent.
/// </para>
/// <para>
/// The refusal is a throw out of <c>InitializeAsync</c>, so a stored configuration that used to run
/// now stops the application starting at all. That is the right call for a library and the wrong
/// one for a desktop client, which has a person waiting at a window: this turns off the features the
/// proxy cannot carry, keeps the proxy - the thing the user asked for - and says what it did.
/// </para>
/// </remarks>
public static class ProxyUdpPolicy
{
    /// <summary>
    /// Decides what may run alongside the configured proxy.
    /// </summary>
    /// <param name="proxy">The proxy as the user configured it.</param>
    /// <param name="dhtRequested">Whether the user asked for DHT.</param>
    public static ProxyUdpPlan Decide(ProxySettings proxy, bool dhtRequested)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        var engineProxy = new PeerSharp.Config.ProxySettings();
        ApplySettings(proxy, engineProxy);
        var udp = engineProxy.GetUdpCapabilities();

        // Disable unsupported DHT before enabling uTP: they share a listener in the engine.
        return new ProxyUdpPlan(
            EnableDht: dhtRequested && udp.SupportsDht,
            EnableUtp: udp.SupportsUtp,
            RestrictedByProxy: (dhtRequested && !udp.SupportsDht) || !udp.SupportsUtp);
    }

    internal static void ApplySettings(ProxySettings source, PeerSharp.Config.ProxySettings target)
    {
        target.Type = ParseProxyType(source.ProxyType);
        target.Host = source.ProxyHost;
        target.Port = (ushort)Math.Clamp(source.ProxyPort, 0, 65535);
        target.Username = source.ProxyUsername;
        target.Password = source.ProxyPassword;
        target.ProxyPeers = source.ProxyPeers;
        target.ProxyTrackers = source.ProxyTrackers;
    }

    /// <summary>
    /// Reads the stored proxy type. Anything unrecognised is no proxy, which is what the engine
    /// setup has always done with it.
    /// </summary>
    public static ProxyType ParseProxyType(string? type) => type switch
    {
        "Socks5" => ProxyType.Socks5,
        "Http" => ProxyType.Http,
        _ => ProxyType.None
    };
}

/// <summary>
/// The outcome of <see cref="ProxyUdpPolicy.Decide"/>.
/// </summary>
/// <param name="EnableDht">Whether the engine may run a DHT node.</param>
/// <param name="EnableUtp">Whether the engine may use uTP.</param>
/// <param name="RestrictedByProxy">
/// Whether anything was turned off that the user had asked for. This is what decides whether to
/// tell them: a proxy that costs nothing is not worth a notification.
/// </param>
public readonly record struct ProxyUdpPlan(bool EnableDht, bool EnableUtp, bool RestrictedByProxy);
