using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Services;
using Peerfluence.ViewModels;

namespace Peerfluence.Tests.ViewModels;

public class CreateTorrentViewModelTests
{
    private readonly ITopLevelService _topLevelService = Substitute.For<ITopLevelService>();
    private readonly CreateTorrentViewModel _sut;

    public CreateTorrentViewModelTests()
    {
        _sut = new CreateTorrentViewModel(_topLevelService, NullLogger<CreateTorrentViewModel>.Instance);
    }

    [Fact]
    public void SourcePath_DefaultsToEmpty()
    {
        Assert.Equal(string.Empty, _sut.SourcePath);
    }

    [Fact]
    public void Trackers_DefaultsToEmpty()
    {
        Assert.Equal(string.Empty, _sut.Trackers);
    }

    [Fact]
    public void WebSeeds_DefaultsToEmpty()
    {
        Assert.Equal(string.Empty, _sut.WebSeeds);
    }

    [Fact]
    public void IsPrivate_DefaultsToFalse()
    {
        Assert.False(_sut.IsPrivate);
    }

    [Fact]
    public void Comment_DefaultsToEmpty()
    {
        Assert.Equal(string.Empty, _sut.Comment);
    }

    [Fact]
    public void SelectedPieceSizeIndex_DefaultsTo2()
    {
        Assert.Equal(2, _sut.SelectedPieceSizeIndex);
    }

    [Fact]
    public void ErrorMessage_DefaultsToEmpty()
    {
        Assert.Equal(string.Empty, _sut.ErrorMessage);
    }

    [Fact]
    public void IsCreating_DefaultsToFalse()
    {
        Assert.False(_sut.IsCreating);
    }

    [Fact]
    public void HasError_FalseWhenErrorMessageIsEmpty()
    {
        Assert.False(_sut.HasError);
    }

    [Fact]
    public void HasError_TrueWhenErrorMessageIsSet()
    {
        _sut.ErrorMessage = "Something went wrong";
        Assert.True(_sut.HasError);
    }

    [Fact]
    public void PieceSizes_ContainsExpectedValues()
    {
        Assert.Equal(7, _sut.PieceSizes.Count);
        Assert.Contains("256 KiB", _sut.PieceSizes);
        Assert.Contains("1 MiB", _sut.PieceSizes);
        Assert.Contains("16 MiB", _sut.PieceSizes);
    }

    [Fact]
    public void CreateCommand_CannotExecuteWhenSourcePathIsEmpty()
    {
        Assert.False(_sut.CreateCommand.CanExecute(null));
    }

    [Fact]
    public void CreateCommand_CanExecuteWhenSourcePathIsSet()
    {
        _sut.SourcePath = @"C:\some\path";
        Assert.True(_sut.CreateCommand.CanExecute(null));
    }

    [Fact]
    public void CreateCommand_CannotExecuteWhileCreating()
    {
        _sut.SourcePath = @"C:\some\path";
        _sut.IsCreating = true;
        Assert.False(_sut.CreateCommand.CanExecute(null));
    }

    [Fact]
    public void SettingSourcePath_ClearsErrorMessage()
    {
        _sut.ErrorMessage = "Some error";
        _sut.SourcePath = @"C:\some\path";
        Assert.Equal(string.Empty, _sut.ErrorMessage);
    }

    [Fact]
    public void ErrorMessageChanged_RaisesHasErrorPropertyChanged()
    {
        var raised = false;
        _sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(_sut.HasError)) raised = true;
        };

        _sut.ErrorMessage = "Error";
        Assert.True(raised);
    }

    [Fact]
    public void OnRequestClose_CanBeSubscribedTo()
    {
        var invoked = false;
        _sut.OnRequestClose += () => invoked = true;

        // Just verify no exception — we can't trigger it without a full Create flow
        Assert.False(invoked);
    }

    [Fact]
    public void SourcePath_NotifiesCanExecuteChanged()
    {
        var canExecuteChanged = false;
        _sut.CreateCommand.CanExecuteChanged += (_, _) => canExecuteChanged = true;

        _sut.SourcePath = @"C:\test";
        Assert.True(canExecuteChanged);
    }
}
