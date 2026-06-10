using Peerfluence.ViewModels;
using PeerSharp.Core;

namespace Peerfluence.Tests.ViewModels;

public class TorrentFileItemViewModelTests
{
    [Fact]
    public void Constructor_SetsPropertiesFromFileInfo()
    {
        var fileInfo = new TorrentFileInfo("movies/test.mkv", 1024 * 1024 * 700, 0, 350 * 1024 * 1024L);
        var selection = new FileSelection(true, Priority.High);

        var sut = new TorrentFileItemViewModel(fileInfo, selection, true);

        Assert.Equal(0, sut.Index);
        Assert.Equal("movies/test.mkv", sut.Path);
        Assert.Equal(1024 * 1024 * 700, sut.SizeBytes);
        Assert.Equal(350 * 1024 * 1024L, sut.DownloadedBytes);
        Assert.True(sut.IsSelected);
        Assert.Equal(Priority.High, sut.Priority);
        Assert.True(sut.IsStreamable);
    }

    [Fact]
    public void Constructor_DefaultStreamableFalse()
    {
        var fileInfo = new TorrentFileInfo("test.txt", 100, 0, 0);
        var selection = new FileSelection();

        var sut = new TorrentFileItemViewModel(fileInfo, selection);

        Assert.False(sut.IsStreamable);
    }

    [Fact]
    public void IsSelected_RaisesPropertyChanged()
    {
        var fileInfo = new TorrentFileInfo("test.txt", 100, 0, 0);
        var sut = new TorrentFileItemViewModel(fileInfo, new FileSelection(true, Priority.Normal));

        var raised = false;
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(sut.IsSelected)) raised = true;
        };

        sut.IsSelected = false;
        Assert.True(raised);
    }

    [Fact]
    public void Priority_RaisesPropertyChanged()
    {
        var fileInfo = new TorrentFileInfo("test.txt", 100, 0, 0);
        var sut = new TorrentFileItemViewModel(fileInfo, new FileSelection(true, Priority.Normal));

        var raised = false;
        sut.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(sut.Priority)) raised = true;
        };

        sut.Priority = Priority.High;
        Assert.True(raised);
    }

    [Fact]
    public void PriorityOptions_ContainsAllValues()
    {
        var options = TorrentFileItemViewModel.PriorityOptions;
        Assert.Contains(options, option => option.Value == Priority.DoNotDownload && option.DisplayName == "Do not download");
        Assert.Contains(options, option => option.Value == Priority.Low && option.DisplayName == "Low");
        Assert.Contains(options, option => option.Value == Priority.Normal && option.DisplayName == "Normal");
        Assert.Contains(options, option => option.Value == Priority.High && option.DisplayName == "High");
    }
}
