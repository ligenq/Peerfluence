using Peerfluence.Core.Services;
using Peerfluence.Services;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

public sealed class MagnetMetadataPreviewServiceTests
{
    [Fact]
    public async Task FetchAsync_UsesRunningEngineAndMapsTorrentMetadata()
    {
        var torrentFile = new TorrentFileBuilder()
            .WithName("Preview")
            .WithPrivate()
            .WithPieceLength(16 * 1024)
            .AddTracker("https://tracker.example/announce")
            .AddFile("folder/a.bin", new byte[10])
            .AddFile("folder/b.bin", new byte[20])
            .Build();

        var engine = Substitute.For<IClientEngine>();
        engine.GetMagnetMetadataAsync(Arg.Any<MagnetLink>(), Arg.Any<CancellationToken>())
            .Returns(torrentFile);
        var engineService = Substitute.For<ITorrentEngineService>();
        engineService.Engine.Returns(engine);
        var sut = new MagnetMetadataPreviewService(engineService);

        var magnetUri = $"magnet:?xt=urn:btih:{torrentFile.InfoHash}";
        var result = await sut.FetchAsync(
            magnetUri,
            TimeSpan.FromSeconds(5),
            cancellationToken: TestContext.Current.CancellationToken);

        result = Assert.IsType<MagnetMetadataPreview>(result);
        Assert.Equal("Preview", result.Name);
        Assert.Equal(torrentFile.InfoHash.ToString(), result.Hash);
        Assert.Equal("V1", result.VersionLabel);
        Assert.Equal(30, result.TotalSizeBytes);
        Assert.Equal(2, result.FileCount);
        Assert.True(result.IsPrivate);
        Assert.Same(torrentFile, result.TorrentFile);
        Assert.Equal("https://tracker.example/announce", Assert.Single(result.Trackers));
        Assert.Collection(
            result.Files,
            file => Assert.Equal((0, Path.Combine("folder", "a.bin"), 10L), (file.Index, file.Path, file.SizeBytes)),
            file => Assert.Equal((1, Path.Combine("folder", "b.bin"), 20L), (file.Index, file.Path, file.SizeBytes)));

        await engine.Received(1).GetMagnetMetadataAsync(
            Arg.Is<MagnetLink>(magnet => magnet != null && magnet.InfoHash == torrentFile.InfoHash),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FetchAsync_ReturnsNullWhenMetadataFetchTimesOut()
    {
        var engine = Substitute.For<IClientEngine>();
        engine.GetMagnetMetadataAsync(Arg.Any<MagnetLink>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromCanceled<TorrentFile>(call.Arg<CancellationToken>()));
        var engineService = Substitute.For<ITorrentEngineService>();
        engineService.Engine.Returns(engine);
        var sut = new MagnetMetadataPreviewService(engineService);

        var result = await sut.FetchAsync(
            $"magnet:?xt=urn:btih:{new string('a', 40)}",
            TimeSpan.Zero,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }
}
