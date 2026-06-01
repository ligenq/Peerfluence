using System.IO.Abstractions;
using Microsoft.Extensions.Logging;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Velopack;
using VelopackSemanticVersion = Velopack.SemanticVersion;

namespace Peerfluence.Tests.Services;

public class UpdateServiceTests
{
    private readonly IUpdateService _sut;

    public UpdateServiceTests()
    {
        var logger = Substitute.For<ILogger<UpdateService>>();
        var store = Substitute.For<IAppSettingsStore>();
        var paths = new AppPaths();
        var settingsService = new AppSettingsService(paths, store, new FileSystem());
        _sut = new UpdateService(logger, settingsService);
    }

    [Fact]
    public void IsInstalled_ReturnsFalse_WhenNotPackaged()
    {
        // In a test/dev environment, the app is not installed via Velopack.
        Assert.False(_sut.IsInstalled);
    }

    [Fact]
    public void IsUpdateAvailable_DefaultsFalse()
    {
        Assert.False(_sut.IsUpdateAvailable);
    }

    [Fact]
    public void Channel_IsDirectDownload_ForVelopackUpdateService()
    {
        Assert.Equal(UpdateChannel.DirectDownload, _sut.Channel);
    }

    [Fact]
    public void CanCheckForUpdates_ReturnsFalse_WhenVelopackIsNotInstalled()
    {
        Assert.False(_sut.CanCheckForUpdates);
    }

