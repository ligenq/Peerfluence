using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using PeerSharp.Config;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

/// <summary>
/// The directory that adds what is dropped into it.
/// </summary>
/// <remarks>
/// Against a real temporary directory, created and removed by each test. The decisions live in
/// <see cref="WatchFolder"/> and are tested without one; what is exercised here is that a file on
/// disk becomes an added torrent and a file that is left alone.
/// </remarks>
public sealed class WatchFolderHostedServiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "peerfluence-watch-tests", Guid.NewGuid().ToString("n"));

    public WatchFolderHostedServiceTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { /* a handle still open; it is a temporary directory */ }
    }

    private (WatchFolderHostedService Service, ITorrentService Torrents) Create(bool enabled = true)
    {
        var settings = new AppSettings();
        settings.WatchFolder.Enabled = enabled;
        settings.WatchFolder.Path = _directory;

        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(settings);

        var torrents = Substitute.For<ITorrentService>();
        torrents.AddTorrentFileAsync(Arg.Any<string>(), Arg.Any<AddTorrentOptions?>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<ITorrent>()));

        return (new WatchFolderHostedService(settingsService, torrents, NullLogger<WatchFolderHostedService>.Instance), torrents);
    }

    private string Drop(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, "not really a torrent");
        return path;
    }

    [Fact]
    public async Task AFileAlreadyThere_IsAddedWhenTheApplicationStarts()
    {
        // The common case, and the one a watcher cannot see: it was dropped while nothing was running.
        var path = Drop("film.torrent");
        var (service, torrents) = Create();

        await service.StartAsync(TestContext.Current.CancellationToken);

        await torrents.Received(1).AddTorrentFileAsync(path, Arg.Any<AddTorrentOptions?>(), Arg.Any<CancellationToken>());
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(WatchFolder.MarkedPath(path)));

        await service.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AFileAlreadyTaken_IsNotTakenAgain()
    {
        Drop("film.torrent" + WatchFolder.AddedSuffix);
        var (service, torrents) = Create();

        await service.SweepAsync(_directory, TestContext.Current.CancellationToken);

        await torrents.DidNotReceive().AddTorrentFileAsync(
            Arg.Any<string>(), Arg.Any<AddTorrentOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AFileThatFailsToAdd_IsLeftForTheNextSweep()
    {
        // The usual failure is being handed a file whose writer has not finished. Marking it would
        // mean never trying again.
        var path = Drop("half-written.torrent");
        var (service, torrents) = Create();
        torrents.AddTorrentFileAsync(Arg.Any<string>(), Arg.Any<AddTorrentOptions?>(), Arg.Any<CancellationToken>())
            .Returns<Task<ITorrent>>(_ => throw new IOException("still being written"));

        await service.AddAsync(path, TestContext.Current.CancellationToken);

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(WatchFolder.MarkedPath(path)));
    }

    [Fact]
    public async Task WithTheFolderTurnedOff_NothingIsRead()
    {
        Drop("film.torrent");
        var (service, torrents) = Create(enabled: false);

        await service.StartAsync(TestContext.Current.CancellationToken);

        await torrents.DidNotReceive().AddTorrentFileAsync(
            Arg.Any<string>(), Arg.Any<AddTorrentOptions?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StopAsync_AndDispose_CanBothBeCalledMoreThanOnce()
    {
        var (service, _) = Create();
        await service.StartAsync(TestContext.Current.CancellationToken);

        await service.StopAsync(TestContext.Current.CancellationToken);
        await service.StopAsync(TestContext.Current.CancellationToken);
        service.Dispose();

        Assert.Null(Record.Exception(service.Dispose));
    }
}
