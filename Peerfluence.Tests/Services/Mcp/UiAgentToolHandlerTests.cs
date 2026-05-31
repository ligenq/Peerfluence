using ModelContextProtocol.Protocol;
using Peerfluence.Core.Services;
using Peerfluence.Services.Mcp;
using Peerfluence.Services;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services.Mcp;

public sealed class UiAgentToolHandlerTests
{
    [Fact]
    public async Task GetUiTestStateAsync_ReturnsStructuredTorrentState()
    {
        var torrent = CreateTorrent("Ubuntu", 0.25f, TorrentState.Active);
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(new[] { torrent });

        var selectionService = Substitute.For<ITorrentSelectionService>();
        selectionService.SelectedTorrent.Returns(torrent);

        var sut = new UiAgentToolHandler(
            torrentService,
            selectionService,
            Substitute.For<ITopLevelService>(),
            new UiAgentTimeline());

        var result = await sut.GetUiTestStateAsync(TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("\"WindowAvailable\":false", Text(result));
        Assert.Contains("\"Name\":\"Ubuntu\"", Text(result));
        Assert.Contains("\"Progress\":0.25", Text(result));
        Assert.Contains("\"SelectedTorrentName\":\"Ubuntu\"", Text(result));
    }

    [Fact]
    public async Task LoadTorrentFileAsync_LoadsTorrentAndSelectsIt()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.torrent");
        await File.WriteAllBytesAsync(tempPath, new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);

        try
        {
            var torrent = CreateTorrent("Loaded", 0.1f, TorrentState.Active);
            var torrentService = Substitute.For<ITorrentService>();
            torrentService.AddTorrentFileAsync(tempPath, null, Arg.Any<CancellationToken>())
                .Returns(torrent);

            var selectionService = Substitute.For<ITorrentSelectionService>();
            var sut = new UiAgentToolHandler(
                torrentService,
                selectionService,
                Substitute.For<ITopLevelService>(),
                new UiAgentTimeline());

            var result = await sut.LoadTorrentFileAsync(tempPath, TestContext.Current.CancellationToken);

            Assert.False(result.IsError);
            Assert.Contains("\"Name\":\"Loaded\"", Text(result));
            selectionService.Received(1).SelectedTorrent = torrent;
            await torrentService.Received(1).AddTorrentFileAsync(tempPath, null, Arg.Any<CancellationToken>());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public async Task StopTorrentAsync_StopsMatchedTorrent()
    {
        var torrent = CreateTorrent("Ubuntu", 0.5f, TorrentState.Active);
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(new[] { torrent });

        var selectionService = Substitute.For<ITorrentSelectionService>();
        var sut = new UiAgentToolHandler(
            torrentService,
            selectionService,
            Substitute.For<ITopLevelService>(),
            new UiAgentTimeline());

        var result = await sut.StopTorrentAsync("ubuntu", TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        await torrent.Received(1).StopAsync(Arg.Any<CancellationToken>());
        selectionService.Received(1).SelectedTorrent = torrent;
    }

    [Fact]
    public async Task ResumeTorrentAsync_StartsMatchedTorrent()
    {
        var torrent = CreateTorrent("Ubuntu", 0.5f, TorrentState.Stopped);
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(new[] { torrent });

        var selectionService = Substitute.For<ITorrentSelectionService>();
        var sut = new UiAgentToolHandler(
            torrentService,
            selectionService,
            Substitute.For<ITopLevelService>(),
            new UiAgentTimeline());

        var result = await sut.ResumeTorrentAsync("ubuntu", TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        await torrent.Received(1).StartAsync(Arg.Any<CancellationToken>());
        selectionService.Received(1).SelectedTorrent = torrent;
    }

    [Fact]
    public async Task ResumeTorrentAsync_ReturnsSuccess_WhenTorrentIsAlreadyActive()
    {
        var torrent = CreateTorrent("Ubuntu", 0.5f, TorrentState.Active);
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(new[] { torrent });

        var selectionService = Substitute.For<ITorrentSelectionService>();
        var sut = new UiAgentToolHandler(
            torrentService,
            selectionService,
            Substitute.For<ITopLevelService>(),
            new UiAgentTimeline());

        var result = await sut.ResumeTorrentAsync("ubuntu", TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        await torrent.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
        selectionService.Received(1).SelectedTorrent = torrent;
    }

    [Fact]
    public async Task WaitForTorrentAsync_ReturnsSuccess_WhenConditionAlreadyMatches()
    {
        var torrent = CreateTorrent("Ubuntu", 0.12f, TorrentState.Stopped);
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(new[] { torrent });

        var sut = new UiAgentToolHandler(
            torrentService,
            Substitute.For<ITorrentSelectionService>(),
            Substitute.For<ITopLevelService>(),
            new UiAgentTimeline());

        var result = await sut.WaitForTorrentAsync(
            "ubuntu",
            state: "Stopped",
            minProgressPercent: 10,
            timeoutSeconds: 1,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("\"Matched\":true", Text(result));
        Assert.Contains("\"Progress\":0.12", Text(result));
    }

    [Fact]
    public async Task SelectTorrentAsync_ReturnsError_WhenIdentifierIsAmbiguous()
    {
        var torrents = new[]
        {
            CreateTorrent("Ubuntu Desktop", 0.1f, TorrentState.Active, 1),
            CreateTorrent("Ubuntu Server", 0.2f, TorrentState.Active, 2)
        };

        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(torrents);

        var sut = new UiAgentToolHandler(
            torrentService,
            Substitute.For<ITorrentSelectionService>(),
            Substitute.For<ITopLevelService>(),
            new UiAgentTimeline());

        var result = await sut.SelectTorrentAsync("ubuntu");

        Assert.True(result.IsError);
        Assert.Contains("matched multiple torrents", Text(result));
    }

    [Fact]
    public async Task AssertTorrentAsync_ReturnsError_WhenAssertionFails()
    {
        var torrent = CreateTorrent("Ubuntu", 0.12f, TorrentState.Active);
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(new[] { torrent });

        var sut = new UiAgentToolHandler(
            torrentService,
            Substitute.For<ITorrentSelectionService>(),
            Substitute.For<ITopLevelService>(),
            new UiAgentTimeline());

        var result = await sut.AssertTorrentAsync(
            "ubuntu",
            state: "Stopped",
            minProgressPercent: 10,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("\"Passed\":false", Text(result));
        Assert.Contains("\"State\":\"Active\"", Text(result));
    }

    [Fact]
    public async Task CleanupAsync_RemovesTorrentsAndClearsSelection()
    {
        var torrent = CreateTorrent("Ubuntu", 0.5f, TorrentState.Stopped);
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns(new[] { torrent });
        var selectionService = Substitute.For<ITorrentSelectionService>();

        var sut = new UiAgentToolHandler(
            torrentService,
            selectionService,
            Substitute.For<ITopLevelService>(),
            new UiAgentTimeline());

        var result = await sut.CleanupAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("\"RemovedTorrents\":1", Text(result));
        await torrentService.Received(1).RemoveAsync(torrent, PeerSharp.Config.RemoveOptions.None, Arg.Any<CancellationToken>());
        selectionService.Received(1).SelectedTorrent = null;
    }

    [Fact]
    public async Task Timeline_CanBeReadAndCleared()
    {
        var timeline = new UiAgentTimeline();
        timeline.Record("test", "event");
        var sut = new UiAgentToolHandler(
            Substitute.For<ITorrentService>(),
            Substitute.For<ITorrentSelectionService>(),
            Substitute.For<ITopLevelService>(),
            timeline);

        var timelineResult = await sut.GetTimelineAsync();
        Assert.Contains("\"EventType\":\"test\"", Text(timelineResult));

        var clearResult = await sut.ClearTimelineAsync();
        Assert.False(clearResult.IsError);

        var emptyResult = await sut.GetTimelineAsync();
        Assert.Equal("[]", Text(emptyResult));
    }

    private static ITorrent CreateTorrent(string name, float progress, TorrentState state, byte hashSeed = 0)
    {
        var hashBytes = new byte[20];
        hashBytes[19] = hashSeed;

        var peers = Substitute.For<IPeers>();
        peers.GetConnectedPeers().Returns(Array.Empty<PeerInfo>());

        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns(name);
        torrent.Hash.Returns(new InfoHash(hashBytes));
        torrent.Progress.Returns(progress);
        torrent.State.Returns(state);
        torrent.TotalSize.Returns(1024);
        torrent.Peers.Returns(peers);
        return torrent;
    }

    private static string Text(CallToolResult result)
    {
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }
}
