using Peerfluence.Core.Services;
using Peerfluence.Services;

namespace Peerfluence.Tests.Services;

/// <summary>
/// The names two copies of Peerfluence use to find, or fail to find, each other.
/// </summary>
/// <remarks>
/// These decide whether a second launch hands its magnet link to the running window or opens a
/// second window fighting it for the same session file. Two profiles must never agree on a name,
/// and one profile must always agree with itself.
/// </remarks>
public sealed class ProfileIpcNamesTests
{
    private static IAppPaths Paths(string appData) =>
        new FakePaths(appData, "downloads", "session", "settings.json");

    [Fact]
    public void TheSameProfile_AlwaysProducesTheSameNames()
    {
        var first = Paths(@"C:\profiles\alpha");
        var second = Paths(@"C:\profiles\alpha");

        Assert.Equal(
            ProfileIpcNames.GetSingleInstancePipeName(first),
            ProfileIpcNames.GetSingleInstancePipeName(second));
        Assert.Equal(
            ProfileIpcNames.GetMcpPipeName(first),
            ProfileIpcNames.GetMcpPipeName(second));
        Assert.Equal(
            ProfileIpcNames.GetLockFilePath(first),
            ProfileIpcNames.GetLockFilePath(second));
    }

    [Fact]
    public void DifferentProfiles_NeverShareAName()
    {
        var alpha = Paths(@"C:\profiles\alpha");
        var beta = Paths(@"C:\profiles\beta");

        Assert.NotEqual(
            ProfileIpcNames.GetSingleInstancePipeName(alpha),
            ProfileIpcNames.GetSingleInstancePipeName(beta));
        Assert.NotEqual(
            ProfileIpcNames.GetMcpPipeName(alpha),
            ProfileIpcNames.GetMcpPipeName(beta));
        Assert.NotEqual(
            ProfileIpcNames.GetLockFilePath(alpha),
            ProfileIpcNames.GetLockFilePath(beta));
    }

    [Fact]
    public void TheTwoPipes_AreNotTheSamePipe()
    {
        // Single-instance activation and the MCP server both listen on one profile's behalf. If
        // they shared a name, one would take the other's connections.
        var paths = Paths(@"C:\profiles\alpha");

        Assert.NotEqual(
            ProfileIpcNames.GetSingleInstancePipeName(paths),
            ProfileIpcNames.GetMcpPipeName(paths));
    }

    [Fact]
    public void APathWrittenTwoWays_IsOneProfile()
    {
        // A trailing separator is the same directory, and the scope is resolved to a full path
        // before it is hashed - otherwise a launch from a shortcut and one from the shell could
        // disagree about whether an instance was already running.
        var plain = Paths(@"C:\profiles\alpha");
        var trailing = Paths(@"C:\profiles\alpha\");

        Assert.Equal(
            ProfileIpcNames.GetScopeId(plain),
            ProfileIpcNames.GetScopeId(trailing));
    }

    [Fact]
    public void AScopeId_IsShortEnoughToNameAPipe()
    {
        // Windows caps a pipe name, and the id is the variable part of one. Twelve bytes of SHA-256
        // as hex is twenty-four characters, which leaves room for the prefix.
        var id = ProfileIpcNames.GetScopeId(Paths(@"C:\profiles\alpha"));

        Assert.Equal(24, id.Length);
        Assert.All(id, c => Assert.True(Uri.IsHexDigit(c), $"'{c}' is not a hex digit"));
    }

    private sealed record FakePaths(
        string AppDataDirectory,
        string DefaultDownloadDirectory,
        string SessionDirectory,
        string SettingsFilePath) : IAppPaths;
}
