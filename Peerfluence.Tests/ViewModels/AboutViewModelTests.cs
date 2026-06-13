using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.ViewModels;

namespace Peerfluence.Tests.ViewModels;

public sealed class AboutViewModelTests
{
    [Fact]
    public void ApplicationVersion_IsAvailable()
    {
        var sut = new AboutViewModel(NullLogger<AboutViewModel>.Instance);

        Assert.False(string.IsNullOrWhiteSpace(sut.ApplicationVersion));
        Assert.Matches(@"^\d+\.\d+\.\d+$", sut.ApplicationVersion);
    }
}
