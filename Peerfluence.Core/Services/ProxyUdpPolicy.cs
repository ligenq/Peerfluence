using Peerfluence.Core.Config;

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

        // A type without a host is not a usable proxy, and PeerSharp treats it as none everywhere
        // else that asks this question. Matching that keeps a half-filled proxy form from disabling
        // DHT for someone who never finished setting one up.
        bool carriesUdp = ParseProxyType(proxy.ProxyType) != ProxyKind.Http
            || string.IsNullOrWhiteSpace(proxy.ProxyHost);

        if (carriesUdp)
        {
            return new ProxyUdpPlan(dhtRequested, EnableUtp: true, RestrictedByProxy: false);
        }

        // uTP is only refused when peer traffic is configured to go through the proxy. Left alone
        // otherwise, because then it is not the proxy's to carry.
        bool enableUtp = !proxy.ProxyPeers;

        return new ProxyUdpPlan(
            EnableDht: false,
            EnableUtp: enableUtp,
            RestrictedByProxy: dhtRequested || !enableUtp);
    }

    /// <summary>
    /// Reads the stored proxy type. Anything unrecognised is no proxy, which is what the engine
    /// setup has always done with it.
    /// </summary>
    public static ProxyKind ParseProxyType(string? type) => type switch
    {
        "Socks5" => ProxyKind.Socks5,
        "Http" => ProxyKind.Http,
        _ => ProxyKind.None
    };
}

/// <summary>The proxy types the settings can hold.</summary>
public enum ProxyKind
{
    /// <summary>No proxy.</summary>
    None,

    /// <summary>A SOCKS5 proxy, which can tunnel UDP.</summary>
    Socks5,

    /// <summary>An HTTP proxy, which cannot carry UDP at all.</summary>
    Http
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
