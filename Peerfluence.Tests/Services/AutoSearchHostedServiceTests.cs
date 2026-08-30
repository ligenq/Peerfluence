using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using PeerSharp.Config;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

/// <summary>
/// The saved query, run.
/// </summary>
public sealed class AutoSearchHostedServiceTests
{
    private static TorrentSearchResult Result(string link, string title = "A release") =>
        new(title, 1024, 10, 2, "indexer", null, link);

    private sealed record Harness(
        AutoSearchHostedService Service,
        AppSettings Settings,
        IAppSettingsService SettingsService,
        ITorrentSearchService Search,
        ITorrentService Torrents,
        ITorrentCategoryService Categories);

    private static Harness Build(
        bool enabled = true,
        string query = "a query",
        string category = "",
        bool configured = true,
        TorrentSearchResponse? response = null)
    {
        var settings = new AppSettings();
        settings.AutoSearch.Enabled = enabled;
        settings.AutoSearch.Query = query;
        settings.AutoSearch.Category = category;

        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(settings);

        var search = Substitute.For<ITorrentSearchService>();
        search.IsConfigured.Returns(configured);
        search.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response ?? TorrentSearchResponse.Succeeded([])));

        var torrent = Substitute.For<ITorrent>();
        torrent.Hash.Returns(new InfoHash(Enumerable.Repeat((byte)0x11, InfoHash.V1Length).ToArray()));
        torrent.HashV2.Returns(InfoHash.EmptyV2);

        var torrents = Substitute.For<ITorrentService>();
        torrents.AddMagnetAsync(Arg.Any<string>(), Arg.Any<AddTorrentOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(torrent));
        torrents.AddTorrentFromUrlAsync(Arg.Any<string>(), Arg.Any<AddTorrentOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(torrent));

        var categories = Substitute.For<ITorrentCategoryService>();

        return new Harness(
            new AutoSearchHostedService(settingsService, search, torrents, categories,
                NullLogger<AutoSearchHostedService>.Instance),
            settings, settingsService, search, torrents, categories);
    }

    [Fact]
    public async Task ANewResult_IsAddedAndRememberedAndSaved()
    {
        var h = Build(response: TorrentSearchResponse.Succeeded([Result("magnet:one")]));

        await h.Service.RunOnceAsync(TestContext.Current.CancellationToken);

        await h.Torrents.Received(1).AddMagnetAsync("magnet:one", Arg.Any<AddTorrentOptions?>(), Arg.Any<CancellationToken>());
        Assert.Contains("magnet:one", h.Settings.AutoSearch.AlreadyAdded);
        await h.SettingsService.Received(1).SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AResultItHasAlreadyAdded_IsNotAddedAgain()
    {
        // Otherwise the same release arrives once an hour, for ever.
        var h = Build(response: TorrentSearchResponse.Succeeded([Result("magnet:one")]));
        h.Settings.AutoSearch.AlreadyAdded.Add("magnet:one");

        await h.Service.RunOnceAsync(TestContext.Current.CancellationToken);

        await h.Torrents.DidNotReceive().AddMagnetAsync(Arg.Any<string>(), Arg.Any<AddTorrentOptions?>(), Arg.Any<CancellationToken>());
        await h.SettingsService.DidNotReceive().SaveAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ALinkThatIsNotAMagnet_IsFetchedAsATorrentFile()
    {
        var h = Build(response: TorrentSearchResponse.Succeeded([Result("https://indexer.invalid/a.torrent")]));

        await h.Service.RunOnceAsync(TestContext.Current.CancellationToken);

        await h.Torrents.Received(1).AddTorrentFromUrlAsync(
            "https://indexer.invalid/a.torrent", Arg.Any<AddTorrentOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AConfiguredCategory_IsAppliedToWhatIsAdded()
    {
        var h = Build(category: "Films", response: TorrentSearchResponse.Succeeded([Result("magnet:one")]));

        await h.Service.RunOnceAsync(TestContext.Current.CancellationToken);

        await h.Categories.Received(1).AssignAsync(Arg.Any<InfoHash>(), "Films", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFailedSearch_AddsNothing()
    {
        var h = Build(response: TorrentSearchResponse.Failed(SearchFailure.Unreachable));

        await h.Service.RunOnceAsync(TestContext.Current.CancellationToken);

        await h.Torrents.DidNotReceive().AddMagnetAsync(Arg.Any<string>(), Arg.Any<AddTorrentOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnAddThatFails_IsNotRemembered()
    {
        // So a result that failed for a passing reason is tried again next time.
        var h = Build(response: TorrentSearchResponse.Succeeded([Result("magnet:one")]));
        h.Torrents.AddMagnetAsync(Arg.Any<string>(), Arg.Any<AddTorrentOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ITorrent>>(_ => throw new InvalidOperationException("no"));

        await h.Service.RunOnceAsync(TestContext.Current.CancellationToken);

        Assert.Empty(h.Settings.AutoSearch.AlreadyAdded);
    }

    [Fact]
    public async Task TurnedOff_NothingIsSearchedFor()
    {
        var h = Build(enabled: false);

        await h.Service.RunOnceAsync(TestContext.Current.CancellationToken);

        await h.Search.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithNoIndexerConfigured_NothingIsSearchedFor()
    {
        var h = Build(configured: false);

        await h.Service.RunOnceAsync(TestContext.Current.CancellationToken);

        await h.Search.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartingAndStopping_RunsNothingInBetween()
    {
        // The first run is one interval away, and the shortest interval is a quarter of an hour, so
        // starting and stopping immediately must search for nothing at all.
        var h = Build();

        await h.Service.StartAsync(TestContext.Current.CancellationToken);
        await h.Service.StopAsync(TestContext.Current.CancellationToken);

        await h.Search.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
