using Peerfluence.Core.Config;

namespace Peerfluence.Tests.Config;

/// <summary>
/// The stored streaming policy, and the two questions everything else asks it: is this number in
/// range, and is this address one a shared link could name.
/// </summary>
/// <remarks>
/// Both live here rather than at each use site because the settings screen, the MCP tool, the
/// engine and the listener all have to agree, and four copies of the same limits would not.
/// </remarks>
public sealed class StreamingSettingsTests
{
    [Fact]
    public void Normalize_LeavesSoundValuesAloneAndSaysSo()
    {
        var settings = new StreamingSettings();
        var expected = new StreamingSettings();

        Assert.False(settings.Normalize());

        Assert.Equal(expected.ReadAheadBytes, settings.ReadAheadBytes);
        Assert.Equal(expected.ReadAheadSeconds, settings.ReadAheadSeconds);
        Assert.Equal(expected.RequestQueueSecondsWhileBuffering, settings.RequestQueueSecondsWhileBuffering);
        Assert.Equal(expected.DataWaitTimeoutSeconds, settings.DataWaitTimeoutSeconds);
        Assert.Equal(expected.MetadataWaitTimeoutSeconds, settings.MetadataWaitTimeoutSeconds);
        Assert.Equal(expected.Port, settings.Port);
    }

    [Fact]
    public void Normalize_ClampsEveryNumberIntoRange()
    {
        var settings = new StreamingSettings
        {
            ReadAheadBytes = 1,
            ReadAheadSeconds = -5,
            RequestQueueSecondsWhileBuffering = 9000,
            DataWaitTimeoutSeconds = -1,
            MetadataWaitTimeoutSeconds = int.MaxValue,
            Port = 70000,
        };

        Assert.True(settings.Normalize());

        Assert.Equal(StreamingSettings.MinReadAheadBytes, settings.ReadAheadBytes);
        Assert.Equal(0, settings.ReadAheadSeconds);
        Assert.Equal(StreamingSettings.MaxRequestQueueSeconds, settings.RequestQueueSecondsWhileBuffering);
        Assert.Equal(0, settings.DataWaitTimeoutSeconds);
        Assert.Equal(StreamingSettings.MaxWaitTimeoutSeconds, settings.MetadataWaitTimeoutSeconds);
        Assert.Equal(StreamingSettings.MaxPort, settings.Port);
        Assert.False(settings.Normalize());
    }

    [Theory]
    [InlineData(long.MaxValue, StreamingSettings.MaxReadAheadBytes)]
    [InlineData(long.MinValue, StreamingSettings.MinReadAheadBytes)]
    public void Normalize_SurvivesTheExtremesOfTheStoredType(long stored, long expected)
    {
        var settings = new StreamingSettings { ReadAheadBytes = stored };

        settings.Normalize();

        Assert.Equal(expected, settings.ReadAheadBytes);
    }

    [Theory]
    [InlineData("192.168.1.23")]
    [InlineData("10.0.0.7")]
    [InlineData("fe80::1")]
    // Loopback is specific, so it is accepted. It is a link only this machine can open, which is
    // a reasonable thing to ask for explicitly and quite different from asking for it by accident.
    [InlineData("127.0.0.1")]
    [InlineData("  192.168.1.23  ")]
    public void IsShareableAddress_AcceptsASpecificAddress(string address)
    {
        Assert.True(StreamingSettings.IsShareableAddress(address));
    }

    [Theory]
    // Blank is the default, and the case that used to produce a loopback link from a button
    // labelled "Copy streaming link". Refusing it is what makes the prerequisite visible.
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    // A URL naming every interface names none of them.
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("255.255.255.255")]
    [InlineData("not-an-address")]
    [InlineData("192.168.1.23.9")]
    public void IsShareableAddress_RefusesBlankWildcardAndNonsense(string? address)
    {
        Assert.False(StreamingSettings.IsShareableAddress(address));
    }
}
