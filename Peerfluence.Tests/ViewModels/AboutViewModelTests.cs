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

    [Fact]
    public void TheGitHubLink_PointsAtTheProjectOverHttps()
    {
        // Shown as a clickable link, so a wrong scheme is a silent no-op and a wrong host sends
        // people somewhere else entirely.
        var sut = new Peerfluence.ViewModels.AboutViewModel(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Peerfluence.ViewModels.AboutViewModel>.Instance);

        Assert.StartsWith("https://", sut.GitHubUrl, StringComparison.Ordinal);
        Assert.Contains("github.com", sut.GitHubUrl, StringComparison.OrdinalIgnoreCase);
    }

}
