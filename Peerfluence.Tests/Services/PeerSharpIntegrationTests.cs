using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using PeerSharp.Config;
using PeerSharp.Core;

namespace Peerfluence.Tests.Services;

public sealed class PeerSharpIntegrationTests
{
    [Fact]
    public async Task AddingATorrent_AppliesSelectionsTrackersAndWebSeedsBeforeStarting()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"peerfluence-peersharp-{Guid.NewGuid():N}");
        var settings = Substitute.For<IAppSettingsService>();
        settings.Current.Returns(new AppSettings
        {
            Storage = { DownloadPath = directory, EnableSessionPersistence = false },
            Network = { EnableDht = false, EnableNatPmp = false, EnableUpnp = false, UseAutomaticListeningPort = true },
        });
        var engine = new TorrentEngineService(settings, NullLoggerFactory.Instance, TimeProvider.System);
        try
        {
            await engine.InitializeAsync(TestContext.Current.CancellationToken);
            using var http = new HttpClient();
            var service = new TorrentService(engine, Substitute.For<IAppMessenger>(), http, settings);
            var metadata = new TorrentFileBuilder().WithName("Selected files").WithPieceLength(16 * 1024)
                .AddFile("skip.bin", new byte[10]).AddFile("keep.bin", new byte[20]).Build();
            var torrent = await service.AddTorrentAsync(metadata, new AddTorrentOptions
            {
                DownloadPath = directory,
                StartImmediately = false,
                FileSelections = [new FileSelection(false, Priority.DoNotDownload), new FileSelection(true, Priority.High)],
                AdditionalTrackers = ["https://tracker.invalid/announce"],
                AdditionalWebSeeds = ["https://seed.invalid/files/"],
            }, TestContext.Current.CancellationToken);

            Assert.False(torrent.Started);
            var selection = torrent.GetAllFileSelections();
            Assert.False(selection[0].Selected);
            Assert.True(selection[1].Selected);
            Assert.Equal(Priority.High, selection[1].Priority);
            Assert.Contains(torrent.Trackers.GetTrackers(), tracker => tracker.Url == "https://tracker.invalid/announce");
            Assert.Contains("https://seed.invalid/files/", torrent.WebSeeds.GetAll());
        }
        finally
        {
            await engine.DisposeAsync();
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
