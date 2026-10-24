using System.IO.Abstractions;
using Peerfluence.Core.Services;
using Peerfluence.Core.Config;

namespace Peerfluence.Tests.Services;

public sealed class AppSettingsServiceTests : IDisposable
{
    [Fact]
    public async Task LoadAsync_NormalizesLimitsRejectedByPeerSharp51()
    {
        var paths = Substitute.For<IAppPaths>();
        paths.DefaultDownloadDirectory.Returns("downloads");
        paths.SessionDirectory.Returns("session");
        var store = Substitute.For<IAppSettingsStore>();
        var loaded = new AppSettings
        {
            Queue = { MaxActiveDownloads = -1, MaxActiveSeeds = -2 },
            Seeding = { LimitRatio = true, RatioLimit = float.NaN, LimitSeedTime = true, SeedTimeLimitMinutes = -1 },
            Streaming = null!,
        };
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(loaded);
        var sut = new AppSettingsService(paths, store, Substitute.For<IFileSystem>());
        await sut.LoadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, sut.Current.Queue.MaxActiveDownloads);
        Assert.Equal(0, sut.Current.Queue.MaxActiveSeeds);
        Assert.False(sut.Current.Seeding.LimitRatio);
        Assert.False(sut.Current.Seeding.LimitSeedTime);
        Assert.Equal(2, sut.Current.Seeding.RatioLimit);
        Assert.NotNull(sut.Current.Streaming);
        var defaults = new PeerSharp.Config.StreamingSettings();
        Assert.Equal(defaults.ReadAheadBytes, sut.Current.Streaming.ReadAheadBytes);
        Assert.Equal(defaults.ReadAheadSeconds, sut.Current.Streaming.ReadAheadSeconds);
        Assert.Equal(defaults.RequestQueueSecondsWhileBuffering, sut.Current.Streaming.RequestQueueSecondsWhileBuffering);
        Assert.Equal(defaults.DataWaitTimeoutSeconds, sut.Current.Streaming.DataWaitTimeoutSeconds);
        Assert.Equal(defaults.MetadataWaitTimeoutSeconds, sut.Current.Streaming.MetadataWaitTimeoutSeconds);
        await store.Received(1).SaveAsync(loaded, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// An out-of-range streaming number is corrected on load, like every other stored limit.
    /// </summary>
    /// <remarks>
    /// The port matters most: the stream listener rejects one outside 0-65535 rather than clamping
    /// it, so an uncorrected 70000 would break every shared link with a raw argument error, while
    /// the settings screen showed a clamped value that disagreed with the file.
    /// </remarks>
    [Fact]
    public async Task LoadAsync_NormalizesStreamingRangesAndRewritesTheFile()
    {
        var paths = Substitute.For<IAppPaths>();
        paths.DefaultDownloadDirectory.Returns("downloads");
        paths.SessionDirectory.Returns("session");
        var store = Substitute.For<IAppSettingsStore>();
        var loaded = new AppSettings
        {
            Streaming =
            {
                Port = 70000,
                ReadAheadBytes = 1,
                ReadAheadSeconds = -5,
                RequestQueueSecondsWhileBuffering = 9000,
                DataWaitTimeoutSeconds = -1,
                MetadataWaitTimeoutSeconds = int.MaxValue,
            },
        };
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(loaded);
        var sut = new AppSettingsService(paths, store, Substitute.For<IFileSystem>());

        await sut.LoadAsync(TestContext.Current.CancellationToken);

        var streaming = sut.Current.Streaming;
        Assert.Equal(StreamingSettings.MaxPort, streaming.Port);
        Assert.Equal(StreamingSettings.MinReadAheadBytes, streaming.ReadAheadBytes);
        Assert.Equal(0, streaming.ReadAheadSeconds);
        Assert.Equal(StreamingSettings.MaxRequestQueueSeconds, streaming.RequestQueueSecondsWhileBuffering);
        Assert.Equal(0, streaming.DataWaitTimeoutSeconds);
        Assert.Equal(StreamingSettings.MaxWaitTimeoutSeconds, streaming.MetadataWaitTimeoutSeconds);
        await store.Received(1).SaveAsync(loaded, Arg.Any<CancellationToken>());
    }

    /// <summary>Defaults are already in range, so loading a sound file does not rewrite it.</summary>
    [Fact]
    public async Task LoadAsync_LeavesStreamingDefaultsAloneAndDoesNotRewrite()
    {
        var paths = Substitute.For<IAppPaths>();
        paths.DefaultDownloadDirectory.Returns("downloads");
        paths.SessionDirectory.Returns("session");
        var store = Substitute.For<IAppSettingsStore>();
        var loaded = new AppSettings { Storage = { DownloadPath = "downloads", SessionPath = "session" } };
        var defaults = new StreamingSettings();
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(loaded);
        var sut = new AppSettingsService(paths, store, Substitute.For<IFileSystem>());

        await sut.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(new StreamingSettings().Normalize());
        Assert.Equal(defaults.ReadAheadBytes, sut.Current.Streaming.ReadAheadBytes);
        Assert.Equal(defaults.Port, sut.Current.Streaming.Port);
        await store.DidNotReceive().SaveAsync(Arg.Any<AppSettings>(), Arg.Any<CancellationToken>());
    }

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
        Assert.Equal("Teal", sut.Current.Theme.ColorTheme);
        Assert.Equal("en-US", sut.Current.Language);
        Assert.True(sut.Current.ShowRemoveTorrentOptions);
        Assert.Equal("RemoveOnly", sut.Current.DefaultRemoveTorrentAction);
        Assert.Equal(0, sut.Current.Network.MaxDiskReadSpeedBytesPerSecond);
        Assert.Equal(0, sut.Current.Network.MaxDiskWriteSpeedBytesPerSecond);
        Assert.Equal(6881, sut.Current.Network.ListeningPort);
        Assert.True(Directory.Exists(paths.DefaultDownloadDirectory));
        Assert.True(Directory.Exists(paths.SessionDirectory));

        await store.Received(1).SaveAsync(Arg.Is<AppSettings>(settings =>
            settings != null &&
            settings.Storage.DownloadPath == paths.DefaultDownloadDirectory &&
            settings.Storage.SessionPath == paths.SessionDirectory &&
            settings.Language == "en-US" &&
            settings.DefaultRemoveTorrentAction == "RemoveOnly" &&
            settings.Network.ListeningPort == 6881 &&
            settings.Network.MaxDiskReadSpeedBytesPerSecond == 0 &&
            settings.Network.MaxDiskWriteSpeedBytesPerSecond == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LoadAsync_WhenSettingsAreAlreadyValid_DoesNotRewriteSettings()
    {
        var paths = CreatePaths();
        var store = Substitute.For<IAppSettingsStore>();
        var loaded = new AppSettings
        {
            Storage =
            {
                DownloadPath = paths.DefaultDownloadDirectory,
                SessionPath = paths.SessionDirectory,
            },
            Theme =
            {
                ThemeVariant = "System",
                ColorTheme = "Teal",
            },
            Language = "en-US",
            DefaultRemoveTorrentAction = "RemoveOnly",
        };
        // Deliberately the old default rather than the current one: an existing profile's port
        // must survive the change to 6881, along with whatever the user forwarded through a router
        // for it.
        loaded.Network.ListeningPort = 55125;
        loaded.CompletionAction.TimeoutSeconds = 300;
        loaded.CompletionAction.WorkingDirectoryTemplate = "{downloadPath}";
        loaded.Mcp.MaxTorrentPayloadBytes = 10 * 1024 * 1024;
        store.LoadAsync(Arg.Any<CancellationToken>()).Returns(loaded);
        var sut = new AppSettingsService(paths, store, new FileSystem());

        await sut.LoadAsync(TestContext.Current.CancellationToken);

        await store.DidNotReceive().SaveAsync(Arg.Any<AppSettings>(), Arg.Any<CancellationToken>());
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
    public async Task SaveAsync_WaitsForServicesToApplyTheSavedSettings()
    {
        var sut = new AppSettingsService(
            CreatePaths(),
            Substitute.For<IAppSettingsStore>(),
            new FileSystem());
        var applied = false;
        sut.SettingsSaved += _ =>
        {
            applied = true;
            return Task.CompletedTask;
        };

        await sut.SaveAsync(TestContext.Current.CancellationToken);

        Assert.True(applied);
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
        Assert.Equal(6881, defaults.Network.ListeningPort);
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
