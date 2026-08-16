using System.Text.Json;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.Core.Services.Rpc;
using PeerSharp.Config;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

/// <summary>
/// Asserts on the JSON an automation client would actually receive. The handler is string in, string
/// out precisely so these can be written without a socket, and so what they check is the protocol
/// rather than an internal shape that happens to serialize to it.
/// </summary>
public sealed class TransmissionRpcHandlerTests
{
    private static readonly InfoHash Hash = InfoHash.FromHex("AAAA1111BBBB2222CCCC3333DDDD4444EEEE5555");

    private readonly ITorrentService _torrentService = Substitute.For<ITorrentService>();
    private readonly IAppSettingsService _settingsService = Substitute.For<IAppSettingsService>();
    private readonly ITorrentTransferSnapshots _snapshots = Substitute.For<ITorrentTransferSnapshots>();
    private readonly ITorrentCategoryService _categoryService = Substitute.For<ITorrentCategoryService>();
    private readonly AppSettings _settings = new();

    public TransmissionRpcHandlerTests()
    {
        _settingsService.Current.Returns(_settings);
        _torrentService.GetTorrents().Returns([]);
    }

    [Fact]
    public async Task SessionGet_ReportsAVersionClientsWillTalkTo()
    {
        var response = await CallAsync("""{"method":"session-get","tag":7}""");

        Assert.Equal("success", response.GetProperty("result").GetString());
        Assert.Equal(7, response.GetProperty("tag").GetInt32());

        var arguments = response.GetProperty("arguments");
        Assert.True(arguments.GetProperty("rpc-version").GetInt32() >= 14);
        Assert.False(string.IsNullOrWhiteSpace(arguments.GetProperty("version").GetString()));
    }

    /// <summary>
    /// The tag is how a client matches an answer to the question it asked, so it has to come back
    /// even when the answer is a failure.
    /// </summary>
    [Fact]
    public async Task AFailure_StillCarriesTheTagItWasAskedWith()
    {
        var response = await CallAsync("""{"method":"nonsense-method","tag":42}""");

        Assert.NotEqual("success", response.GetProperty("result").GetString());
        Assert.Equal(42, response.GetProperty("tag").GetInt32());
    }

    [Fact]
    public async Task MalformedJson_IsAnswered_NotThrown()
    {
        var response = await CallAsync("{ this is not json");

        Assert.Equal("invalid json", response.GetProperty("result").GetString());
    }

    [Fact]
    public async Task AMethodThatThrows_ComesBackAsAFailureResult()
    {
        _torrentService.AddMagnetAsync(Arg.Any<string>(), Arg.Any<AddTorrentOptions>(), Arg.Any<CancellationToken>())
            .Returns<Task<ITorrent>>(_ => throw new InvalidOperationException("engine is not running"));

        var response = await CallAsync("""{"method":"torrent-add","arguments":{"filename":"magnet:?xt=urn:btih:abc"}}""");

        // A dropped connection would leave the client guessing; a result it can read is the contract.
        Assert.Contains("engine is not running", response.GetProperty("result").GetString());
    }

    [Fact]
    public async Task TorrentGet_ReturnsOnlyTheFieldsAskedFor()
    {
        var torrent = Torrent();
        _torrentService.GetTorrents().Returns([torrent]);

        var response = await CallAsync("""{"method":"torrent-get","arguments":{"fields":["id","name","hashString"]}}""");

        var row = response.GetProperty("arguments").GetProperty("torrents")[0];
        Assert.Equal(["id", "name", "hashString"], row.EnumerateObject().Select(p => p.Name));
        Assert.Equal("ubuntu.iso", row.GetProperty("name").GetString());
    }

