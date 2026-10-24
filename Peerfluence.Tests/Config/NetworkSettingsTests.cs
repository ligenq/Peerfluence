using Peerfluence.Core.Config;

namespace Peerfluence.Tests.Config;

public class NetworkSettingsTests
{
    [Theory]
    [InlineData(null, 0, "127.0.0.1", 1, true)]
    [InlineData("  sam.local  ", 70000, "sam.local", 65535, true)]
    [InlineData("localhost", 7656, "localhost", 7656, false)]
    public void NormalizeI2p_RepairsEndpointsAndPreservesNetworkIsolation(
        string? host, int port, string expectedHost, int expectedPort, bool changed)
    {
        var settings = new NetworkSettings { I2pSamHost = host!, I2pSamPort = port };
        Assert.Equal(changed, settings.NormalizeI2p());
        Assert.Equal(expectedHost, settings.I2pSamHost);
        Assert.Equal(expectedPort, settings.I2pSamPort);
        Assert.False(settings.EnableI2p);
        Assert.False(settings.I2pAllowMixedNetworks);
    }

    [Theory]
    [InlineData("Require", "Allow", false)]
    [InlineData("Inherit", "Refuse", false)]
    [InlineData(null, "unexpected", true)]
    [InlineData("", "inherit", true)]
    public void NormalizeEncryption_PreservesValidChoicesAndRepairsUnknownOverrides(
        string? incoming, string outgoing, bool changed)
    {
        var settings = new NetworkSettings { IncomingEncryptionMode = incoming!, OutgoingEncryptionMode = outgoing };
        Assert.Equal(changed, settings.NormalizeEncryption());
        Assert.Equal(changed ? "Inherit" : incoming, settings.IncomingEncryptionMode);
        Assert.Equal(changed ? "Inherit" : outgoing, settings.OutgoingEncryptionMode);
        Assert.True(settings.PreferRc4);
    }
}
