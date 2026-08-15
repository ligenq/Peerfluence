using System.Net;
using System.Text;
using Peerfluence.Core.Services;
using PeerSharp.Core;

namespace Peerfluence.Tests.Services;

/// <summary>
/// Proves that what the add paths produce is something the engine would actually take.
///
/// <para>
/// Written because this is the failure that shipped. Every unit test around the Internet Archive
/// source passed while adding any of its results was impossible: the link went to the engine's file
/// loader, which checks <c>File.Exists</c> and throws, so every add failed with "torrent file not
/// found". Nothing caught it because nothing followed the artefact to the end - the tests asserted
/// that a method had been called, and it had been.
/// </para>
///
/// <para>
/// So these end at the parse. A link is only right if the bytes behind it are a torrent, and a
/// magnet is only right if the engine can read an info hash out of it.
/// </para>
/// </summary>
public sealed class AddRoundTripTests
{
    /// <summary>
    /// A minimal but real single-file torrent. Built rather than fetched so the test needs no
    /// network, and bencoded by hand so it is the format under test rather than the writer.
    /// </summary>
    private static byte[] TorrentBytes()
    {
        const string name = "peerfluence-round-trip.bin";
        var pieces = new string('\0', 20);

        var bencoded =
            "d8:announce31:http://tracker.invalid/announce" +
            "4:infod6:lengthi1024e4:name" + name.Length + ":" + name +
            "12:piece lengthi16384e6:pieces20:" + pieces + "ee";

        return Encoding.ASCII.GetBytes(bencoded);
    }

    /// <summary>
    /// The exact shape that was broken: a search result carrying an http link to someone else's
    /// server. The bytes have to come back and parse, because the engine is handed those bytes.
    /// </summary>
    [Fact]
    public async Task ATorrentLinkedOverHttp_ArrivesAsSomethingTheEngineCanParse()
    {
        var bytes = TorrentBytes();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        });

        var engineService = Substitute.For<ITorrentEngineService>();
        var sut = new TorrentService(engineService, Substitute.For<IAppMessenger>(), new HttpClient(handler));

        // The engine is a substitute, so the assertion is on what it was handed rather than on what
        // it did with it - which is the whole question here.
        TorrentFile? handed = null;
        engineService.Engine.AddTorrentAsync(
                Arg.Do<TorrentFile>(file => handed = file),
                Arg.Any<PeerSharp.Config.AddTorrentOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(Substitute.For<PeerSharp.Interfaces.ITorrent>());

        // A path is supplied so the service does not go asking the substituted engine for a default,
        // which is not what this test is about.
        await sut.AddTorrentFromUrlAsync(
            "https://archive.org/download/example/example_archive.torrent",
            new PeerSharp.Config.AddTorrentOptions { DownloadPath = Path.GetTempPath() },
            TestContext.Current.CancellationToken);

        Assert.NotNull(handed);
        Assert.Equal("peerfluence-round-trip.bin", handed.Name);
        Assert.False(handed.InfoHash.IsEmpty);
    }

    /// <summary>
    /// The bytes the fixture builds have to be a torrent in the first place, or the test above would
    /// pass on a parser that accepts anything.
    /// </summary>
    [Fact]
    public void TheFixtureItself_IsARealTorrent()
    {
        var file = TorrentFile.Parse(TorrentBytes());

        Assert.Equal("peerfluence-round-trip.bin", file.Name);
        Assert.Equal(1024, file.TotalSize);
        Assert.False(file.InfoHash.IsEmpty);
    }

    /// <summary>
    /// Something that is not a torrent must fail at the parse rather than reach the engine. An
    /// indexer answering with an HTML error page is the ordinary way this happens.
    /// </summary>
    [Fact]
    public async Task AnHtmlErrorPageBehindATorrentLink_FailsBeforeTheEngineSeesIt()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>404</body></html>", Encoding.UTF8, "text/html")
        });

        var engineService = Substitute.For<ITorrentEngineService>();
        var sut = new TorrentService(engineService, Substitute.For<IAppMessenger>(), new HttpClient(handler));

        await Assert.ThrowsAnyAsync<Exception>(() => sut.AddTorrentFromUrlAsync(
            "https://example.invalid/not-a-torrent",
            new PeerSharp.Config.AddTorrentOptions { DownloadPath = Path.GetTempPath() },
            TestContext.Current.CancellationToken));

        await engineService.Engine.DidNotReceive().AddTorrentAsync(
            Arg.Any<TorrentFile>(),
            Arg.Any<PeerSharp.Config.AddTorrentOptions>(),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// The other half of what the search screen produces. A magnet built by a source is only usable
    /// if the engine's own parser can read an info hash out of it.
    /// </summary>
    [Theory]
    [InlineData("magnet:?xt=urn:btih:AAAA1111BBBB2222CCCC3333DDDD4444EEEE5555")]
    [InlineData("magnet:?xt=urn:btih:AAAA1111BBBB2222CCCC3333DDDD4444EEEE5555&dn=Example+Name")]
    public void AMagnetOfferedBySearch_ParsesWithAUsableInfoHash(string magnet)
    {
        Assert.True(MagnetLink.TryParse(magnet, out var parsed));
        Assert.NotNull(parsed);
        Assert.False(parsed.InfoHash.IsEmpty);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(respond(request));
        }
    }
}
