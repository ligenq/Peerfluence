using System.IO.Abstractions;
using Peerfluence.Core.Services;
using Peerfluence.Core.Config;

namespace Peerfluence.Tests.Services;

public sealed class AppSettingsServiceTests : IDisposable
{
    private readonly string _rootPath;

    public AppSettingsServiceTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"peerfluence-settings-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);
    }

    [Fact]
    public async Task LoadAsync_NormalizesMissingValues_AndPersistsNormalizedSettings()
    {
        var paths = CreatePaths();
        var store = Substitute.For<IAppSettingsStore>();
        var loaded = new AppSettings
        {
            Storage =
            {
                DownloadPath = "",
                SessionPath = "",
            },
            Theme =
            {
                ThemeVariant = "",
                ColorTheme = "",
                BackgroundStyle = "",
            },
            Language = "",
            DefaultRemoveTorrentAction = "Nope",
        };
        loaded.Network.MaxDiskReadSpeedBytesPerSecond = -5;
        loaded.Network.MaxDiskWriteSpeedBytesPerSecond = -10;
        loaded.Network.ListeningPort = 0;
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(loaded);

        var sut = new AppSettingsService(paths, store, new FileSystem());

        await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(paths.DefaultDownloadDirectory, sut.Current.Storage.DownloadPath);
        Assert.Equal(paths.SessionDirectory, sut.Current.Storage.SessionPath);
        Assert.Equal("System", sut.Current.Theme.ThemeVariant);
        Assert.Equal("Indigo", sut.Current.Theme.ColorTheme);
        Assert.Equal("GradientSoft", sut.Current.Theme.BackgroundStyle);
        Assert.Equal("en-US", sut.Current.Language);
        Assert.True(sut.Current.ShowRemoveTorrentOptions);
        Assert.Equal("RemoveOnly", sut.Current.DefaultRemoveTorrentAction);
        Assert.Equal(0, sut.Current.Network.MaxDiskReadSpeedBytesPerSecond);
        Assert.Equal(0, sut.Current.Network.MaxDiskWriteSpeedBytesPerSecond);
        Assert.Equal(55125, sut.Current.Network.ListeningPort);
        Assert.True(Directory.Exists(paths.DefaultDownloadDirectory));
        Assert.True(Directory.Exists(paths.SessionDirectory));

        await store.Received(1).SaveAsync(Arg.Is<AppSettings>(settings =>
            settings.Storage.DownloadPath == paths.DefaultDownloadDirectory &&
            settings.Storage.SessionPath == paths.SessionDirectory &&
            settings.Language == "en-US" &&
            settings.DefaultRemoveTorrentAction == "RemoveOnly" &&
            settings.Network.ListeningPort == 55125 &&
            settings.Network.MaxDiskReadSpeedBytesPerSecond == 0 &&
            settings.Network.MaxDiskWriteSpeedBytesPerSecond == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveAsync_CreatesRequiredDirectories_BeforePersisting()
    {
        var paths = CreatePaths();
        var store = Substitute.For<IAppSettingsStore>();
        var sut = new AppSettingsService(paths, store, new FileSystem());

        sut.Current.Storage.DownloadPath = Path.Combine(_rootPath, "custom-downloads");
        sut.Current.Storage.SessionPath = Path.Combine(_rootPath, "custom-session");

        await sut.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(sut.Current.Storage.DownloadPath));
        Assert.True(Directory.Exists(sut.Current.Storage.SessionPath));
        await store.Received(1).SaveAsync(sut.Current, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CreateDefaultSettings_UsesConfiguredAppPaths()
    {
        var paths = CreatePaths();
        var sut = new AppSettingsService(paths, Substitute.For<IAppSettingsStore>(), new FileSystem());

        var defaults = sut.CreateDefaultSettings();

        Assert.Equal(paths.DefaultDownloadDirectory, defaults.Storage.DownloadPath);
        Assert.Equal(paths.SessionDirectory, defaults.Storage.SessionPath);
        Assert.True(defaults.Storage.EnableSessionPersistence);
        Assert.True(defaults.Network.EnableDht);
        Assert.False(defaults.Network.UseAutomaticListeningPort);
        Assert.Equal(55125, defaults.Network.ListeningPort);
        Assert.True(defaults.ShowRemoveTorrentOptions);
        Assert.Equal("RemoveOnly", defaults.DefaultRemoveTorrentAction);
    }

    private FakeAppPaths CreatePaths()
    {
        return new FakeAppPaths(
            Path.Combine(_rootPath, "appdata"),
            Path.Combine(_rootPath, "downloads"),
            Path.Combine(_rootPath, "session"),
            Path.Combine(_rootPath, "settings.json"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_rootPath))
            {
                Directory.Delete(_rootPath, recursive: true);
            }
        }
        catch
        {
        }
    }

    private sealed record FakeAppPaths(
        string AppDataDirectory,
        string DefaultDownloadDirectory,
        string SessionDirectory,
        string SettingsFilePath) : IAppPaths;
}
