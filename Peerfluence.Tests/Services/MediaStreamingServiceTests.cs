using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

public sealed class MediaStreamingServiceTests
{
    private static readonly byte[] Payload = [1, 2, 3, 4, 5, 6];

    private static ITorrent Torrent(string name = "movie.mp4")
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.StreamableFileIndices.Returns([0]);
        torrent.Started.Returns(true);
        torrent.DownloadStrategy.Returns(DownloadStrategy.RarestFirst);
        torrent.GetAllFileInfo().Returns([new TorrentFileInfo(name, Payload.Length, 0, Payload.Length)]);
        torrent.OpenStreamAsync(0, Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult<Stream>(new MemoryStream(Payload, writable: false)));
        return torrent;
    }

    private static MediaStreamingService Service(AppSettings? settings = null, ITorrentEngineService? engine = null)
    {
        var store = Substitute.For<IAppSettingsService>();
        store.Current.Returns(settings ?? new AppSettings());
        return new MediaStreamingService(store, engine ?? Substitute.For<ITorrentEngineService>(), NullLoggerFactory.Instance);
    }

    private static HttpClient Client() => new(new SocketsHttpHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };

    [Theory]
    [InlineData("movie.m4v", "video/mp4")]
    [InlineData("movie.m2ts", "video/mp2t")]
    [InlineData("music.m4a", "audio/mp4")]
    [InlineData("music.opus", "audio/ogg")]
    public async Task Playback_ServesRangesWithTheEngineMediaTypeAndPreservesStrategy(string name, string contentType)
    {
        var torrent = Torrent(name);
        var engine = Substitute.For<ITorrentEngineService>();
        using var service = Service(engine: engine);
        // A configured sharing address must never expose ordinary local playback on the network.
        var stream = await service.StartAsync(torrent, 0, false, TestContext.Current.CancellationToken);
        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(new Uri(stream.Url).Host)));
        Assert.Equal(contentType, stream.ContentType);
        using var client = Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, stream.Url);
        request.Headers.Range = new RangeHeaderValue(1, 3);
        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(contentType, response.Content.Headers.ContentType!.MediaType);
        Assert.Equal(new byte[] { 2, 3, 4 }, await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        Assert.Equal(DownloadStrategy.RarestFirst, torrent.DownloadStrategy);
        Assert.DoesNotContain(torrent.ReceivedCalls(), call => call.GetMethodInfo().Name == "set_DownloadStrategy");
        engine.Received(1).ApplyStreamingSettings();
        service.EndSession();
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync(stream.Url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ASharedStream_RequiresItsGeneratedTokenAndSupportsReceiverPreflight()
    {
        var address = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(entry => entry.Address)
            .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip));
        Assert.SkipUnless(address is not null, "A network interface is required to verify a shared listener.");
        var settings = new AppSettings { Streaming = { BindAddress = address.ToString() } };
        using var service = Service(settings);
        var info = await service.StartAsync(Torrent(), 0, true, TestContext.Current.CancellationToken);
        var url = new Uri(info.Url);
        Assert.Equal(address.ToString(), url.Host);
        Assert.Equal(2, url.Segments.Length - 1);
        Assert.True(url.Segments[1].Trim('/').Length >= 20);
        using var client = Client();
        using var unauthorized = await client.GetAsync(new Uri(url, "/stream"), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, unauthorized.StatusCode);
        using var authorized = await client.GetAsync(url, TestContext.Current.CancellationToken);
        Assert.Equal(Payload, await authorized.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken));
        using var request = new HttpRequestMessage(HttpMethod.Options, url);
        using var preflight = await client.SendAsync(request, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
        Assert.Contains("*", preflight.Headers.GetValues("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task StartingAnotherStream_ClosesThePreviousListener()
    {
        using var service = Service();
        var first = await service.StartAsync(Torrent(), 0, false, TestContext.Current.CancellationToken);
        var second = await service.StartAsync(Torrent(), 0, false, TestContext.Current.CancellationToken);
        Assert.NotEqual(first.Url, second.Url);
        using var client = Client();
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => client.GetAsync(first.Url, TestContext.Current.CancellationToken));
        Assert.Equal(Payload, await client.GetByteArrayAsync(second.Url, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task EndingTheSession_CancelsMetadataPreparationAndCanBeRepeated()
    {
        var torrent = Torrent();
        var waiting = new TaskCompletionSource<Stream>(TaskCreationOptions.RunContinuationsAsynchronously);
        torrent.OpenStreamAsync(0, Arg.Any<CancellationToken>())
            .Returns(call => waiting.Task.WaitAsync(call.Arg<CancellationToken>()));
        var service = Service();
        var preparation = service.StartAsync(torrent, 0, false, TestContext.Current.CancellationToken);
        service.EndSession();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation);
        service.EndSession();
        service.Dispose();
        service.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.StartAsync(Torrent(), 0, false, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("invalid")]
    // Blank belongs with the refusals, not with loopback: a 127.0.0.1 link is not something the
    // caller can hand to another device, and nothing about the copied URL would have said so.
    [InlineData("")]
    [InlineData("   ")]
    public async Task Sharing_RefusesAnInvalidAddressInsteadOfFallingBack(string address)
    {
        using var service = Service(new AppSettings { Streaming = { BindAddress = address } });
        await Assert.ThrowsAsync<ArgumentException>(() => service.StartAsync(Torrent(), 0, true, TestContext.Current.CancellationToken));
        // Local playback remains available regardless of a bad shared-address preference.
        var local = await service.StartAsync(Torrent(), 0, false, TestContext.Current.CancellationToken);
        Assert.Equal("127.0.0.1", new Uri(local.Url).Host);
    }

    [Fact]
    public async Task Playback_StartsAStoppedTorrentAndRejectsUnstreamableFiles()
    {
        using var service = Service();
        var torrent = Torrent();
        torrent.Started.Returns(false);
        await service.StartAsync(torrent, 0, false, TestContext.Current.CancellationToken);
        await torrent.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.StartAsync(torrent, 7, false, TestContext.Current.CancellationToken));
    }
}
