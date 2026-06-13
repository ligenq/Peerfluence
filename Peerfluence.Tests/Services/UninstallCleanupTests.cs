using Peerfluence.Services;

namespace Peerfluence.Tests.Services;

public sealed class UninstallCleanupTests : IDisposable
{
    private readonly string _root;

    public UninstallCleanupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"peerfluence-uninstall-cleanup-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void GetTraceDirectories_DoesNotIncludeDefaultDownloadsDirectory()
    {
        var directories = UninstallCleanup.GetTraceDirectories();

        Assert.Contains(directories, path => path.EndsWith(
            Path.Combine("AppData", "Local", "Peerfluence"),
            StringComparison.OrdinalIgnoreCase));
        Assert.Contains(directories, path => path.EndsWith(
            Path.Combine("AppData", "Roaming", "Peerfluence"),
            StringComparison.OrdinalIgnoreCase));
        Assert.Contains(directories, path => path.EndsWith("Peerfluence", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(directories, path => path.Contains(
            Path.Combine("Downloads", "Peerfluence"),
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeleteDirectories_RemovesTraceDirectories_AndLeavesOtherDirectories()
    {
        var localTrace = Path.Combine(_root, "LocalTrace");
        var roamingTrace = Path.Combine(_root, "RoamingTrace");
        var downloadDirectory = Path.Combine(_root, "Downloads", "Peerfluence");

        Directory.CreateDirectory(localTrace);
        Directory.CreateDirectory(roamingTrace);
        Directory.CreateDirectory(downloadDirectory);

        File.WriteAllText(Path.Combine(localTrace, "settings.json"), "{}");
        File.WriteAllText(Path.Combine(roamingTrace, "crash.log"), "crash");
        File.WriteAllText(Path.Combine(downloadDirectory, "download.bin"), "download");

        UninstallCleanup.DeleteDirectories([localTrace, roamingTrace]);

        Assert.False(Directory.Exists(localTrace));
        Assert.False(Directory.Exists(roamingTrace));
        Assert.True(Directory.Exists(downloadDirectory));
        Assert.True(File.Exists(Path.Combine(downloadDirectory, "download.bin")));
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }
}
