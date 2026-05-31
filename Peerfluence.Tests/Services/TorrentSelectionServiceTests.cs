using Peerfluence.Core.Services;
using Peerfluence.Core.Messaging;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

public class TorrentSelectionServiceTests
{
    private readonly IAppMessenger _messenger = Substitute.For<IAppMessenger>();
    private readonly TorrentSelectionService _sut;

    public TorrentSelectionServiceTests()
    {
        _sut = new TorrentSelectionService(_messenger);
    }

    [Fact]
    public void SelectedTorrent_DefaultsToNull()
    {
        Assert.Null(_sut.SelectedTorrent);
    }

    [Fact]
    public void SettingSelectedTorrent_SendsSelectionChangedMessage()
    {
        var torrent = Substitute.For<ITorrent>();

        _sut.SelectedTorrent = torrent;

        _messenger.Received(1).Publish(Arg.Is<TorrentSelectionChangedMessage>(message => ReferenceEquals(message.SelectedTorrent, torrent)));
        Assert.Same(torrent, _sut.SelectedTorrent);
    }

    [Fact]
    public void SettingSameTorrent_DoesNotSendMessage()
    {
        var torrent = Substitute.For<ITorrent>();
        _sut.SelectedTorrent = torrent;
        _messenger.ClearReceivedCalls();

        _sut.SelectedTorrent = torrent;

        _messenger.DidNotReceive().Publish(Arg.Any<TorrentSelectionChangedMessage>());
    }

    [Fact]
    public void SettingToNull_SendsSelectionChangedMessage()
    {
        var torrent = Substitute.For<ITorrent>();
        _sut.SelectedTorrent = torrent;
        _messenger.ClearReceivedCalls();

        _sut.SelectedTorrent = null;

        _messenger.Received(1).Publish(Arg.Is<TorrentSelectionChangedMessage>(message => message.SelectedTorrent == null));
        Assert.Null(_sut.SelectedTorrent);
    }

    [Fact]
    public void ChangingTorrent_SendsSelectionChangedMessage()
    {
        var torrent1 = Substitute.For<ITorrent>();
        var torrent2 = Substitute.For<ITorrent>();
        _sut.SelectedTorrent = torrent1;
        _messenger.ClearReceivedCalls();

        _sut.SelectedTorrent = torrent2;

        _messenger.Received(1).Publish(Arg.Is<TorrentSelectionChangedMessage>(message => ReferenceEquals(message.SelectedTorrent, torrent2)));
        Assert.Same(torrent2, _sut.SelectedTorrent);
    }
}
