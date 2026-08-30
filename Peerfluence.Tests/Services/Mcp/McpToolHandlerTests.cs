using Peerfluence.Services.Mcp;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using System.IO.Abstractions;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using PeerSharp.Core;
using PeerSharp.Interfaces;
using System.Net;

namespace Peerfluence.Tests.Services.Mcp;

public class McpToolHandlerTests
{
    [Fact]
    public async Task AddTorrentAsync_ReturnsError_WhenMagnetLinkIsEmpty()
    {
        // Arrange
        // We can pass null for TorrentService because it should fail fast on validation.
        var handler = new McpToolHandler(null!, null!, null!, null!);

        // Act
        var result = await handler.AddTorrentAsync(string.Empty, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Input cannot be empty", Text(result));
    }

    [Fact]
    public async Task AddTorrentAsync_ReturnsError_WhenMagnetLinkIsInvalid()
    {
        // Arrange
        var handler = new McpToolHandler(null!, null!, null!, null!);

        // Act
        var result = await handler.AddTorrentAsync("invalid_magnet_link", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Input is not a valid magnet link", Text(result));
    }

    [Fact]
    public async Task ManageTorrentAsync_ReturnsError_WhenInfoHashIsInvalid()
    {
        // Arrange
        var handler = new McpToolHandler(null!, null!, null!, null!);

        // Act
        var result = await handler.ManageTorrentAsync("not_a_hex_hash", "pause", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result.IsError);
        Assert.Contains("Invalid info hash format", Text(result));
    }

    [Fact]
    public async Task UpdateSettingsAsync_AppliesNestedSettingsPayload()
    {
        var store = Substitute.For<IAppSettingsStore>();
        var settingsService = new AppSettingsService(new AppPaths(), store, new FileSystem());
        settingsService.Current.Mcp.AllowDestructiveTools = true;
        var handler = new McpToolHandler(
            Substitute.For<ITorrentService>(),
            Substitute.For<ITopLevelService>(),
            settingsService,
            Substitute.For<IHostApplicationLifetime>());

        var downloadPath = Path.Combine(Path.GetTempPath(), $"peerfluence-mcp-{Guid.NewGuid():N}");
        var result = await handler.UpdateSettingsAsync($$"""
            {
              "storage": { "downloadPath": "{{downloadPath.Replace("\\", "\\\\", StringComparison.Ordinal)}}" },
              "network": { "enableDht": false },
              "update": { "updateUrl": "https://updates.example/feed" }
            }
            """, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("Settings updated successfully", Text(result));
        Assert.Equal(downloadPath, settingsService.Current.Storage.DownloadPath);
        Assert.False(settingsService.Current.Network.EnableDht);
        Assert.Equal("https://updates.example/feed", settingsService.Current.Update.UpdateUrl);
    }

    [Theory]
    [InlineData("not an address")]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    public async Task UpdateSettingsAsync_RejectsABindAddressThatWouldDisableTheKillSwitch(string invalidAddress)
    {
        var store = Substitute.For<IAppSettingsStore>();
        var settingsService = new AppSettingsService(new AppPaths(), store, new FileSystem());
        settingsService.Current.Mcp.AllowDestructiveTools = true;
        settingsService.Current.Network.BindAddress = "127.0.0.1";
        var handler = new McpToolHandler(
            Substitute.For<ITorrentService>(),
            Substitute.For<ITopLevelService>(),
            settingsService,
            Substitute.For<IHostApplicationLifetime>());

        var result = await handler.UpdateSettingsAsync(
            $$"""{ "network": { "bindAddress": "{{invalidAddress}}" } }""",
            TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("Bind address must be", Text(result));
        Assert.Equal("127.0.0.1", settingsService.Current.Network.BindAddress);
        await store.DidNotReceive().SaveAsync(Arg.Any<AppSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AddTorrentAsync_DeletesTemporaryTorrentFileAfterBase64Import()
    {
        var torrentService = Substitute.For<ITorrentService>();
        string? capturedPath = null;
        torrentService.AddTorrentFileAsync(Arg.Any<string>(), null, Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                capturedPath = ci.Arg<string>();
                Assert.True(File.Exists(capturedPath));
                return Task.FromResult(Substitute.For<PeerSharp.Interfaces.ITorrent>());
            });

        var handler = new McpToolHandler(
            torrentService,
            Substitute.For<ITopLevelService>(),
            CreateSettingsService(),
            Substitute.For<IHostApplicationLifetime>());

        var result = await handler.AddTorrentAsync(
            Convert.ToBase64String(new byte[] { 1, 2, 3, 4 }),
            TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("Successfully added torrent from base64 data", Text(result));
        Assert.NotNull(capturedPath);
        Assert.False(File.Exists(capturedPath));
    }

    [Fact]
    public async Task ManageTorrentAsync_AnAllZeroHash_RemovesNothing()
    {
        // Forty zero characters parse into a perfectly valid InfoHash, and a v2 only torrent stores
        // exactly that as its v1 hash because it does not have one. Resolving by comparing the
        // stored hash directly therefore answered this request with a real torrent, and "remove"
        // does what it says to whatever it is given.
        var v2Only = Substitute.For<ITorrent>();
        v2Only.Hash.Returns(InfoHash.Empty);
        v2Only.HashV2.Returns(new InfoHash(Enumerable.Repeat((byte)0x22, InfoHash.V2Length).ToArray()));
        v2Only.State.Returns(TorrentState.Active);

        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns([v2Only]);

        var handler = new McpToolHandler(
            torrentService,
            Substitute.For<ITopLevelService>(),
            CreateSettingsService(allowDestructiveTools: true),
            Substitute.For<IHostApplicationLifetime>());

        var result = await handler.ManageTorrentAsync(
            new string('0', InfoHash.V1Length * 2), "remove", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("not found", Text(result), StringComparison.OrdinalIgnoreCase);
        await torrentService.DidNotReceive().RemoveAsync(
            Arg.Any<ITorrent>(), Arg.Any<PeerSharp.Config.RemoveOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManageTorrentAsync_PauseResumeAndRemove_UseExpectedOperations()
    {
        var infoHash = new InfoHash(Enumerable.Repeat((byte)0x11, InfoHash.V1Length).ToArray());
        var torrent = Substitute.For<ITorrent>();
        torrent.Hash.Returns(infoHash);
        torrent.State.Returns(TorrentState.Active, TorrentState.Stopped);
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns([torrent]);

        var handler = new McpToolHandler(
            torrentService,
            Substitute.For<ITopLevelService>(),
            CreateSettingsService(allowDestructiveTools: true),
            Substitute.For<IHostApplicationLifetime>());

        var pauseResult = await handler.ManageTorrentAsync(infoHash.ToHexString(), "pause", TestContext.Current.CancellationToken);
        var resumeResult = await handler.ManageTorrentAsync(infoHash.ToHexString(), "resume", TestContext.Current.CancellationToken);
        var removeResult = await handler.ManageTorrentAsync(infoHash.ToHexString(), "remove", TestContext.Current.CancellationToken);

        Assert.Contains("Successfully paused", Text(pauseResult));
        Assert.Contains("Successfully resumed", Text(resumeResult));
        Assert.Contains("Successfully removed", Text(removeResult));
        await torrent.Received(1).StopAsync(Arg.Any<CancellationToken>());
        await torrent.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await torrentService.Received(1).RemoveAsync(torrent, PeerSharp.Config.RemoveOptions.None, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManageTorrentAsync_Remove_IsDenied_WhenDestructiveToolsAreDisabled()
    {
        var infoHash = new InfoHash(Enumerable.Repeat((byte)0x11, InfoHash.V1Length).ToArray());
        var torrent = Substitute.For<ITorrent>();
        torrent.Hash.Returns(infoHash);
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns([torrent]);

        var handler = new McpToolHandler(
            torrentService,
            Substitute.For<ITopLevelService>(),
            CreateSettingsService(),
            Substitute.For<IHostApplicationLifetime>());

        var result = await handler.ManageTorrentAsync(infoHash.ToHexString(), "remove", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("Destructive MCP tools are disabled", Text(result));
        await torrentService.DidNotReceive().RemoveAsync(Arg.Any<ITorrent>(), Arg.Any<PeerSharp.Config.RemoveOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManageTorrentAsync_Resume_ReturnsSuccess_WhenTorrentIsAlreadyActive()
    {
        var infoHash = new InfoHash(Enumerable.Repeat((byte)0x11, InfoHash.V1Length).ToArray());
        var torrent = Substitute.For<ITorrent>();
        torrent.Hash.Returns(infoHash);
        torrent.State.Returns(TorrentState.Active);
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns([torrent]);

        var handler = new McpToolHandler(
            torrentService,
            Substitute.For<ITopLevelService>(),
            CreateSettingsService(),
            Substitute.For<IHostApplicationLifetime>());

        var result = await handler.ManageTorrentAsync(infoHash.ToHexString(), "resume", TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("already active", Text(result));
        await torrent.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ManageTorrentAsync_Pause_ReturnsSuccess_WhenTorrentIsAlreadyStopped()
    {
        var infoHash = new InfoHash(Enumerable.Repeat((byte)0x11, InfoHash.V1Length).ToArray());
        var torrent = Substitute.For<ITorrent>();
        torrent.Hash.Returns(infoHash);
        torrent.State.Returns(TorrentState.Stopped);
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns([torrent]);

        var handler = new McpToolHandler(
            torrentService,
            Substitute.For<ITopLevelService>(),
            CreateSettingsService(),
            Substitute.For<IHostApplicationLifetime>());

        var result = await handler.ManageTorrentAsync(infoHash.ToHexString(), "pause", TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        Assert.Contains("already paused", Text(result));
        await torrent.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ShutdownApplicationAsync_StopsHost()
    {
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        var handler = new McpToolHandler(
            Substitute.For<ITorrentService>(),
            Substitute.For<ITopLevelService>(),
            CreateSettingsService(allowDestructiveTools: true),
            lifetime);

        var result = await handler.ShutdownApplicationAsync();

        Assert.False(result.IsError);
        Assert.Contains("Application shutdown requested", Text(result));
        lifetime.Received(1).StopApplication();
    }

    [Fact]
    public async Task SetFilePriorityAsync_UpdatesTorrentPriority_WhenInputsAreValid()
    {
        var infoHash = new InfoHash(Enumerable.Repeat((byte)0x11, InfoHash.V1Length).ToArray());
        var torrent = Substitute.For<ITorrent>();
        torrent.Hash.Returns(infoHash);
        torrent.FileCount.Returns(3);

        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns([torrent]);

        var handler = new McpToolHandler(
            torrentService,
            Substitute.For<ITopLevelService>(),
            CreateSettingsService(),
            Substitute.For<IHostApplicationLifetime>());

        var result = await handler.SetFilePriorityAsync(infoHash.ToHexString(), 1, "high", TestContext.Current.CancellationToken);

        Assert.Contains("Successfully set priority", Text(result));
        await torrent.Received(1).SetFilePriorityAsync(1, Priority.High, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTorrentDiagnosticsAsync_ReturnsSerializedDiagnostics()
    {
        var infoHash = new InfoHash(Enumerable.Repeat((byte)0x11, InfoHash.V1Length).ToArray());
        var tracker = new TrackerStatus("udp://tracker.example", TrackerStatusType.Working);

        var peers = new[]
        {
            new PeerInfo(
                new IPEndPoint(IPAddress.Loopback, 6881),
                ClientName: "Peer",
                DownloadSpeed: 10,
                UploadSpeed: 5,
                Progress: 0.5f)
        };

        var peerCollection = Substitute.For<IPeers>();
        peerCollection.GetConnectedPeers().Returns(peers);
        peerCollection.GetPieceAvailability().Returns([0, 1, 1]);

        var trackers = Substitute.For<ITrackers>();
        trackers.GetTrackers().Returns([tracker]);

        var torrent = Substitute.For<ITorrent>();
        torrent.Hash.Returns(infoHash);
        torrent.Name.Returns("Ubuntu");
        torrent.State.Returns(TorrentState.Active);
        torrent.PieceCount.Returns(3);
        torrent.Peers.Returns(peerCollection);
        torrent.Trackers.Returns(trackers);

        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns([torrent]);

        var handler = new McpToolHandler(
            torrentService,
            Substitute.For<ITopLevelService>(),
            CreateSettingsService(),
            Substitute.For<IHostApplicationLifetime>());

        var result = await handler.GetTorrentDiagnosticsAsync(infoHash.ToHexString());

        Assert.Contains("\"Name\":\"Ubuntu\"", Text(result));
        Assert.Contains("\"MissingPieces\":1", Text(result));
        Assert.Contains("udp://tracker.example", Text(result));
    }

    private static IAppSettingsService CreateSettingsService(bool allowDestructiveTools = false)
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(new AppSettings
        {
            Mcp =
            {
                Enabled = true,
                AllowDestructiveTools = allowDestructiveTools
            }
        });
        return settingsService;
    }

    [Fact]
    public async Task TakeScreenshotAsync_ReturnsTheImageTheWindowRendered()
    {
        var png = new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' };
        var topLevelService = Substitute.For<ITopLevelService>();
        topLevelService.IsWindowAvailable.Returns(true);
        topLevelService.CaptureWindowPngAsync(Arg.Any<CancellationToken>()).Returns(png);

        var handler = new McpToolHandler(null!, topLevelService, null!, null!);

        var result = await handler.TakeScreenshotAsync(TestContext.Current.CancellationToken);

        var image = Assert.IsType<ImageContentBlock>(Assert.Single(result.Content));
        Assert.Equal("image/png", image.MimeType);
    }

    [Fact]
    public async Task TakeScreenshotAsync_ReturnsError_WhenThereIsNoWindow()
    {
        var topLevelService = Substitute.For<ITopLevelService>();
        topLevelService.IsWindowAvailable.Returns(false);

        var handler = new McpToolHandler(null!, topLevelService, null!, null!);

        var result = await handler.TakeScreenshotAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("UI window not available", Text(result));
        await topLevelService.DidNotReceive().CaptureWindowPngAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TakeScreenshotAsync_ReturnsError_WhenTheWindowHasNothingToRender()
    {
        var topLevelService = Substitute.For<ITopLevelService>();
        topLevelService.IsWindowAvailable.Returns(true);
        topLevelService.CaptureWindowPngAsync(Arg.Any<CancellationToken>()).Returns((byte[]?)null);

        var handler = new McpToolHandler(null!, topLevelService, null!, null!);

        var result = await handler.TakeScreenshotAsync(TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("Invalid window dimensions", Text(result));
    }

    private static string Text(CallToolResult result)
    {
        return Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
    }
}
