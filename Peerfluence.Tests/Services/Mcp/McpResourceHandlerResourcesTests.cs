using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core.Services;
using Peerfluence.Services.Mcp;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services.Mcp;

/// <summary>
/// The read-only views an agent asks the running application for.
/// </summary>
/// <remarks>
/// These are resources rather than tools, so there is no <c>isError</c> flag to lean on: a failure
/// has to arrive as JSON the caller can tell apart from an answer. That is what most of these check
/// - that a bad hash, a torrent that is not there, and an engine that is not up all produce a coded
/// error rather than an exception crossing the pipe.
/// </remarks>
[Collection("Messenger")]
public sealed class McpResourceHandlerResourcesTests : IDisposable
{
    private static readonly string Hash = new InfoHash(Enumerable.Repeat((byte)0x11, InfoHash.V1Length).ToArray()).ToHexString();

    private readonly List<McpResourceHandler> _handlers = [];

    private McpResourceHandler Handler(ITorrentEngineService engineService)
    {
        var handler = new McpResourceHandler(
            engineService,
            new AppPaths(Path.Combine(Path.GetTempPath(), $"peerfluence-mcp-resource-{Guid.NewGuid():N}")),
            NullLogger<McpResourceHandler>.Instance,
            Substitute.For<IEngineMetricsReader>());

        _handlers.Add(handler);
        return handler;
    }

    private McpResourceHandler HandlerWith(params ITorrent[] torrents)
    {
        var engine = Substitute.For<IClientEngine>();
        engine.GetTorrents().Returns(torrents);

        var engineService = Substitute.For<ITorrentEngineService>();
        engineService.Engine.Returns(engine);

        return Handler(engineService);
    }

    private McpResourceHandler HandlerWithNoEngine()
    {
        var engineService = Substitute.For<ITorrentEngineService>();
        engineService.Engine.Returns(_ => throw new InvalidOperationException("Torrent engine is not initialized."));

        return Handler(engineService);
    }

    private static ITorrent Torrent()
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Hash.Returns(new InfoHash(Enumerable.Repeat((byte)0x11, InfoHash.V1Length).ToArray()));
        torrent.Name.Returns("A Torrent");
        torrent.State.Returns(TorrentState.Active);
        torrent.Peers.GetConnectedPeers().Returns([]);
        torrent.GetAllFileInfo().Returns([]);
        torrent.GetAllFileSelections().Returns([]);
        return torrent;
    }

    private static string ErrorCode(string json) =>
        JsonDocument.Parse(json).RootElement.GetProperty("Code").GetString() ?? string.Empty;

    [Fact]
    public async Task TheActiveTorrentList_IsJsonEvenWhenThereAreNone()
    {
        var json = await HandlerWith().GetActiveTorrentsAsync();

        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(json).RootElement.ValueKind);
    }

    [Fact]
    public async Task TheActiveTorrentList_NamesEachTorrentAndItsHash()
    {
        var json = await HandlerWith(Torrent()).GetActiveTorrentsAsync();

        var first = JsonDocument.Parse(json).RootElement.EnumerateArray().Single();
        Assert.Equal("A Torrent", first.GetProperty("Name").GetString());
        Assert.Equal(Hash, first.GetProperty("Hash").GetString());
    }

    [Fact]
    public async Task AnEngineThatIsNotUp_ProducesACodedErrorRatherThanThrowing()
    {
        // Asked before startup finishes, or after shutdown began. The pipe stays usable either way.
        var json = await HandlerWithNoEngine().GetActiveTorrentsAsync();

        Assert.Equal("active_torrents_failed", ErrorCode(json));
    }

    [Fact]
    public async Task TheRecentAlerts_StartEmptyAndAreStillJson()
    {
        var json = await HandlerWith().GetRecentAlertsAsync();

        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(json).RootElement.ValueKind);
        Assert.Empty(JsonDocument.Parse(json).RootElement.EnumerateArray());
    }

    [Fact]
    public async Task AskingForFilesWithAMalformedHash_IsRefusedBeforeTheEngineIsTouched()
    {
        var json = await HandlerWithNoEngine().GetTorrentFilesAsync("not a hash");

        Assert.Equal("invalid_info_hash", ErrorCode(json));
    }

    [Fact]
    public async Task AskingForTheFilesOfATorrentThatIsNotThere_SaysSo()
    {
        var json = await HandlerWith().GetTorrentFilesAsync(Hash);

        Assert.Equal("torrent_not_found", ErrorCode(json));
    }

    [Fact]
    public async Task TheFileList_IsJsonForATorrentThatIsThere()
    {
        var json = await HandlerWith(Torrent()).GetTorrentFilesAsync(Hash);

        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(json).RootElement.ValueKind);
    }

    [Fact]
    public async Task AskingForPeersWithAMalformedHash_IsRefusedBeforeTheEngineIsTouched()
    {
        var json = await HandlerWithNoEngine().GetTorrentPeersAsync("not a hash");

        Assert.Equal("invalid_info_hash", ErrorCode(json));
    }

    [Fact]
    public async Task AskingForThePeersOfATorrentThatIsNotThere_SaysSo()
    {
        var json = await HandlerWith().GetTorrentPeersAsync(Hash);

        Assert.Equal("torrent_not_found", ErrorCode(json));
    }

    [Fact]
    public async Task ThePeerList_IsJsonForATorrentThatIsThere()
    {
        var json = await HandlerWith(Torrent()).GetTorrentPeersAsync(Hash);

        Assert.Equal(JsonValueKind.Array, JsonDocument.Parse(json).RootElement.ValueKind);
    }

    public void Dispose()
    {
        // The handler registers for alerts on the shared messenger. Left undisposed, one test's
        // handler goes on collecting the next test's alerts.
        foreach (var handler in _handlers)
        {
            handler.Dispose();
        }
    }

    [Fact]
    public async Task AnAlert_IsRecordedWhileTheHandlerIsAlive()
    {
        var handler = HandlerWith();

        Send("Ubuntu ISO");

        var json = await handler.GetRecentAlertsAsync();
        Assert.Single(JsonDocument.Parse(json).RootElement.EnumerateArray());
    }

    [Fact]
    public async Task ADisposedHandler_StopsCollectingAlerts()
    {
        // It registers on the shared messenger, which holds it until it unregisters. Without this
        // a handler built for one request goes on recording every alert in the process for the
        // life of the application - and, in these tests, every alert the next test raises.
        var handler = HandlerWith();
        handler.Dispose();

        Send("Ubuntu ISO");

        var json = await handler.GetRecentAlertsAsync();
        Assert.Empty(JsonDocument.Parse(json).RootElement.EnumerateArray());
    }

    [Fact]
    public void DisposingTwice_IsSafe()
    {
        var handler = HandlerWith();

        handler.Dispose();
        handler.Dispose();
    }

    private static void Send(string torrentName)
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns(torrentName);
        torrent.Hash.Returns(new InfoHash(Enumerable.Repeat((byte)0x11, InfoHash.V1Length).ToArray()));

        WeakReferenceMessenger.Default.Send(
            new TorrentAlertMessage(
                torrent,
                new TorrentErrorAlert
                {
                    Id = AlertId.TorrentError,
                    Torrent = torrent,
                    Exception = new InvalidOperationException("disk full")
                }));
    }

}
