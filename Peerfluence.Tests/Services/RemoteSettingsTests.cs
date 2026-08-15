using Peerfluence.Core.Config;

namespace Peerfluence.Tests.Services;

public sealed class RemoteSettingsTests
{
    [Fact]
    public void RemoteControl_IsOffUntilAskedFor()
    {
        var settings = new RemoteSettings();

        Assert.False(settings.Enabled);
        Assert.False(settings.AllowRemoteConnections);
        Assert.Equal(9091, settings.Port);
    }

    /// <summary>
    /// The one combination that must never start. Anyone who could reach the port would be able to
    /// add and delete downloads, so it is refused rather than served - and the settings screen says
    /// so while it is being typed rather than leaving it to a log nobody reads.
    /// </summary>
    [Fact]
    public void ListeningBeyondThisMachineWithoutAPassword_IsNotUsable()
    {
        var settings = new RemoteSettings { Enabled = true, AllowRemoteConnections = true };

        Assert.False(settings.IsUsable);

        settings.Username = "peerfluence";

        Assert.True(settings.IsUsable);
    }

    [Fact]
    public void LoopbackWithoutAPassword_IsFine()
    {
        // Nothing off this machine can reach it, which is the case the default exists for.
        var settings = new RemoteSettings { Enabled = true };

        Assert.True(settings.IsUsable);
        Assert.False(settings.RequiresAuthentication);
    }
}
