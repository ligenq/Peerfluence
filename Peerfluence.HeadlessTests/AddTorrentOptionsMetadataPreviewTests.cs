using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Services;
using Peerfluence.ViewModels;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.HeadlessTests;

public sealed class AddTorrentOptionsMetadataPreviewTests
{
    private const string MagnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";

    [AvaloniaFact]
    public async Task AddCommand_DoesNotWaitForAnInFlightPreviewToUnwind()
    {
        // A preview's torrent claims no info hash, so the add has nothing to wait for: it cancels
        // the preview and goes straight on, however long the engine takes to discard it.
        var previewReleased = false;
        var previewService = Substitute.For<IMagnetMetadataPreviewService>();
        previewService
            .FetchAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call => FetchUntilCancelled(call.Arg<CancellationToken>(), () => previewReleased = true));

        var previewHadUnwoundWhenAddRan = true;
        var torrentService = Substitute.For<ITorrentService>();
        torrentService
            .AddMagnetAsync(Arg.Any<string>(), Arg.Any<PeerSharp.Config.AddTorrentOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                previewHadUnwoundWhenAddRan = previewReleased;
                return Task.FromResult(Substitute.For<ITorrent>());
            });

        var sut = CreateMagnetViewModel(torrentService);
        sut.StartMetadataPreview(previewService, TimeSpan.FromMinutes(1));

        await sut.AddCommand.ExecuteAsync(null);

        Assert.False(previewHadUnwoundWhenAddRan);
        Assert.False(sut.IsFetchingMetadata);
        Assert.True(sut.WasAdded);
        Assert.Equal(string.Empty, sut.ErrorMessage);
    }

    [AvaloniaFact]
    public async Task StartMetadataPreview_SaysSoWhenTheFetchFails()
    {
        var previewService = Substitute.For<IMagnetMetadataPreviewService>();
        previewService
            .FetchAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns<Task<MagnetMetadataPreview?>>(_ => throw new InvalidOperationException("the fetch failed"));

        var sut = CreateMagnetViewModel(Substitute.For<ITorrentService>());
        sut.StartMetadataPreview(previewService, TimeSpan.FromMinutes(1));

        await WaitForAsync(() => !sut.IsFetchingMetadata);

        Assert.Equal(Peerfluence.Properties.Resources.AddTorrent_MetadataPreviewUnavailable, sut.MetadataStatusText);
    }

    private static async Task<MagnetMetadataPreview?> FetchUntilCancelled(
        CancellationToken cancellationToken,
        Action onReleased)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        finally
        {
            // Stands in for the engine discarding its transient torrent, which the real fetch does
            // after cancellation rather than before returning.
            await Task.Delay(20, CancellationToken.None);
            onReleased();
        }

        return null;
    }

    private static AddTorrentOptionsViewModel CreateMagnetViewModel(ITorrentService torrentService)
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(new AppSettings
        {
            Storage =
            {
                DownloadPath = "C:\\Downloads"
            }
        });

        return AddTorrentOptionsViewModel.CreateForMagnet(
            MagnetUri,
            torrentService,
            Substitute.For<ITopLevelService>(),
            settingsService);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100 && !condition(); attempt++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
