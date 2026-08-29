using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.Services.Mcp;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services.Mcp;

/// <summary>
/// The tools that reach the parts of PeerSharp 4.0 the desktop interface also exposes.
/// </summary>
/// <remarks>
/// Two things are being pinned down: that an agent gets a usable answer rather than an exception
/// when it names something that is not there, and that the operations which move data on disk stay
/// behind both the destructive-tools switch and the engine's own "must be stopped" rule.
/// </remarks>
public sealed class McpToolHandlerPeerSharpToolsTests
{
    private static readonly string Hash = new InfoHash(new byte[20]).ToHexString();

    private static IAppSettingsService Settings(bool allowDestructiveTools = false)
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(new AppSettings
        {
            Mcp = { AllowDestructiveTools = allowDestructiveTools }
        });
        return settingsService;
    }

    private static (McpToolHandler Handler, ITorrent Torrent) HandlerWithTorrent(
        bool allowDestructiveTools = false,
        TorrentState state = TorrentState.Stopped)
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Hash.Returns(new InfoHash(new byte[20]));
        torrent.State.Returns(state);

        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns([torrent]);

        var handler = new McpToolHandler(
            torrentService,
            Substitute.For<ITopLevelService>(),
            Settings(allowDestructiveTools),
            Substitute.For<IHostApplicationLifetime>());

        return (handler, torrent);
    }

    private static McpToolHandler HandlerWithNoTorrents()
    {
        var torrentService = Substitute.For<ITorrentService>();
        torrentService.GetTorrents().Returns([]);

        return new McpToolHandler(
            torrentService,
            Substitute.For<ITopLevelService>(),
            Settings(allowDestructiveTools: true),
            Substitute.For<IHostApplicationLifetime>());
    }

    private static string Text(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    // ------------------------------------------------------------------- configure_torrent --

    [Fact]
    public async Task ConfiguringATorrent_AppliesOnlyWhatWasNamed()
    {
        var (handler, torrent) = HandlerWithTorrent();

        var result = await handler.ConfigureTorrentAsync(Hash, superSeeding: true, maxUploadSlots: 6);

        Assert.False(result.IsError);
        Assert.True(torrent.SuperSeeding);
        Assert.Equal(6, torrent.MaxUploadSlots);

        // Left alone rather than reset to zero, which is what an omitted value has to mean if the
        // tool is to be usable for changing one setting.
        Assert.Equal(0, torrent.MaxConnections);
    }

    [Fact]
    public async Task ConfiguringNothing_SaysSoRatherThanClaimingAChange()
    {
        var (handler, _) = HandlerWithTorrent();

        var result = await handler.ConfigureTorrentAsync(Hash);

        Assert.False(result.IsError);
        Assert.Contains("Nothing to change", Text(result));
    }

    [Fact]
    public async Task ANegativeLimit_IsRefusedBeforeTheEngineThrows()
    {
        // The engine rejects a negative with ArgumentOutOfRangeException. Caught here so the agent
        // gets a coded refusal rather than a stack trace.
        var (handler, torrent) = HandlerWithTorrent();

        var result = await handler.ConfigureTorrentAsync(Hash, maxConnections: -1);

        Assert.True(result.IsError);
        Assert.Contains("invalid_limit", Text(result));
        Assert.Equal(0, torrent.MaxConnections);
    }

    [Fact]
    public async Task ConfiguringATorrentThatIsNotThere_IsAnError()
    {
        var result = await HandlerWithNoTorrents().ConfigureTorrentAsync(Hash, superSeeding: true);

        Assert.True(result.IsError);
        Assert.Contains("torrent_not_found", Text(result));
    }

    [Fact]
    public async Task AMalformedHash_IsAnErrorRatherThanAMiss()
    {
        var result = await HandlerWithNoTorrents().ConfigureTorrentAsync("not a hash", superSeeding: true);

        Assert.True(result.IsError);
        Assert.Contains("invalid_info_hash", Text(result));
    }

    // ------------------------------------------------------------------ manage_web_seeds --

    [Fact]
    public async Task AddingAWebSeed_ReturnsTheListItProduced()
    {
        var (handler, torrent) = HandlerWithTorrent();
        torrent.WebSeeds.Add("https://mirror.invalid/files/").Returns(true);
        torrent.WebSeeds.GetAll().Returns(["https://mirror.invalid/files/"]);

        var result = await handler.ManageWebSeedsAsync(Hash, "add", "https://mirror.invalid/files/");

        Assert.False(result.IsError);
        Assert.Contains("https://mirror.invalid/files/", Text(result));
    }

    [Fact]
    public async Task AWebSeedTheEngineRefuses_IsReportedRatherThanThrown()
    {
        // The engine reports a bad or duplicate URL with false, because a web seed list is usually
        // pasted from somewhere the user does not control.
        var (handler, torrent) = HandlerWithTorrent();
        torrent.WebSeeds.Add(Arg.Any<string>()).Returns(false);

        var result = await handler.ManageWebSeedsAsync(Hash, "add", "not a url");

        Assert.True(result.IsError);
        Assert.Contains("web_seed_rejected", Text(result));
    }

    [Fact]
    public async Task AddingAWebSeedWithNoUrl_IsRefused()
    {
        var (handler, _) = HandlerWithTorrent();

        var result = await handler.ManageWebSeedsAsync(Hash, "add");

        Assert.True(result.IsError);
        Assert.Contains("missing_url", Text(result));
    }

    [Fact]
    public async Task RemovingAWebSeedThatWasNotThere_IsReported()
    {
        var (handler, torrent) = HandlerWithTorrent();
        torrent.WebSeeds.Remove(Arg.Any<string>()).Returns(false);

        var result = await handler.ManageWebSeedsAsync(Hash, "remove", "https://mirror.invalid/");

        Assert.True(result.IsError);
        Assert.Contains("web_seed_not_found", Text(result));
    }

    [Fact]
    public async Task ListingWebSeeds_ChangesNothing()
    {
        var (handler, torrent) = HandlerWithTorrent();
        torrent.WebSeeds.GetAll().Returns(["https://mirror.invalid/files/"]);

        var result = await handler.ManageWebSeedsAsync(Hash, "list");

        Assert.False(result.IsError);
        torrent.WebSeeds.DidNotReceive().Add(Arg.Any<string>());
        torrent.WebSeeds.DidNotReceive().Remove(Arg.Any<string>());
    }

    [Fact]
    public async Task AnUnknownWebSeedAction_IsRefused()
    {
        var (handler, _) = HandlerWithTorrent();

        var result = await handler.ManageWebSeedsAsync(Hash, "delete-everything");

        Assert.True(result.IsError);
        Assert.Contains("unknown_action", Text(result));
    }

    // ------------------------------------------------------------------- scrape_trackers --

    [Fact]
    public async Task ScrapingTrackers_AsksThemAndReturnsTheirCounts()
    {
        var (handler, torrent) = HandlerWithTorrent();
        torrent.Trackers.GetTrackers().Returns([
            new TrackerStatus("https://tracker.invalid/announce", TrackerStatusType.Working, SeedCount: 9, LeechCount: 3)
        ]);

        var result = await handler.ScrapeTrackersAsync(Hash, TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        await torrent.Trackers.Received(1).ScrapeAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
        Assert.Contains("tracker.invalid", Text(result));
        Assert.Contains("9", Text(result));
    }

    // -------------------------------------------------- rename_torrent_file / move_storage --

    [Fact]
    public async Task RenamingAFile_IsDeniedWhenDestructiveToolsAreOff()
    {
        var (handler, torrent) = HandlerWithTorrent(allowDestructiveTools: false);

        var result = await handler.RenameTorrentFileAsync(Hash, 0, "renamed.bin", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("destructive_tools_disabled", Text(result));
        await torrent.DidNotReceive().RenameFileAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenamingAFileOnARunningTorrent_IsRefusedBeforeTheEngineIsAsked()
    {
        // The engine requires the torrent be stopped. Refused here so the answer names the reason
        // rather than surfacing an InvalidOperationException.
        var (handler, torrent) = HandlerWithTorrent(allowDestructiveTools: true, state: TorrentState.Active);

        var result = await handler.RenameTorrentFileAsync(Hash, 0, "renamed.bin", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("torrent_running", Text(result));
        await torrent.DidNotReceive().RenameFileAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenamingAFile_PassesTheNewNameThrough()
    {
        var (handler, torrent) = HandlerWithTorrent(allowDestructiveTools: true);

        var result = await handler.RenameTorrentFileAsync(Hash, 2, "folder/renamed.bin", TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        await torrent.Received(1).RenameFileAsync(2, "folder/renamed.bin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task APathTheEngineRejects_ComesBackAsAnInvalidPath()
    {
        // ArgumentException is what an absolute path or one climbing out with ".." produces.
        var (handler, torrent) = HandlerWithTorrent(allowDestructiveTools: true);
        torrent.RenameFileAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new ArgumentException("escapes the download path"));

        var result = await handler.RenameTorrentFileAsync(Hash, 0, "../escape.bin", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("invalid_path", Text(result));
    }

    [Fact]
    public async Task MovingStorage_IsDeniedWhenDestructiveToolsAreOff()
    {
        var (handler, torrent) = HandlerWithTorrent(allowDestructiveTools: false);

        var result = await handler.MoveTorrentStorageAsync(Hash, @"D:\elsewhere", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("destructive_tools_disabled", Text(result));
        await torrent.DidNotReceive().MoveStorageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MovingStorageOnARunningTorrent_IsRefused()
    {
        var (handler, torrent) = HandlerWithTorrent(allowDestructiveTools: true, state: TorrentState.Active);

        var result = await handler.MoveTorrentStorageAsync(Hash, @"D:\elsewhere", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("torrent_running", Text(result));
        await torrent.DidNotReceive().MoveStorageAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MovingStorage_TakesTheDataToTheNewPath()
    {
        var (handler, torrent) = HandlerWithTorrent(allowDestructiveTools: true);

        var result = await handler.MoveTorrentStorageAsync(Hash, @"D:\elsewhere", TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        await torrent.Received(1).MoveStorageAsync(@"D:\elsewhere", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MovingStorageWithNoDestination_IsRefused()
    {
        var (handler, _) = HandlerWithTorrent(allowDestructiveTools: true);

        var result = await handler.MoveTorrentStorageAsync(Hash, "  ", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("missing_path", Text(result));
    }

    // ------------------------------------------------------------------ invoke_ui_action --

    [Fact]
    public async Task PausingEverything_UsesTheEnginesOwnPauseRatherThanALoop()
    {
        // The loop this replaced had no way to remember which torrents were running, so its
        // resume_all started everything - including what the user had stopped by hand.
        var torrentService = Substitute.For<ITorrentService>();
        var handler = new McpToolHandler(
            torrentService,
            Substitute.For<ITopLevelService>(),
            Settings(),
            Substitute.For<IHostApplicationLifetime>());

        await handler.InvokeUiActionAsync("pause_all", TestContext.Current.CancellationToken);
        await handler.InvokeUiActionAsync("resume_all", TestContext.Current.CancellationToken);

        await torrentService.Received(1).PauseSessionAsync(Arg.Any<CancellationToken>());
        await torrentService.Received(1).ResumeSessionAsync(Arg.Any<CancellationToken>());
        torrentService.DidNotReceive().GetTorrents();
    }

    [Fact]
    public async Task AnUnknownUiAction_IsRefused()
    {
        var handler = new McpToolHandler(
            Substitute.For<ITorrentService>(),
            Substitute.For<ITopLevelService>(),
            Settings(),
            Substitute.For<IHostApplicationLifetime>());

        var result = await handler.InvokeUiActionAsync("format_disk", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Contains("unknown_action", Text(result));
    }
}
