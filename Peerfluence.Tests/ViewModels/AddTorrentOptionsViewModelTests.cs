using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.ViewModels;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.ViewModels;

public class AddTorrentOptionsViewModelTests
{
    [Fact]
    public void BuildOptions_MapsUserChoicesToAddTorrentOptions()
    {
        var sut = AddTorrentOptionsViewModel.CreateForMagnet(
            "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&dn=Test",
            Substitute.For<ITorrentService>(),
            Substitute.For<ITopLevelService>(),
            CreateSettingsService("C:\\Downloads"));

        sut.DownloadPath = "C:\\Downloads\\Test";
        sut.StartImmediately = false;
        sut.DownloadLimitKiBPerSecond = 128;
        sut.UploadLimitKiBPerSecond = 64;
        sut.QueuePriority = 7;
        sut.RatioLimit = 1.5f;
        sut.SeedTimeLimitMinutes = 45;
        sut.AdditionalTrackers = "udp://tracker.example:80\r\nudp://tracker.example:80\nhttps://tracker.two/announce";
        sut.Files.Add(new AddTorrentFileOptionViewModel(1, "b.bin", 200) { IsSelected = false });
        sut.Files.Add(new AddTorrentFileOptionViewModel(0, "a.bin", 100) { Priority = Priority.High });

        var options = sut.BuildOptions();

        Assert.Equal("C:\\Downloads\\Test", options.DownloadPath);
        Assert.False(options.StartImmediately);
        Assert.Equal(128 * 1024, options.DownloadLimitBytesPerSecond);
        Assert.Equal(64 * 1024, options.UploadLimitBytesPerSecond);
        Assert.Equal(7, options.QueuePriority);
        Assert.Equal(1.5f, options.RatioLimit);
        Assert.Equal(TimeSpan.FromMinutes(45), options.SeedTimeLimit);
        Assert.Equal(new[] { "udp://tracker.example:80", "https://tracker.two/announce" }, options.AdditionalTrackers);
        Assert.Collection(
            options.FileSelections!,
            file =>
            {
                Assert.True(file.Selected);
                Assert.Equal(Priority.High, file.Priority);
            },
            file =>
            {
                Assert.False(file.Selected);
                Assert.Equal(Priority.DoNotDownload, file.Priority);
            });
    }

    [Fact]
    public async Task AddCommand_WhenSkipIsChecked_DisablesFutureAddOptions()
    {
        var torrentService = Substitute.For<ITorrentService>();
        torrentService
            .AddMagnetAsync(Arg.Any<string>(), Arg.Any<PeerSharp.Config.AddTorrentOptions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<ITorrent>()));

        var settingsService = CreateSettingsService("C:\\Downloads");
        settingsService.SaveAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var sut = AddTorrentOptionsViewModel.CreateForMagnet(
            "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&dn=Test",
            torrentService,
            Substitute.For<ITopLevelService>(),
            settingsService);
        sut.SkipThisStepNextTime = true;

        await sut.AddCommand.ExecuteAsync(null);

        Assert.False(settingsService.Current.ShowAddTorrentOptions);
        await settingsService.Received(1).SaveAsync(Arg.Any<CancellationToken>());
        Assert.True(sut.WasAdded);
    }

    [Fact]
    public void ApplyMetadataPreview_UpdatesMagnetDetailsAndFileList()
    {
        var sut = AddTorrentOptionsViewModel.CreateForMagnet(
            "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&dn=Test",
            Substitute.For<ITorrentService>(),
            Substitute.For<ITopLevelService>(),
            CreateSettingsService("C:\\Downloads"));
        var preview = new MagnetMetadataPreview(
            "Resolved torrent",
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            "V1",
            300,
            2,
            12,
            16384,
            IsPrivate: false,
            new[]
            {
                new MagnetMetadataPreviewFile(0, "a.bin", 100),
                new MagnetMetadataPreviewFile(1, "b.bin", 200)
            },
            new[] { "udp://tracker.example:80", "https://tracker.two/announce" });

        Assert.True(sut.IsMetadataPending);

        sut.ApplyMetadataPreview(preview);

        Assert.Equal("Resolved torrent", sut.Name);
        Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", sut.Hash);
        Assert.Equal("V1", sut.VersionLabel);
        Assert.Equal(300, sut.TotalSizeBytes);
        Assert.Equal(2, sut.FileCount);
        Assert.Equal(12, sut.PieceCount);
        Assert.Equal(16384, sut.PieceSizeBytes);
        Assert.True(sut.HasFiles);
        Assert.False(sut.IsMetadataPending);
        Assert.Equal("udp://tracker.example:80\r\nhttps://tracker.two/announce", sut.ExistingTrackers);
        Assert.Collection(
            sut.Files,
            file =>
            {
                Assert.Equal(0, file.Index);
                Assert.Equal("a.bin", file.Path);
                Assert.Equal(100, file.SizeBytes);
            },
            file =>
            {
                Assert.Equal(1, file.Index);
                Assert.Equal("b.bin", file.Path);
                Assert.Equal(200, file.SizeBytes);
            });
    }

    private static IAppSettingsService CreateSettingsService(string downloadPath)
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(new AppSettings
        {
            Storage =
            {
                DownloadPath = downloadPath
            }
        });
        return settingsService;
    }
}
