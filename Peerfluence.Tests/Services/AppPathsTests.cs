using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

public sealed class AppPathsTests
{
    [Fact]
    public void CustomAppDataDirectory_IsUsedForProfileScopedPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var paths = new AppPaths(root);

        Assert.Equal(Path.GetFullPath(root), paths.AppDataDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "Downloads"), paths.DefaultDownloadDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "Session"), paths.SessionDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(root), "settings.json"), paths.SettingsFilePath);
    }
}
