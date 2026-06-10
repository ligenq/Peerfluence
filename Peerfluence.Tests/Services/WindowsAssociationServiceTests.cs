using Peerfluence.Services;

namespace Peerfluence.Tests.Services;

public sealed class WindowsAssociationServiceTests : IDisposable
{
    private readonly string _root;

    public WindowsAssociationServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"peerfluence-association-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ResolveAssociationExecutablePath_UsesVelopackRootStub_WhenRunningFromCurrentDirectory()
    {
        var currentDirectory = Path.Combine(_root, "current");
        Directory.CreateDirectory(currentDirectory);

        var currentExecutable = Path.Combine(currentDirectory, "Peerfluence.exe");
        var stableExecutable = Path.Combine(_root, "Peerfluence.exe");
        File.WriteAllText(currentExecutable, string.Empty);
        File.WriteAllText(stableExecutable, string.Empty);

        var result = WindowsAssociationService.ResolveAssociationExecutablePath(currentExecutable);

        Assert.Equal(stableExecutable, result);
    }

    [Fact]
    public void ResolveAssociationExecutablePath_UsesCurrentExecutable_WhenRootStubIsMissing()
    {
        var currentDirectory = Path.Combine(_root, "current");
        Directory.CreateDirectory(currentDirectory);

        var currentExecutable = Path.Combine(currentDirectory, "Peerfluence.exe");
        File.WriteAllText(currentExecutable, string.Empty);

        var result = WindowsAssociationService.ResolveAssociationExecutablePath(currentExecutable);

        Assert.Equal(currentExecutable, result);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }
}
