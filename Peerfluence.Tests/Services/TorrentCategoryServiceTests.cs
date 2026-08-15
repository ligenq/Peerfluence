using Peerfluence.Core.Config;
using Peerfluence.Core.Messaging;
using Peerfluence.Core.Services;
using PeerSharp.Core;

namespace Peerfluence.Tests.Services;

public sealed class TorrentCategoryServiceTests
{
    private static readonly InfoHash First = InfoHash.FromHex("AAAA1111BBBB2222CCCC3333DDDD4444EEEE5555");
    private static readonly InfoHash Second = InfoHash.FromHex("FFFF6666AAAA7777BBBB8888CCCC9999DDDD0000");

    private readonly AppSettings _settings = new();
    private readonly IAppSettingsService _settingsService = Substitute.For<IAppSettingsService>();
    private readonly IAppMessenger _messenger = Substitute.For<IAppMessenger>();

    public TorrentCategoryServiceTests()
    {
        _settingsService.Current.Returns(_settings);
    }

    [Fact]
    public async Task ACategory_CanBeAddedAndFiledUnder()
    {
        var sut = Create();
        await sut.AddAsync("Films", @"D:\Films", TestContext.Current.CancellationToken);

        await sut.AssignAsync(First, "Films", TestContext.Current.CancellationToken);

        Assert.Equal("Films", sut.GetCategory(First));
        Assert.Equal(@"D:\Films", sut.ResolveSavePath("Films"));
    }

    [Fact]
    public async Task ACategoryWithNoFolder_StillGroups()
    {
        var sut = Create();
        await sut.AddAsync("Work", string.Empty, TestContext.Current.CancellationToken);

        await sut.AssignAsync(First, "Work", TestContext.Current.CancellationToken);

        Assert.Equal("Work", sut.GetCategory(First));
        // Nothing to redirect to, so the ordinary download path stands.
        Assert.Null(sut.ResolveSavePath("Work"));
    }

    [Fact]
    public async Task AssigningNothing_UnfilesTheTorrent()
    {
        var sut = Create();
        await sut.AddAsync("Films", @"D:\Films", TestContext.Current.CancellationToken);
        await sut.AssignAsync(First, "Films", TestContext.Current.CancellationToken);

        await sut.AssignAsync(First, null, TestContext.Current.CancellationToken);

        Assert.Null(sut.GetCategory(First));
    }

    /// <summary>
    /// Otherwise the list would show torrents filed under a name that means nothing, and no way to
    /// pick that name to unfile them.
    /// </summary>
    [Fact]
    public async Task RemovingACategory_UnfilesEverythingThatWasInIt()
    {
        var sut = Create();
        await sut.AddAsync("Films", @"D:\Films", TestContext.Current.CancellationToken);
        await sut.AssignAsync(First, "Films", TestContext.Current.CancellationToken);
        await sut.AssignAsync(Second, "Films", TestContext.Current.CancellationToken);

        await sut.RemoveAsync("Films", TestContext.Current.CancellationToken);

        Assert.Empty(sut.Categories);
        Assert.Null(sut.GetCategory(First));
        Assert.Null(sut.GetCategory(Second));
        Assert.Empty(_settings.Categories.Assignments);
    }

    [Fact]
    public async Task ACategoryThatAlreadyExists_IsNotAddedTwice()
    {
        var sut = Create();
        await sut.AddAsync("Films", @"D:\Films", TestContext.Current.CancellationToken);

        await sut.AddAsync("films", @"E:\Elsewhere", TestContext.Current.CancellationToken);

        Assert.Single(sut.Categories);
        Assert.Equal(@"D:\Films", sut.ResolveSavePath("Films"));
    }

    [Fact]
    public async Task AssigningToACategoryThatDoesNotExist_FilesUnderNothing()
    {
        var sut = Create();

        await sut.AssignAsync(First, "Imaginary", TestContext.Current.CancellationToken);

        Assert.Null(sut.GetCategory(First));
    }

    /// <summary>
    /// A magnet has no info hash until its metadata arrives. Filing one would file it under the empty
    /// string and hand that category to whatever came next.
    /// </summary>
    [Fact]
    public async Task ATorrentWithNoHashYet_IsNotFiled()
    {
        var sut = Create();
        await sut.AddAsync("Films", string.Empty, TestContext.Current.CancellationToken);

        await sut.AssignAsync(default, "Films", TestContext.Current.CancellationToken);

        Assert.Empty(_settings.Categories.Assignments);
        Assert.Null(sut.GetCategory(default));
    }

    /// <summary>
    /// Torrents are removed from several places, and none of them tells this service. Sweeping keeps
    /// the settings file from growing by one entry per torrent ever added.
    /// </summary>
    [Fact]
    public async Task AssignmentsForTorrentsThatAreGone_AreForgotten()
    {
        var sut = Create();
        await sut.AddAsync("Films", string.Empty, TestContext.Current.CancellationToken);
        await sut.AssignAsync(First, "Films", TestContext.Current.CancellationToken);
        await sut.AssignAsync(Second, "Films", TestContext.Current.CancellationToken);

        await sut.ForgetMissingAsync([First], TestContext.Current.CancellationToken);

        Assert.Equal("Films", sut.GetCategory(First));
        Assert.Null(sut.GetCategory(Second));
    }

    [Fact]
    public async Task NothingToForget_DoesNotSave()
    {
        var sut = Create();
        await sut.AddAsync("Films", string.Empty, TestContext.Current.CancellationToken);
        await sut.AssignAsync(First, "Films", TestContext.Current.CancellationToken);
        _settingsService.ClearReceivedCalls();

        await sut.ForgetMissingAsync([First], TestContext.Current.CancellationToken);

        await _settingsService.DidNotReceive().SaveAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Several screens show this and none owns it, so a change has to be announced rather than
    /// discovered.
    /// </summary>
    [Fact]
    public async Task EveryChange_IsAnnounced()
    {
        var sut = Create();

        await sut.AddAsync("Films", string.Empty, TestContext.Current.CancellationToken);
        await sut.AssignAsync(First, "Films", TestContext.Current.CancellationToken);
        await sut.RemoveAsync("Films", TestContext.Current.CancellationToken);

        _messenger.Received(3).Publish(Arg.Any<CategoriesChangedMessage>());
    }

    [Fact]
    public async Task ACategoryFiledAgainstAnEditedSettingsFile_ReadsAsNoCategory()
    {
        var sut = Create();
        // What a hand-edited file looks like: an assignment naming a category that is not defined.
        _settings.Categories.Assignments[First.ToHexStringUpper()] = "Deleted";

        Assert.Null(sut.GetCategory(First));

        await Task.CompletedTask;
    }

    private TorrentCategoryService Create() => new(_settingsService, _messenger);
}
