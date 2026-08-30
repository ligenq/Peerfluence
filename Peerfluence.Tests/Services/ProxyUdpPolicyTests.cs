using Peerfluence.Core.Config;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

/// <summary>
/// What a configured proxy costs, and what it does not.
/// </summary>
/// <remarks>
/// PeerSharp 4.0 throws out of <c>InitializeAsync</c> for a configuration that used to run - an HTTP
/// proxy alongside DHT or proxied uTP - because only SOCKS5 can carry UDP and sending it directly
/// would expose the address the proxy exists to hide. Peerfluence turns those features off instead
/// of failing to start, so these are the tests that say which combinations lose what.
/// </remarks>
public sealed class ProxyUdpPolicyTests
{
    private static ProxySettings Proxy(string type, string host = "proxy.example", bool proxyPeers = true) =>
        new()
        {
            ProxyType = type,
            ProxyHost = host,
            ProxyPeers = proxyPeers
        };

    [Fact]
    public void NoProxy_KeepsEverything()
    {
        var plan = ProxyUdpPolicy.Decide(Proxy("None", host: string.Empty), dhtRequested: true);

        Assert.True(plan.EnableDht);
        Assert.True(plan.EnableUtp);
        Assert.False(plan.RestrictedByProxy);
    }

    [Fact]
    public void Socks5_KeepsEverything_BecauseItCanTunnelUdp()
    {
        var plan = ProxyUdpPolicy.Decide(Proxy("Socks5"), dhtRequested: true);

        Assert.True(plan.EnableDht);
        Assert.True(plan.EnableUtp);
        Assert.False(plan.RestrictedByProxy);
    }

    [Fact]
    public void HttpProxy_TurnsOffDhtAndUtp_AndSaysItDid()
    {
        var plan = ProxyUdpPolicy.Decide(Proxy("Http"), dhtRequested: true);

        Assert.False(plan.EnableDht);
        Assert.False(plan.EnableUtp);
        Assert.True(plan.RestrictedByProxy);
    }

    [Fact]
    public void HttpProxy_StillCostsUtp_WhenDhtWasNotWanted()
    {
        // The hazard is wider than DHT: uTP is on by default in the engine and Peerfluence proxies
        // peer traffic by default, so an HTTP proxy alone was enough to stop the engine starting.
        var plan = ProxyUdpPolicy.Decide(Proxy("Http"), dhtRequested: false);

        Assert.False(plan.EnableUtp);
        Assert.True(plan.RestrictedByProxy);
    }

    [Fact]
    public void HttpProxy_LeavesUtpAlone_WhenPeersAreNotProxied()
    {
        // Traffic the proxy was never asked to carry is not the proxy's to refuse.
        var plan = ProxyUdpPolicy.Decide(Proxy("Http", proxyPeers: false), dhtRequested: false);

        Assert.True(plan.EnableUtp);
        Assert.False(plan.RestrictedByProxy);
    }

    [Fact]
    public void HttpProxy_WithoutAHost_IsNotAProxyAtAll()
    {
        // Matches what the engine does with it, so a half-filled proxy form does not silently cost
        // DHT to someone who never finished setting one up.
        var plan = ProxyUdpPolicy.Decide(Proxy("Http", host: string.Empty), dhtRequested: true);

        Assert.True(plan.EnableDht);
        Assert.True(plan.EnableUtp);
        Assert.False(plan.RestrictedByProxy);
    }

    [Theory]
    [InlineData("Socks5", ProxyKind.Socks5)]
    [InlineData("Http", ProxyKind.Http)]
    [InlineData("None", ProxyKind.None)]
    [InlineData("something else", ProxyKind.None)]
    [InlineData(null, ProxyKind.None)]
    public void ParseProxyType_TreatsAnythingUnrecognisedAsNoProxy(string? stored, ProxyKind expected)
    {
        Assert.Equal(expected, ProxyUdpPolicy.ParseProxyType(stored));
    }
}