    /// <summary>
    /// Clients ask for supersets of what any one server implements. Refusing the whole call over one
    /// unknown name would break every one of them.
    /// </summary>
    [Fact]
    public async Task TorrentGet_SkipsFieldsItDoesNotKnow()
    {
        var torrent = Torrent();
        _torrentService.GetTorrents().Returns([torrent]);

        var response = await CallAsync("""{"method":"torrent-get","arguments":{"fields":["name","somethingInvented"]}}""");

        Assert.Equal("success", response.GetProperty("result").GetString());
        var row = response.GetProperty("arguments").GetProperty("torrents")[0];
        Assert.Equal(["name"], row.EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task TorrentGet_WithoutFields_IsRefused()
    {
        var response = await CallAsync("""{"method":"torrent-get","arguments":{}}""");

        Assert.Equal("no fields specified", response.GetProperty("result").GetString());
    }

    /// <summary>
    /// Transmission's status numbers are what clients branch on, so the mapping is the contract.
    /// </summary>
    [Theory]
    [InlineData(false, TorrentState.Stopped, false, 0)]
    [InlineData(true, TorrentState.CheckingFiles, false, 2)]
    [InlineData(true, TorrentState.Active, false, 4)]
    [InlineData(true, TorrentState.Active, true, 6)]
    public async Task Status_IsReportedInTransmissionsNumbers(bool started, TorrentState state, bool finished, int expected)
    {
        var torrent = Torrent(started: started, state: state, finished: finished);
        _torrentService.GetTorrents().Returns([torrent]);

        var response = await CallAsync("""{"method":"torrent-get","arguments":{"fields":["status"]}}""");

        Assert.Equal(expected, response.GetProperty("arguments").GetProperty("torrents")[0].GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Ids_SelectByHashString()
    {
        var wanted = Torrent();
        var other = Torrent(hash: InfoHash.FromHex("FFFF6666AAAA7777BBBB8888CCCC9999DDDD0000"), name: "other");
        _torrentService.GetTorrents().Returns([wanted, other]);

        var response = await CallAsync(
            """{"method":"torrent-get","arguments":{"fields":["name"],"ids":["HASH"]}}"""
                .Replace("HASH", Hash.ToHexString()));

        var torrents = response.GetProperty("arguments").GetProperty("torrents");
        Assert.Equal(1, torrents.GetArrayLength());
        Assert.Equal("ubuntu.iso", torrents[0].GetProperty("name").GetString());
    }

    /// <summary>
    /// The protocol's rule, and the reason torrent-get with no ids is the ordinary polling call.
    /// </summary>
    [Fact]
    public async Task NoIds_MeansEveryTorrent()
    {
        var first = Torrent();
        var second = Torrent(name: "second");
        _torrentService.GetTorrents().Returns([first, second]);

        var response = await CallAsync("""{"method":"torrent-get","arguments":{"fields":["name"]}}""");

        Assert.Equal(2, response.GetProperty("arguments").GetProperty("torrents").GetArrayLength());
    }

    [Fact]
    public async Task TorrentAdd_TakesAMagnetAndReportsWhatItAdded()
    {
        var added = Torrent();
        _torrentService.AddMagnetAsync(Arg.Any<string>(), Arg.Any<AddTorrentOptions>(), Arg.Any<CancellationToken>())
            .Returns(added);

        var response = await CallAsync(
            """{"method":"torrent-add","arguments":{"filename":"magnet:?xt=urn:btih:abc","download-dir":"D:\\Films","paused":true}}""");

        var reported = response.GetProperty("arguments").GetProperty("torrent-added");
        Assert.Equal("ubuntu.iso", reported.GetProperty("name").GetString());
        Assert.Equal(Hash.ToHexString(), reported.GetProperty("hashString").GetString());

        await _torrentService.Received(1).AddMagnetAsync(
            "magnet:?xt=urn:btih:abc",
            Arg.Is<AddTorrentOptions>(o => o.DownloadPath == @"D:\Films" && !o.StartImmediately),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TorrentAdd_TakesABase64Torrent()
    {
        var response = await CallAsync("""{"method":"torrent-add","arguments":{"metainfo":"not base64!!"}}""");

        Assert.Equal("metainfo is not valid base64", response.GetProperty("result").GetString());
    }

    [Fact]
    public async Task TorrentAdd_WithNothingToAdd_IsRefused()
    {
        var response = await CallAsync("""{"method":"torrent-add","arguments":{}}""");

        Assert.Equal("no metainfo or filename", response.GetProperty("result").GetString());
    }

    /// <summary>
    /// Labels are how the automation tools tell their own downloads from everyone else's, and
    /// categories are the nearest thing this application has to them.
    /// </summary>
    [Fact]
    public async Task ALabelOnAdd_BecomesACategory()
    {
        var added = Torrent();
        _torrentService.AddMagnetAsync(Arg.Any<string>(), Arg.Any<AddTorrentOptions>(), Arg.Any<CancellationToken>())
            .Returns(added);

        await CallAsync("""{"method":"torrent-add","arguments":{"filename":"magnet:?xt=urn:btih:abc","labels":["tv-sonarr"]}}""");

        await _categoryService.Received(1).AssignAsync(Hash, "tv-sonarr", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ACategory_IsReportedBackAsALabel()
    {
        var torrent = Torrent();
        _torrentService.GetTorrents().Returns([torrent]);
        _categoryService.GetCategory(Hash).Returns("tv-sonarr");

        var response = await CallAsync("""{"method":"torrent-get","arguments":{"fields":["labels"]}}""");

        var labels = response.GetProperty("arguments").GetProperty("torrents")[0].GetProperty("labels");
        Assert.Equal("tv-sonarr", labels[0].GetString());
    }

    [Fact]
    public async Task TorrentRemove_PassesOnWhetherToDeleteTheData()
    {
        var torrent = Torrent();
        _torrentService.GetTorrents().Returns([torrent]);

        await CallAsync("""{"method":"torrent-remove","arguments":{"delete-local-data":true}}""");

        await _torrentService.Received(1).RemoveAsync(torrent, RemoveOptions.DeleteFiles, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TorrentRemove_KeepsTheDataByDefault()
    {
        var torrent = Torrent();
        _torrentService.GetTorrents().Returns([torrent]);

        await CallAsync("""{"method":"torrent-remove","arguments":{}}""");

        await _torrentService.Received(1).RemoveAsync(torrent, RemoveOptions.None, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartAndStop_ReachTheTorrent()
    {
        var torrent = Torrent();
        _torrentService.GetTorrents().Returns([torrent]);

        await CallAsync("""{"method":"torrent-start-now","arguments":{}}""");
        await CallAsync("""{"method":"torrent-stop","arguments":{}}""");

        await torrent.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await torrent.Received(1).StopAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The engine cannot move a torrent's data. Reporting success would have the client believe files
    /// are somewhere they are not, which is worse than saying no.
    /// </summary>
    [Fact]
    public async Task MovingATorrent_IsRefusedRatherThanSilentlyIgnored()
    {
        var response = await CallAsync("""{"method":"torrent-set-location","arguments":{"location":"D:\\Elsewhere"}}""");

        Assert.NotEqual("success", response.GetProperty("result").GetString());
    }

    [Fact]
    public async Task Rates_ComeFromTheLastFiguresSeen()
    {
        var torrent = Torrent();
        _torrentService.GetTorrents().Returns([torrent]);
        _snapshots.Get(Hash).Returns(new TorrentTransferSnapshot(1024, 512, 4096, 2048, 7));

        var response = await CallAsync(
            """{"method":"torrent-get","arguments":{"fields":["rateDownload","rateUpload","peersConnected","eta"]}}""");

        var row = response.GetProperty("arguments").GetProperty("torrents")[0];
        Assert.Equal(1024, row.GetProperty("rateDownload").GetInt64());
        Assert.Equal(512, row.GetProperty("rateUpload").GetInt64());
        Assert.Equal(7, row.GetProperty("peersConnected").GetInt32());
        // 2048 bytes left at 1024 a second.
        Assert.Equal(2, row.GetProperty("eta").GetInt32());
    }

    [Fact]
    public async Task AnEtaThatCannotBeKnown_IsMinusOne()
    {
        var torrent = Torrent();
        _torrentService.GetTorrents().Returns([torrent]);
        _snapshots.Get(Hash).Returns(default(TorrentTransferSnapshot));

        var response = await CallAsync("""{"method":"torrent-get","arguments":{"fields":["eta"]}}""");

        Assert.Equal(-1, response.GetProperty("arguments").GetProperty("torrents")[0].GetProperty("eta").GetInt32());
    }


    // ---------------------------------------------------------------------------------------------
    // Everything below was added because mutation testing reported it unreached. session-stats and
    // free-space had no test at all; most torrent-get fields were never asked for, so the code that
    // writes them never ran; and torrent-set, integer ids and the non-magnet add branches were in
    // the same position.
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task SessionStats_AddsUpWhatTheTorrentsAreDoing()
    {
        var running = Torrent();
        var stopped = Torrent(name: "stopped", started: false, state: TorrentState.Stopped);
        _torrentService.GetTorrents().Returns([running, stopped]);
        _snapshots.Get(Hash).Returns(new TorrentTransferSnapshot(1000, 250, 0, 0, 3));

        var response = await CallAsync("""{"method":"session-stats"}""");

        var arguments = response.GetProperty("arguments");
        Assert.Equal(2, arguments.GetProperty("torrentCount").GetInt32());
        Assert.Equal(1, arguments.GetProperty("activeTorrentCount").GetInt32());
        Assert.Equal(1, arguments.GetProperty("pausedTorrentCount").GetInt32());
        // Both fixtures carry the same hash, so both read the same snapshot.
        Assert.Equal(2000, arguments.GetProperty("downloadSpeed").GetInt64());
        Assert.Equal(500, arguments.GetProperty("uploadSpeed").GetInt64());
    }

    /// <summary>
    /// Clients check there is room before sending something large, so this has to answer with the
    /// real figure rather than a guess they would ignore.
    /// </summary>
    [Fact]
    public async Task FreeSpace_AnswersForARealPath()
    {
        var response = await CallAsync(
            """{"method":"free-space","arguments":{"path":"PATH"}}"""
                .Replace("PATH", Path.GetTempPath().Replace(@"\", @"\\")));

        var arguments = response.GetProperty("arguments");
        Assert.True(arguments.GetProperty("size-bytes").GetInt64() > 0);
        Assert.False(string.IsNullOrEmpty(arguments.GetProperty("path").GetString()));
    }

    [Fact]
    public async Task FreeSpace_ForSomewhereThatCannotBeMeasured_IsMinusOne()
    {
        var response = await CallAsync("""{"method":"free-space","arguments":{"path":"::not a path"}}""");

        Assert.Equal(-1, response.GetProperty("arguments").GetProperty("size-bytes").GetInt64());
    }

    /// <summary>
    /// One test asking for everything supported, because a field is only written when it is asked
    /// for - so a field nobody requests is a branch nobody runs.
    /// </summary>
    [Fact]
    public async Task EverySupportedField_IsWrittenWhenAskedFor()
    {
        var torrent = Torrent(finished: true);
        torrent.RatioLimit.Returns(1.5f);
        torrent.LastException.Returns(new InvalidOperationException("disk full"));
        _torrentService.GetTorrents().Returns([torrent]);
        _snapshots.Get(Hash).Returns(new TorrentTransferSnapshot(10, 20, 30, 40, 5));

        var response = await CallAsync("""
            {"method":"torrent-get","arguments":{"fields":[
              "id","hashString","name","status","totalSize","sizeWhenDone","leftUntilDone",
              "percentDone","isFinished","downloadDir","rateDownload","rateUpload","downloadedEver",
              "uploadedEver","peersConnected","eta","errorString","error","fileCount","addedDate",
              "isPrivate","labels","seedRatioLimit","seedRatioMode"]}}
            """);

        var row = response.GetProperty("arguments").GetProperty("torrents")[0];
        Assert.Equal(4096, row.GetProperty("totalSize").GetInt64());
        Assert.Equal(4096, row.GetProperty("sizeWhenDone").GetInt64());
        Assert.Equal(2048, row.GetProperty("leftUntilDone").GetInt64());
        Assert.Equal(0.5, row.GetProperty("percentDone").GetDouble(), 3);
        Assert.True(row.GetProperty("isFinished").GetBoolean());
        Assert.Equal(30, row.GetProperty("downloadedEver").GetInt64());
        Assert.Equal(40, row.GetProperty("uploadedEver").GetInt64());
        Assert.Equal("disk full", row.GetProperty("errorString").GetString());
        Assert.Equal(3, row.GetProperty("error").GetInt32());
        Assert.Equal(1, row.GetProperty("fileCount").GetInt32());
        Assert.Equal(0, row.GetProperty("addedDate").GetInt64());
        Assert.False(row.GetProperty("isPrivate").GetBoolean());
        Assert.Equal(1.5, row.GetProperty("seedRatioLimit").GetDouble(), 3);
        Assert.Equal(1, row.GetProperty("seedRatioMode").GetInt32());
        // Finished, so there is nothing left to wait for.
        Assert.Equal(0, row.GetProperty("eta").GetInt32());
    }

    [Fact]
    public async Task NoRatioLimit_IsReportedAsTheModeBeingOff()
    {
        var torrent = Torrent();
        torrent.RatioLimit.Returns((float?)null);
        _torrentService.GetTorrents().Returns([torrent]);

        var response = await CallAsync("""{"method":"torrent-get","arguments":{"fields":["seedRatioLimit","seedRatioMode"]}}""");

        var row = response.GetProperty("arguments").GetProperty("torrents")[0];
        Assert.Equal(0, row.GetProperty("seedRatioLimit").GetDouble());
        Assert.Equal(0, row.GetProperty("seedRatioMode").GetInt32());
    }

    /// <summary>
    /// Clients remember the integer between calls, so selecting by it has to work as well as
    /// selecting by hash does.
    /// </summary>
    [Fact]
    public async Task Ids_SelectByTheIntegerTheClientWasGiven()
    {
        var wanted = Torrent();
        var other = Torrent(hash: InfoHash.FromHex("FFFF6666AAAA7777BBBB8888CCCC9999DDDD0000"), name: "other");
        _torrentService.GetTorrents().Returns([wanted, other]);

        // The first call is what hands the numbers out.
        var all = await CallAsync("""{"method":"torrent-get","arguments":{"fields":["id","name"]}}""");
        var firstId = all.GetProperty("arguments").GetProperty("torrents")[0].GetProperty("id").GetInt32();

        var response = await CallAsync(
            """{"method":"torrent-get","arguments":{"fields":["name"],"ids":[ID]}}"""
                .Replace("ID", firstId.ToString()));

        var torrents = response.GetProperty("arguments").GetProperty("torrents");
        Assert.Equal(1, torrents.GetArrayLength());
        Assert.Equal("ubuntu.iso", torrents[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task RecentlyActive_MeansEverything()
    {
        var first = Torrent();
        var second = Torrent(name: "second");
        _torrentService.GetTorrents().Returns([first, second]);

        var response = await CallAsync("""{"method":"torrent-get","arguments":{"fields":["name"],"ids":"recently-active"}}""");

        Assert.Equal(2, response.GetProperty("arguments").GetProperty("torrents").GetArrayLength());
    }

    [Fact]
    public async Task TorrentSet_FilesAndUnfilesByLabel()
    {
        var torrent = Torrent();
        _torrentService.GetTorrents().Returns([torrent]);

        await CallAsync("""{"method":"torrent-set","arguments":{"labels":["radarr"]}}""");
        await _categoryService.Received(1).AssignAsync(Hash, "radarr", Arg.Any<CancellationToken>());

        // An empty array is how a client clears one.
        await CallAsync("""{"method":"torrent-set","arguments":{"labels":[]}}""");
        await _categoryService.Received(1).AssignAsync(Hash, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TorrentSet_CarriesARatioLimitThrough()
    {
        var torrent = Torrent();
        _torrentService.GetTorrents().Returns([torrent]);

        await CallAsync("""{"method":"torrent-set","arguments":{"seedRatioLimit":2.5}}""");

        Assert.Equal(2.5f, torrent.RatioLimit);
    }

    [Fact]
    public async Task ARatioLimitOfZero_TurnsTheLimitOff()
    {
        var torrent = Torrent();
        _torrentService.GetTorrents().Returns([torrent]);

        await CallAsync("""{"method":"torrent-set","arguments":{"seedRatioLimit":0}}""");

        Assert.Null(torrent.RatioLimit);
    }

    [Fact]
    public async Task TorrentAdd_TakesAnHttpLinkToATorrentFile()
    {
        var added = Torrent();
        _torrentService.AddTorrentFromUrlAsync(Arg.Any<string>(), Arg.Any<AddTorrentOptions>(), Arg.Any<CancellationToken>())
            .Returns(added);

        await CallAsync("""{"method":"torrent-add","arguments":{"filename":"https://example.invalid/a.torrent"}}""");

        await _torrentService.Received(1).AddTorrentFromUrlAsync(
            "https://example.invalid/a.torrent", Arg.Any<AddTorrentOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TorrentAdd_TakesALocalPath()
    {
        var added = Torrent();
        _torrentService.AddTorrentFileAsync(Arg.Any<string>(), Arg.Any<AddTorrentOptions>(), Arg.Any<CancellationToken>())
            .Returns(added);

        await CallAsync("""{"method":"torrent-add","arguments":{"filename":"local-file.torrent"}}""");

        await _torrentService.Received(1).AddTorrentFileAsync(
            "local-file.torrent", Arg.Any<AddTorrentOptions>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The engine holds a path including the torrent's own folder; clients want the directory it
    /// sits in, which is the parent.
    /// </summary>
    [Fact]
    public async Task DownloadDir_IsTheFolderTheDownloadSitsIn()
    {
        var torrent = Torrent();
        var files = Substitute.For<PeerSharp.Interfaces.IFiles>();
        files.DownloadPath.Returns(Path.Combine("D:", "Downloads", "ubuntu.iso"));
        torrent.Files.Returns(files);
        _torrentService.GetTorrents().Returns([torrent]);

        var response = await CallAsync("""{"method":"torrent-get","arguments":{"fields":["downloadDir"]}}""");

        var reported = response.GetProperty("arguments").GetProperty("torrents")[0]
            .GetProperty("downloadDir").GetString();
        Assert.Equal(Path.Combine("D:", "Downloads"), reported);
    }

    private async Task<JsonElement> CallAsync(string request)
    {
        var handler = new TransmissionRpcHandler(_torrentService, _settingsService, _snapshots, _categoryService, "1.2.3");
        var json = await handler.HandleAsync(request, TestContext.Current.CancellationToken);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static ITorrent Torrent(
        InfoHash? hash = null,
        string name = "ubuntu.iso",
        bool started = true,
        TorrentState state = TorrentState.Active,
        bool finished = false)
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Hash.Returns(hash ?? Hash);
        torrent.Name.Returns(name);
        torrent.Started.Returns(started);
        torrent.State.Returns(state);
        torrent.Finished.Returns(finished);
        torrent.TotalSize.Returns(4096);
        torrent.DataLeft.Returns(2048);
        torrent.Progress.Returns(0.5f);
        torrent.FileCount.Returns(1);
        torrent.TimeAdded.Returns(DateTimeOffset.UnixEpoch);
        return torrent;
    }
}
