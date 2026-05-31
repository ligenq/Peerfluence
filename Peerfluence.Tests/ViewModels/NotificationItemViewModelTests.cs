using Material.Icons;
using Peerfluence.ViewModels;

namespace Peerfluence.Tests.ViewModels;

public class NotificationItemViewModelTests
{
    [Fact]
    public void Constructor_SetsProperties()
    {
        var sut = new NotificationItemViewModel("Title", "Message", NotificationKind.Info, MaterialIconKind.Information);

        Assert.Equal("Title", sut.Title);
        Assert.Equal("Message", sut.Message);
        Assert.Equal(NotificationKind.Info, sut.Kind);
        Assert.Equal(MaterialIconKind.Information, sut.Icon);
    }

    [Fact]
    public void Timestamp_IsSetOnCreation()
    {
        var before = DateTimeOffset.Now;
        var sut = new NotificationItemViewModel("T", "M", NotificationKind.Info, MaterialIconKind.Information);
        var after = DateTimeOffset.Now;

        Assert.InRange(sut.Timestamp, before, after);
    }

    [Theory]
    [InlineData(NotificationKind.Success, true, false, false)]
    [InlineData(NotificationKind.Warning, false, true, false)]
    [InlineData(NotificationKind.Error, false, false, true)]
    [InlineData(NotificationKind.Info, false, false, false)]
    public void KindFlags_AreCorrect(NotificationKind kind, bool isSuccess, bool isWarning, bool isError)
    {
        var sut = new NotificationItemViewModel("T", "M", kind, MaterialIconKind.Information);

        Assert.Equal(isSuccess, sut.IsSuccess);
        Assert.Equal(isWarning, sut.IsWarning);
        Assert.Equal(isError, sut.IsError);
    }
}
