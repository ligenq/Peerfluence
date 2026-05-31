using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

public class AddTorrentDialogServiceTests
{
    [Fact]
    public async Task ShowMagnetAsync_WhenOptionsAreDisabled_AddsWithoutOpeningDialog()
    {
        const string magnet = "magnet:?xt=urn:btih:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var torrentService = Substitute.For<ITorrentService>();
        torrentService
            .AddMagnetAsync(magnet, null, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<ITorrent>()));

        var sut = new AddTorrentDialogService(
            torrentService,
            Substitute.For<ITopLevelService>(),
            CreateSettingsService(showAddTorrentOptions: false),
            Substitute.For<IMagnetMetadataPreviewService>());

        var wasAdded = await sut.ShowMagnetAsync(magnet);

        Assert.True(wasAdded);
        await torrentService.Received(1).AddMagnetAsync(magnet, null, Arg.Any<CancellationToken>());
    }

    private static IAppSettingsService CreateSettingsService(bool showAddTorrentOptions)
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(new AppSettings
        {
            ShowAddTorrentOptions = showAddTorrentOptions
        });
        return settingsService;
    }
}