    [Fact]
    public void AvailableVersion_DefaultsNull()
    {
        Assert.Null(_sut.AvailableVersion);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsFalse_WhenNotInstalled()
    {
        var result = await _sut.CheckForUpdatesAsync();
        Assert.False(result);
    }

    [Fact]
    public async Task DownloadUpdateAsync_ReturnsFalse_WhenNotInstalled()
    {
        var result = await _sut.DownloadUpdateAsync();
        Assert.False(result);
    }

    [Fact]
    public void ApplyUpdateAndRestart_DoesNotThrow_WhenNotInstalled()
    {
        var exception = Record.Exception(() => _sut.ApplyUpdateAndRestart());
        Assert.Null(exception);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_RebuildsUpdateManagerWhenUpdateUrlChanges()
    {
        var logger = Substitute.For<ILogger<UpdateService>>();
        var store = Substitute.For<IAppSettingsStore>();
        var paths = new AppPaths();
        var settingsService = new AppSettingsService(paths, store, new FileSystem());

        var createdUrls = new List<string>();
        var adapters = new Queue<FakeUpdateManagerAdapter>(new[]
        {
            new FakeUpdateManagerAdapter(isInstalled: true),
            new FakeUpdateManagerAdapter(isInstalled: true)
        });

        var sut = new UpdateService(logger, settingsService, updateUrl =>
        {
            createdUrls.Add(updateUrl);
            return adapters.Dequeue();
        });

        settingsService.Current.Update.UpdateUrl = "https://updates.example/v1";
        await sut.CheckForUpdatesAsync();

        settingsService.Current.Update.UpdateUrl = "https://updates.example/v2";
        await sut.CheckForUpdatesAsync();

        Assert.Equal(new[] { "https://updates.example/v1", "https://updates.example/v2" }, createdUrls);
    }

    [Theory]
    [InlineData("https://github.com/ligenq/Peerfluence", "https://github.com/ligenq/Peerfluence")]
    [InlineData("https://github.com/ligenq/Peerfluence/releases", "https://github.com/ligenq/Peerfluence")]
    [InlineData(" https://github.com/ligenq/Peerfluence/releases/latest ", "https://github.com/ligenq/Peerfluence")]
    [InlineData("https://updates.example/peerfluence", "https://updates.example/peerfluence")]
    public void NormalizeUpdateUrl_UsesGithubRepositoryUrl_ForGithubReleasePages(string input, string expected)
    {
        Assert.Equal(expected, UpdateService.NormalizeUpdateUrl(input));
    }

    [Fact]
    public async Task CheckForUpdatesAsync_SetsAvailabilityAndVersion_WhenUpdateExists()
    {
        var logger = Substitute.For<ILogger<UpdateService>>();
        var settingsService = new AppSettingsService(new AppPaths(), Substitute.For<IAppSettingsStore>(), new FileSystem());
        var updateInfo = CreateUpdateInfo("2.3.4");
        var adapter = new FakeUpdateManagerAdapter(isInstalled: true)
        {
            UpdateInfo = updateInfo
        };

        var sut = new UpdateService(logger, settingsService, _ => adapter);

        var result = await sut.CheckForUpdatesAsync();

        Assert.True(result);
        Assert.True(sut.IsUpdateAvailable);
        Assert.Equal("2.3.4", sut.AvailableVersion);
    }

    [Fact]
    public async Task DownloadUpdateAsync_DownloadsAndStoresRelease_ForApplyUpdate()
    {
        var logger = Substitute.For<ILogger<UpdateService>>();
        var settingsService = new AppSettingsService(new AppPaths(), Substitute.For<IAppSettingsStore>(), new FileSystem());
        var updateInfo = CreateUpdateInfo("3.0.0");
        var adapter = new FakeUpdateManagerAdapter(isInstalled: true)
        {
            UpdateInfo = updateInfo
        };

        var sut = new UpdateService(logger, settingsService, _ => adapter);

        var downloadResult = await sut.DownloadUpdateAsync();
        sut.ApplyUpdateAndRestart(new[] { "--restart" });

        Assert.True(downloadResult);
        Assert.Same(updateInfo, adapter.DownloadedUpdate);
        Assert.Same(updateInfo.TargetFullRelease, adapter.AppliedAsset);
        Assert.Equal(new[] { "--restart" }, adapter.AppliedArgs);
    }

    [Fact]
    public async Task CheckForUpdatesAsync_ReturnsFalse_WhenAdapterThrows()
    {
        var logger = Substitute.For<ILogger<UpdateService>>();
        var settingsService = new AppSettingsService(new AppPaths(), Substitute.For<IAppSettingsStore>(), new FileSystem());
        var adapter = new FakeUpdateManagerAdapter(isInstalled: true)
        {
            CheckForUpdatesException = new InvalidOperationException("boom")
        };

        var sut = new UpdateService(logger, settingsService, _ => adapter);

        var result = await sut.CheckForUpdatesAsync();

        Assert.False(result);
        Assert.False(sut.IsUpdateAvailable);
        Assert.Null(sut.AvailableVersion);
    }

    [Fact]
    public async Task MicrosoftStoreUpdateService_DisablesSelfUpdates()
    {
        var logger = Substitute.For<ILogger<MicrosoftStoreUpdateService>>();
        var sut = new MicrosoftStoreUpdateService(logger);

        Assert.Equal(UpdateChannel.MicrosoftStore, sut.Channel);
        Assert.True(sut.IsInstalled);
        Assert.False(sut.CanCheckForUpdates);
        Assert.False(sut.CanApplyUpdates);
        Assert.False(await sut.CheckForUpdatesAsync());
        Assert.False(await sut.DownloadUpdateAsync());

        var exception = Record.Exception(() => sut.ApplyUpdateAndRestart());
        Assert.Null(exception);
    }

    private static UpdateInfo CreateUpdateInfo(string version)
    {
        return new UpdateInfo(
            new VelopackAsset
            {
                Version = VelopackSemanticVersion.Parse(version),
                FileName = "peerfluence.nupkg",
            },
            false,
            new VelopackAsset(),
            Array.Empty<VelopackAsset>());
    }

    private sealed class FakeUpdateManagerAdapter : UpdateService.IUpdateManagerAdapter
    {
        public FakeUpdateManagerAdapter(bool isInstalled)
        {
            IsInstalled = isInstalled;
        }

        public bool IsInstalled { get; }

        public UpdateInfo? UpdateInfo { get; init; }

        public Exception? CheckForUpdatesException { get; init; }

        public UpdateInfo? DownloadedUpdate { get; private set; }

        public VelopackAsset? AppliedAsset { get; private set; }

        public string[]? AppliedArgs { get; private set; }

        public Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            if (CheckForUpdatesException != null)
            {
                throw CheckForUpdatesException;
            }

            return Task.FromResult(UpdateInfo);
        }

        public Task DownloadUpdatesAsync(UpdateInfo update, Action<int>? progress, CancellationToken cancellationToken)
        {
            DownloadedUpdate = update;
            return Task.CompletedTask;
        }

        public void ApplyUpdatesAndRestart(VelopackAsset? asset, string[]? restartArgs)
        {
            AppliedAsset = asset;
            AppliedArgs = restartArgs;
        }
    }
}
