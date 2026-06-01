using Peerfluence.Core.Config;

namespace Peerfluence.Tests.Services;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var settings = new AppSettings();

        Assert.Equal(string.Empty, settings.Storage.DownloadPath);
        Assert.Equal(string.Empty, settings.Storage.SessionPath);
        Assert.True(settings.Storage.EnableSessionPersistence);
        Assert.True(settings.Network.EnableDht);
        Assert.True(settings.Network.EnableNatPmp);
        Assert.False(settings.Network.EnableUpnp);
        Assert.False(settings.Network.UseAutomaticListeningPort);
        Assert.Equal(55125, settings.Network.ListeningPort);
        Assert.Equal(0, settings.Network.MaxDiskReadSpeedBytesPerSecond);
        Assert.Equal(0, settings.Network.MaxDiskWriteSpeedBytesPerSecond);
        Assert.Equal("System", settings.Theme.ThemeVariant);
        Assert.Equal("Indigo", settings.Theme.ColorTheme);
        Assert.Equal("GradientSoft", settings.Theme.BackgroundStyle);
        Assert.Equal("en-US", settings.Language);
        Assert.True(settings.ShowRemoveTorrentOptions);
        Assert.Equal("RemoveOnly", settings.DefaultRemoveTorrentAction);
    }

    [Fact]
    public void QueueDefaults_AreCorrect()
    {
        var settings = new AppSettings();

        Assert.False(settings.Queue.EnableQueueManagement);
        Assert.Equal(3, settings.Queue.MaxActiveDownloads);
        Assert.Equal(2, settings.Queue.MaxActiveSeeds);
    }

    [Fact]
    public void BlocklistDefaults_AreCorrect()
    {
        var settings = new AppSettings();

        Assert.False(settings.EnableBlocklist);
        Assert.Equal(string.Empty, settings.BlocklistPath);
    }

    [Fact]
    public void GeoIpDefaults_AreCorrect()
    {
        var settings = new AppSettings();

        Assert.False(settings.EnableGeoIp);
        Assert.Equal(string.Empty, settings.GeoIpPath);
    }

    [Fact]
    public void EncryptionDefault_IsAllow()
    {
        var settings = new AppSettings();
        Assert.Equal("Allow", settings.EncryptionMode);
    }

    [Fact]
    public void ProxyDefaults_AreCorrect()
    {
        var settings = new AppSettings();

        Assert.Equal("None", settings.Proxy.ProxyType);
        Assert.Equal(string.Empty, settings.Proxy.ProxyHost);
        Assert.Equal(0, settings.Proxy.ProxyPort);
        Assert.Equal(string.Empty, settings.Proxy.ProxyUsername);
        Assert.Equal(string.Empty, settings.Proxy.ProxyPassword);
        Assert.True(settings.Proxy.ProxyPeers);
        Assert.True(settings.Proxy.ProxyTrackers);
    }

    [Fact]
    public void UpdateDefaults_AreCorrect()
    {
        var settings = new AppSettings();

        Assert.Equal(UpdateSettings.DefaultUpdateUrl, settings.Update.UpdateUrl);
        Assert.True(settings.Update.CheckForUpdatesOnStartup);
    }

    [Fact]
    public void CompletionActionDefaults_AreCorrect()
    {
        var settings = new AppSettings();

        Assert.False(settings.CompletionAction.Enabled);
        Assert.Equal(string.Empty, settings.CompletionAction.ProgramPath);
        Assert.Equal(string.Empty, settings.CompletionAction.ArgumentsTemplate);
        Assert.Equal("{downloadPath}", settings.CompletionAction.WorkingDirectoryTemplate);
        Assert.Equal(300, settings.CompletionAction.TimeoutSeconds);
        Assert.True(settings.CompletionAction.RunHidden);
    }

    [Fact]
    public void AllProperties_AreSettable()
    {
        var settings = new AppSettings();
        settings.Storage.DownloadPath = "/downloads";
        settings.Storage.SessionPath = "/session";
        settings.Storage.EnableSessionPersistence = false;
        settings.Network.EnableDht = false;
        settings.Network.EnableNatPmp = false;
        settings.Network.EnableUpnp = true;
        settings.Network.UseAutomaticListeningPort = true;
        settings.Network.ListeningPort = 51413;
        settings.Network.MaxDiskReadSpeedBytesPerSecond = 1000;
        settings.Network.MaxDiskWriteSpeedBytesPerSecond = 2000;
        settings.Theme.ThemeVariant = "Dark";
        settings.Theme.ColorTheme = "Rose";
        settings.Theme.BackgroundStyle = "Flat";
        settings.Language = "de-DE";
        settings.Queue.EnableQueueManagement = true;
        settings.Queue.MaxActiveDownloads = 5;
        settings.Queue.MaxActiveSeeds = 3;
        settings.EnableBlocklist = true;
        settings.BlocklistPath = "/blocklist.txt";
        settings.EnableGeoIp = true;
        settings.GeoIpPath = "/geo.mmdb";
        settings.EncryptionMode = "Require";
        settings.Proxy.ProxyType = "Socks5";
        settings.Proxy.ProxyHost = "proxy.test";
        settings.Proxy.ProxyPort = 1080;
        settings.Proxy.ProxyUsername = "user";
        settings.Proxy.ProxyPassword = "pass";
        settings.Proxy.ProxyPeers = false;
        settings.Proxy.ProxyTrackers = false;
        settings.CompletionAction.Enabled = true;
        settings.CompletionAction.ProgramPath = "/bin/tool";
        settings.CompletionAction.ArgumentsTemplate = "--path {downloadPath}";
        settings.CompletionAction.WorkingDirectoryTemplate = "/downloads";
        settings.CompletionAction.TimeoutSeconds = 60;
        settings.CompletionAction.RunHidden = false;
        settings.ShowRemoveTorrentOptions = false;
        settings.AssociateTorrentFiles = true;
        settings.AssociateMagnetLinks = true;
        settings.DefaultRemoveTorrentAction = "DeleteAll";

        Assert.Equal("/downloads", settings.Storage.DownloadPath);
        Assert.Equal("Dark", settings.Theme.ThemeVariant);
        Assert.True(settings.Network.UseAutomaticListeningPort);
        Assert.Equal(51413, settings.Network.ListeningPort);
        Assert.Equal("Socks5", settings.Proxy.ProxyType);
        Assert.Equal(1080, settings.Proxy.ProxyPort);
        Assert.True(settings.CompletionAction.Enabled);
        Assert.Equal("/bin/tool", settings.CompletionAction.ProgramPath);
        Assert.False(settings.ShowRemoveTorrentOptions);
        Assert.True(settings.AssociateTorrentFiles);
        Assert.True(settings.AssociateMagnetLinks);
        Assert.Equal("DeleteAll", settings.DefaultRemoveTorrentAction);
    }
}
