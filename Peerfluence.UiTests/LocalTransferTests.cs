using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using PeerSharp.Core;

namespace Peerfluence.UiTests;

/// <summary>
/// Two real copies of the application on this machine, one giving a torrent to the other, and what the
/// second one keeps through everything that can happen to it afterwards.
/// </summary>
/// <remarks>
/// <para>
/// Everything else proves its part against one engine in one process. This is the only place real
/// bytes cross a real socket between real applications and then have to survive a real restart, and
/// the seeder is gone by then, so anything lost cannot quietly be downloaded again.
/// </para>
/// <para>
/// Nothing leaves the machine: no DHT, no trackers, and the only peer is the other copy on loopback.
/// </para>
/// </remarks>
public sealed class LocalTransferTests : IDisposable
{
    private const string Hash = "hashString";
    private const int Stopped = 0;

    /// <summary>Long enough for a restored session to have started anything it was going to.</summary>
    private static readonly TimeSpan EngineStartup = TimeSpan.FromSeconds(7);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "peerfluence-transfer", Guid.NewGuid().ToString("n"));
    private readonly byte[] _first = RandomNumberGenerator.GetBytes(6 * 1024 * 1024);
    private readonly byte[] _second = RandomNumberGenerator.GetBytes(6 * 1024 * 1024);

    public LocalTransferTests()
    {
        Directory.CreateDirectory(SeedDirectory);
        Directory.CreateDirectory(DownloadDirectory);
    }

    private string SeedDirectory => Path.Combine(_root, "seed");

    private string DownloadDirectory => Path.Combine(_root, "download");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* a file still held by a process on its way out; this is temp */ }
    }

    [Fact]
    public async Task ADownloadedTorrent_KeepsEverythingThroughARename_APause_AndACrash()
    {
        await File.WriteAllBytesAsync(Path.Combine(SeedDirectory, "first.bin"), _first, Token);
        await File.WriteAllBytesAsync(Path.Combine(SeedDirectory, "second.bin"), _second, Token);
        var torrent = new TorrentFileBuilder()
            .WithName("pair")
            .WithPieceLength(256 * 1024)
            .AddFile("first.bin", _first)
            .AddFile("second.bin", _second)
            .Build();

        int seedPort = FreePort();
        using var seedRpc = new Rpc(FreePort());
        using var seeder = new RunningApplication(Settings(seedPort, seedRpc.Port));
        await seedRpc.AddAsync(Convert.ToBase64String(torrent.RawData.Span), SeedDirectory, metainfo: true);
        await seedRpc.UntilAsync(t => t.GetProperty("percentDone").GetDouble() == 1 && t.GetProperty("status").GetInt32() == 6, "the seeder to check its files");

        using var rpc = new Rpc(FreePort());
        var downloader = new RunningApplication(Settings(FreePort(), rpc.Port));
        try
        {
            string hash = torrent.InfoHash.ToHexString();
            await rpc.AddAsync($"magnet:?xt=urn:btih:{hash}&dn=pair&x.pe=127.0.0.1:{seedPort}", DownloadDirectory, metainfo: false);
            await rpc.UntilAsync(t => t.GetProperty("percentDone").GetDouble() == 1, "the download to finish", seconds: 120);
            await AssertFilesAsync("first.bin", "second.bin");

            // The seeder goes, so nothing below can be put right by downloading it again.
            seeder.ShutDown();

            await rpc.CallAsync("torrent-stop");
            await rpc.UntilAsync(t => t.GetProperty("status").GetInt32() == Stopped, "the torrent to stop");
            await downloader.CallToolAsync("rename_torrent_file", new() { ["infoHash"] = hash, ["fileIndex"] = 1, ["newPath"] = "renamed.bin" }, Token);

            downloader = downloader.RestartGracefully();
            var restored = await rpc.SingleTorrentAsync();
            Assert.Equal(Stopped, restored.GetProperty("status").GetInt32());
            Assert.Equal(1, restored.GetProperty("percentDone").GetDouble());

            await rpc.CallAsync("torrent-start");
            await rpc.UntilAsync(t => t.GetProperty("status").GetInt32() == 6, "it to seed again");
            Assert.Equal(1, (await rpc.SingleTorrentAsync()).GetProperty("percentDone").GetDouble());
            await AssertFilesAsync("first.bin", "renamed.bin");
            Assert.False(File.Exists(Path.Combine(DownloadDirectory, "second.bin")), "the old name came back");

            // Paused and closed: it comes back paused, and resumes to where it was.
            await downloader.CallToolAsync("invoke_ui_action", new() { ["actionName"] = "pause_all" }, Token);
            await rpc.UntilAsync(t => t.GetProperty("status").GetInt32() == Stopped, "the pause to stop it");

            downloader = downloader.RestartGracefully();
            await Task.Delay(TimeSpan.FromSeconds(7), Token);
            Assert.Equal(Stopped, (await rpc.SingleTorrentAsync()).GetProperty("status").GetInt32());
            RunningApplication.Until(() => downloader.Exists("ResumeAllButton"), "the toolbar to offer Resume");

            await downloader.CallToolAsync("invoke_ui_action", new() { ["actionName"] = "resume_all" }, Token);
            await rpc.UntilAsync(t => t.GetProperty("status").GetInt32() == 6, "the resume to start it");
            Assert.Equal(1, (await rpc.SingleTorrentAsync()).GetProperty("percentDone").GetDouble());

            // And with no warning at all. Whatever was last saved has to be enough.
            await RunningApplication.SaveSessionByWaitingAsync(Token);
            downloader = downloader.Restart();
            await rpc.UntilAsync(t => t.GetProperty("status").GetInt32() == 6, "it to seed after a crash", seconds: 60);
            Assert.Equal(1, (await rpc.SingleTorrentAsync()).GetProperty("percentDone").GetDouble());
            await AssertFilesAsync("first.bin", "renamed.bin");
        }
        finally
        {
            downloader.Dispose();
        }
    }

    /// <summary>
    /// With no torrents there is nothing else to say the session was paused, and with a kill no save
    /// happens on the way out, so all that is left is what the toolbar's buttons wrote at the time.
    /// </summary>
    [Fact]
    public async Task AnEmptySessionKilledAfterAPause_ComesBackPaused_AndAfterAResume_ComesBackRunning()
    {
        var application = new RunningApplication(Settings(FreePort(), FreePort()));
        try
        {
            application = PauseAndKill(application);
            await Task.Delay(EngineStartup, Token);
            RunningApplication.Until(() => application.Exists("ResumeAllButton"), "the toolbar to offer Resume");

            application = ResumeAndKill(application);
            await Task.Delay(EngineStartup, Token);
            Assert.False(application.Exists("ResumeAllButton"), "the session came back paused after a resume");
        }
        finally
        {
            application.Dispose();
        }
    }

    /// <summary>
    /// The same with a torrent, saved as running beforehand: the pause and the resume happen after that
    /// save, so nothing written to the torrent's entry can be what brings the session back as it was.
    /// </summary>
    [Fact]
    public async Task ASessionKilledAfterAPause_HoldsItsTorrent_AndAfterAResume_RunsIt()
    {
        await File.WriteAllBytesAsync(Path.Combine(SeedDirectory, "plain.bin"), _first, Token);
        var torrent = new TorrentFileBuilder().WithName("plain.bin").WithPieceLength(256 * 1024).AddFile("plain.bin", _first).Build();

        using var rpc = new Rpc(FreePort());
        var application = new RunningApplication(Settings(FreePort(), rpc.Port));
        try
        {
            await rpc.AddAsync(Convert.ToBase64String(torrent.RawData.Span), SeedDirectory, metainfo: true);
            await rpc.UntilAsync(t => t.GetProperty("status").GetInt32() == 6, "it to seed");
            await RunningApplication.SaveSessionByWaitingAsync(Token);

            application = PauseAndKill(application);
            await Task.Delay(EngineStartup, Token);
            Assert.Equal(Stopped, (await rpc.SingleTorrentAsync()).GetProperty("status").GetInt32());
            RunningApplication.Until(() => application.Exists("ResumeAllButton"), "the toolbar to offer Resume");

            application = ResumeAndKill(application);
            await rpc.UntilAsync(t => t.GetProperty("status").GetInt32() == 6, "it to seed after the resume", seconds: 60);
            Assert.False(application.Exists("ResumeAllButton"), "the session came back paused after a resume");
        }
        finally
        {
            application.Dispose();
        }
    }

    /// <summary>
    /// The application adds a torrent stopped and starts it afterwards, so a crash soon after has only
    /// the start's own save to go on, the periodic one being up to a minute away.
    /// </summary>
    [Fact]
    public async Task ATorrentKilledStraightAfterItWasAdded_ComesBackRunning()
    {
        await File.WriteAllBytesAsync(Path.Combine(SeedDirectory, "plain.bin"), _first, Token);
        var torrent = new TorrentFileBuilder().WithName("plain.bin").WithPieceLength(256 * 1024).AddFile("plain.bin", _first).Build();

        using var rpc = new Rpc(FreePort());
        var application = new RunningApplication(Settings(FreePort(), rpc.Port));
        try
        {
            await rpc.AddAsync(Convert.ToBase64String(torrent.RawData.Span), SeedDirectory, metainfo: true);
            await rpc.UntilAsync(t => t.GetProperty("status").GetInt32() == 6, "it to seed");

            application = application.Restart();
            await rpc.UntilAsync(t => t.GetProperty("status").GetInt32() == 6 && t.GetProperty("percentDone").GetDouble() == 1, "it to seed after the crash", seconds: 60);
        }
        finally
        {
            application.Dispose();
        }
    }

    /// <summary>The button changing means the pause is done. Nothing is waited for past that.</summary>
    private static RunningApplication PauseAndKill(RunningApplication application)
    {
        application.Find("PauseAllButton").Patterns.Invoke.Pattern.Invoke();
        RunningApplication.Until(() => application.Exists("ResumeAllButton"), "the pause to finish");
        return application.Restart();
    }

    private static RunningApplication ResumeAndKill(RunningApplication application)
    {
        application.Find("ResumeAllButton").Patterns.Invoke.Pattern.Invoke();
        RunningApplication.Until(() => application.Exists("PauseAllButton"), "the resume to finish");
        return application.Restart();
    }

    /// <summary>
    /// A magnet with no folder chosen, which is how most arrive. It lands in the download root, and the
    /// application gives it a folder of its own once the metadata names one. That used to stop every
    /// single-file torrent partway through, because the folder would have had the name of its file.
    /// </summary>
    [Fact]
    public async Task ASingleFileMagnetWithNoFolderChosen_DownloadsToTheEnd()
    {
        await File.WriteAllBytesAsync(Path.Combine(SeedDirectory, "plain.bin"), _first, Token);
        var torrent = new TorrentFileBuilder().WithName("plain.bin").WithPieceLength(256 * 1024).AddFile("plain.bin", _first).Build();

        await DownloadWithNoFolderChosenAsync(torrent);

        Assert.Equal(_first, await ReadSharedAsync("plain.bin"));
    }

    /// <summary>
    /// By then it has started writing to the root, and those files have to go with it, not stay
    /// behind beside a second copy.
    /// </summary>
    [Fact]
    public async Task AMultiFileMagnetWithNoFolderChosen_MovesIntoItsOwnFolder_LeavingNothingBehind()
    {
        await File.WriteAllBytesAsync(Path.Combine(SeedDirectory, "first.bin"), _first, Token);
        await File.WriteAllBytesAsync(Path.Combine(SeedDirectory, "second.bin"), _second, Token);
        var torrent = new TorrentFileBuilder()
            .WithName("pair")
            .WithPieceLength(256 * 1024)
            .AddFile("first.bin", _first)
            .AddFile("second.bin", _second)
            .Build();

        await DownloadWithNoFolderChosenAsync(torrent, Path.Combine("pair", "first.bin"), Path.Combine("pair", "second.bin"));

        Assert.Equal(_first, await ReadSharedAsync(Path.Combine("pair", "first.bin")));
        Assert.Equal(_second, await ReadSharedAsync(Path.Combine("pair", "second.bin")));
        Assert.Empty(Directory.GetFiles(DownloadDirectory));
    }

    /// <summary>
    /// Waits for the download to finish where it belongs. A torrent this small can finish in the root
    /// before it is moved into its own folder, and is finished again once the move is done, so finished
    /// alone does not say the move has happened.
    /// </summary>
    private async Task DownloadWithNoFolderChosenAsync(TorrentFile torrent, params string[] landsAt)
    {
        int seedPort = FreePort();
        using var seedRpc = new Rpc(FreePort());
        using var seeder = new RunningApplication(Settings(seedPort, seedRpc.Port));
        await seedRpc.AddAsync(Convert.ToBase64String(torrent.RawData.Span), SeedDirectory, metainfo: true);
        await seedRpc.UntilAsync(t => t.GetProperty("status").GetInt32() == 6, "the seeder to check its files");

        using var rpc = new Rpc(FreePort());
        using var downloader = new RunningApplication(Settings(FreePort(), rpc.Port, DownloadDirectory));
        await rpc.CallAsync("torrent-add", new Dictionary<string, object>
        {
            ["filename"] = $"magnet:?xt=urn:btih:{torrent.InfoHash.ToHexString()}&x.pe=127.0.0.1:{seedPort}"
        });
        await rpc.UntilAsync(
            t => t.GetProperty("status").GetInt32() == 6 && t.GetProperty("percentDone").GetDouble() == 1
                && landsAt.All(name => File.Exists(Path.Combine(DownloadDirectory, name))),
            "the download to finish in its own folder", seconds: 60);
    }

    private async Task AssertFilesAsync(string first, string second)
    {
        Assert.Equal(_first, await ReadSharedAsync(first));
        Assert.Equal(_second, await ReadSharedAsync(second));
    }

    /// <summary>Shared, because the application keeps the files open to seed them.</summary>
    private async Task<byte[]> ReadSharedAsync(string name)
    {
        await using var file = new FileStream(
            Path.Combine(DownloadDirectory, name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var content = new MemoryStream();
        await file.CopyToAsync(content, Token);
        return content.ToArray();
    }

    /// <summary>Everything that would reach past this machine turned off.</summary>
    private static string Settings(int peerPort, int rpcPort, string downloadDirectory = "") =>
        $$"""
        {
          "InterfaceMode": "Advanced",
          "ShowAddTorrentOptions": false,
          "Network": { "EnableDht": false, "EnableNatPmp": false, "EnableUpnp": false, "UseAutomaticListeningPort": false, "ListeningPort": {{peerPort}} },
          "Storage": { "DownloadPath": {{JsonSerializer.Serialize(downloadDirectory)}} },
          "Remote": { "Enabled": true, "Port": {{rpcPort}} },
          "Mcp": { "Enabled": true, "AllowDestructiveTools": true }
        }
        """;

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>The Transmission endpoint, as a client of it would use it.</summary>
    private sealed class Rpc(int port) : IDisposable
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
        private string? _session;

        public int Port { get; } = port;

        public void Dispose() => _http.Dispose();

        public Task AddAsync(string source, string directory, bool metainfo) =>
            CallAsync("torrent-add", new Dictionary<string, object>
            {
                [metainfo ? "metainfo" : "filename"] = source,
                ["download-dir"] = directory
            });

        public async Task<JsonElement> SingleTorrentAsync()
        {
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (true)
            {
                try
                {
                    var answer = await CallAsync("torrent-get", new Dictionary<string, object>
                    {
                        ["fields"] = new[] { "name", Hash, "percentDone", "status" }
                    });
                    var torrents = answer.GetProperty("arguments").GetProperty("torrents");
                    if (torrents.GetArrayLength() == 1)
                    {
                        return torrents[0].Clone();
                    }
                }
                catch (HttpRequestException) when (DateTime.UtcNow < deadline)
                {
                    // Still starting up.
                }

                if (DateTime.UtcNow > deadline)
                {
                    throw new InvalidOperationException("the torrent did not come back");
                }

                await Task.Delay(100, Token);
            }
        }

        public async Task UntilAsync(Func<JsonElement, bool> condition, string what, int seconds = 30)
        {
            var deadline = DateTime.UtcNow.AddSeconds(seconds);
            JsonElement last = default;
            while (DateTime.UtcNow < deadline)
            {
                last = await SingleTorrentAsync();
                if (condition(last))
                {
                    return;
                }

                await Task.Delay(100, Token);
            }

            throw new InvalidOperationException($"timed out waiting for {what}; last saw {last}");
        }

        public async Task<JsonElement> CallAsync(string method, Dictionary<string, object>? arguments = null)
        {
            string body = JsonSerializer.Serialize(new Dictionary<string, object> { ["method"] = method, ["arguments"] = arguments ?? [] });
            for (int attempt = 0; ; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{Port}/transmission/rpc")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                if (_session != null)
                {
                    request.Headers.Add("X-Transmission-Session-Id", _session);
                }

                using var response = await _http.SendAsync(request, Token);
                if (response.StatusCode == HttpStatusCode.Conflict && attempt < 3)
                {
                    _session = response.Headers.GetValues("X-Transmission-Session-Id").First();
                    continue;
                }

                response.EnsureSuccessStatusCode();
                string text = await response.Content.ReadAsStringAsync(Token);
                using var document = JsonDocument.Parse(text);
                Assert.Equal("success", document.RootElement.GetProperty("result").GetString());
                return document.RootElement.Clone();
            }
        }
    }
}
