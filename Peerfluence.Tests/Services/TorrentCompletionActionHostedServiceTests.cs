using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.Core;
using Peerfluence.Core.Config;
using Peerfluence.Core.Messaging;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

[Collection("Messenger")]
public sealed class TorrentCompletionActionHostedServiceTests
{
    [Fact]
    public async Task TorrentFinished_RunsConfiguredCompletionAction()
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(new AppSettings
        {
            CompletionAction =
            {
                Enabled = true,
                ProgramPath = "tool.exe"
            }
        });

        var runner = new FakeCompletionActionRunner(new CompletionActionResult(true, 0, null));
        var notifications = new NotificationService();
        var sut = new TorrentCompletionActionHostedService(settingsService, runner, notifications, NullLogger<TorrentCompletionActionHostedService>.Instance);
        var torrent = CreateTorrent("Ubuntu ISO");

        await sut.StartAsync(TestContext.Current.CancellationToken);

        WeakReferenceMessenger.Default.Send(new TorrentAlertMessage(
            torrent,
            new SimpleTorrentAlert
            {
                Id = AlertId.TorrentFinished,
                Torrent = torrent
            }));

        Assert.Equal(1, runner.RunCount);
        Assert.Same(torrent, runner.LastTorrent);
        Assert.Same(settingsService.Current.CompletionAction, runner.LastSettings);
        Assert.Contains(notifications.Notifications, n => n.Title == "Completion action finished" && n.Type == NotificationType.Success);

        await sut.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task TorrentFinished_DoesNotRunWhenDisabled()
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(new AppSettings
        {
            CompletionAction =
            {
                Enabled = false,
                ProgramPath = "tool.exe"
            }
        });

        var runner = new FakeCompletionActionRunner(new CompletionActionResult(true, 0, null));
        var sut = new TorrentCompletionActionHostedService(
            settingsService,
            runner,
            new NotificationService(),
            NullLogger<TorrentCompletionActionHostedService>.Instance);
        var torrent = CreateTorrent("Ubuntu ISO");

        await sut.StartAsync(TestContext.Current.CancellationToken);

        WeakReferenceMessenger.Default.Send(new TorrentAlertMessage(
            torrent,
            new SimpleTorrentAlert
            {
                Id = AlertId.TorrentFinished,
                Torrent = torrent
            }));

        Assert.Equal(0, runner.RunCount);

        await sut.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public void ExpandTokens_ReplacesTorrentValues()
    {
        var torrent = CreateTorrent("Ubuntu ISO");

        var result = CompletionActionRunner.ExpandTokens("{name}|{hash}|{downloadPath}|{totalSize}", torrent);

        Assert.Equal("Ubuntu ISO|0102030405060708090a0b0c0d0e0f1011121314|C:\\Downloads\\Ubuntu|1234", result);
    }

    [Fact]
    public void SplitArguments_HandlesQuotedValues()
    {
        var result = CompletionActionRunner.SplitArguments("--path \"C:\\Downloads\\Ubuntu ISO\" --flag");

        Assert.Equal(["--path", "C:\\Downloads\\Ubuntu ISO", "--flag"], result);
    }

    private static ITorrent CreateTorrent(string name)
    {
        var torrent = Substitute.For<ITorrent>();
        var files = Substitute.For<IFiles>();
        files.DownloadPath.Returns("C:\\Downloads\\Ubuntu");
        torrent.Name.Returns(name);
        torrent.Hash.Returns(new InfoHash(
        [
            1, 2, 3, 4, 5,
            6, 7, 8, 9, 10,
            11, 12, 13, 14, 15,
            16, 17, 18, 19, 20
        ]));
        torrent.Files.Returns(files);
        torrent.TotalSize.Returns(1234);
        return torrent;
    }

    private sealed class FakeCompletionActionRunner : ICompletionActionRunner
    {
        private readonly CompletionActionResult _result;

        public FakeCompletionActionRunner(CompletionActionResult result)
        {
            _result = result;
        }

        public int RunCount { get; private set; }

        public ITorrent? LastTorrent { get; private set; }

        public CompletionActionSettings? LastSettings { get; private set; }

        public Task<CompletionActionResult> RunAsync(ITorrent torrent, CompletionActionSettings settings, CancellationToken cancellationToken)
        {
            RunCount++;
            LastTorrent = torrent;
            LastSettings = settings;
            return Task.FromResult(_result);
        }
    }
}
