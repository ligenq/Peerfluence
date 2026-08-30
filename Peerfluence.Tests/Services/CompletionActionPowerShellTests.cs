using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core.Config;
using Peerfluence.Services;
using PeerSharp.Core;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

/// <summary>
/// The completion action actually running something.
/// </summary>
/// <remarks>
/// <para>
/// The rest of the runner's tests deliberately stop short of starting a process, on the grounds
/// that starting one is the operating system's job. That left the part between the settings and the
/// program untested: whether the tokens expand, whether the arguments survive being split and
/// handed to PowerShell, and whether the working directory is where the script actually runs.
/// </para>
/// <para>
/// So this starts one. A real script, on disk, that writes down what it was given.
/// </para>
/// </remarks>
public sealed class CompletionActionPowerShellTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "peerfluence-completion-tests", Guid.NewGuid().ToString("n"));

    private readonly string _downloads;

    public CompletionActionPowerShellTests()
    {
        _downloads = Path.Combine(_directory, "Downloads", "Some Torrent");
        Directory.CreateDirectory(_downloads);
    }

    public void Dispose()
    {
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { /* a handle still open; it is a temporary directory */ }
    }

    private ITorrent Torrent(string name = "Some Torrent")
    {
        var files = Substitute.For<IFiles>();
        files.DownloadPath.Returns(_downloads);

        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns(name);
        torrent.Files.Returns(files);
        torrent.TotalSize.Returns(4096L);
        torrent.Hash.Returns(new InfoHash(Enumerable.Repeat((byte)0x11, InfoHash.V1Length).ToArray()));
        return torrent;
    }

    /// <summary>
    /// A script that records the arguments it was given and the directory it was run in.
    /// </summary>
    private string WriteScript()
    {
        var script = Path.Combine(_directory, "on-complete.ps1");
        var output = Path.Combine(_directory, "ran.txt");

        File.WriteAllText(script, string.Join(Environment.NewLine,
        [
            "param($name, $path, $size)",
            "$lines = @(",
            "  \"name=$name\",",
            "  \"path=$path\",",
            "  \"size=$size\",",
            "  \"cwd=$($PWD.Path)\"",
            ")",
            $"Set-Content -LiteralPath '{output}' -Value $lines",
            "exit 0",
        ]));

        return script;
    }

    private string Output => Path.Combine(_directory, "ran.txt");

    private static CompletionActionRunner Runner() => new(NullLogger<CompletionActionRunner>.Instance);

    [Fact]
    public async Task APowerShellScript_RunsAndIsToldWhichTorrentFinished()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The .ps1 path only exists on Windows.");

        var result = await Runner().RunAsync(
            Torrent(),
            new CompletionActionSettings
            {
                ProgramPath = WriteScript(),
                ArgumentsTemplate = "\"{name}\" \"{downloadPath}\" {totalSize}",
                WorkingDirectoryTemplate = "{downloadPath}",
                TimeoutSeconds = 60,
            },
            TestContext.Current.CancellationToken);

        Assert.True(result.Started, result.Error);
        Assert.Equal(0, result.ExitCode);

        var wrote = await File.ReadAllLinesAsync(Output, TestContext.Current.CancellationToken);

        // The tokens reached the script as separate arguments, quotes and spaces intact.
        Assert.Contains("name=Some Torrent", wrote);
        Assert.Contains($"path={_downloads}", wrote);
        Assert.Contains("size=4096", wrote);
    }

    [Fact]
    public async Task TheWorkingDirectory_IsWhereTheScriptActuallyRuns()
    {
        // The setting's whole claim. A script that unpacks or moves what was downloaded works in
        // relative paths, and this is what makes those relative to the download rather than to
        // wherever Peerfluence happens to have been started from.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The .ps1 path only exists on Windows.");

        await Runner().RunAsync(
            Torrent(),
            new CompletionActionSettings
            {
                ProgramPath = WriteScript(),
                ArgumentsTemplate = string.Empty,
                WorkingDirectoryTemplate = "{downloadPath}",
                TimeoutSeconds = 60,
            },
            TestContext.Current.CancellationToken);

        var wrote = await File.ReadAllLinesAsync(Output, TestContext.Current.CancellationToken);

        Assert.Contains($"cwd={_downloads}", wrote);
    }

    [Fact]
    public async Task WithNoWorkingDirectory_TheScriptRunsWhereverPeerfluenceWasStarted()
    {
        // Which is the reason the setting exists at all. Left empty, a script writing a log file
        // beside itself writes it into the application's own directory instead.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The .ps1 path only exists on Windows.");

        await Runner().RunAsync(
            Torrent(),
            new CompletionActionSettings
            {
                ProgramPath = WriteScript(),
                ArgumentsTemplate = string.Empty,
                WorkingDirectoryTemplate = string.Empty,
                TimeoutSeconds = 60,
            },
            TestContext.Current.CancellationToken);

        var wrote = await File.ReadAllLinesAsync(Output, TestContext.Current.CancellationToken);

        Assert.DoesNotContain($"cwd={_downloads}", wrote);
        Assert.Contains(wrote, line => line.StartsWith("cwd=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AScriptThatOverstaysItsTimeout_IsStopped()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "The .ps1 path only exists on Windows.");

        var script = Path.Combine(_directory, "slow.ps1");
        await File.WriteAllTextAsync(script, "Start-Sleep -Seconds 30", TestContext.Current.CancellationToken);

        var result = await Runner().RunAsync(
            Torrent(),
            new CompletionActionSettings
            {
                ProgramPath = script,
                WorkingDirectoryTemplate = "{downloadPath}",
                TimeoutSeconds = 1,
            },
            TestContext.Current.CancellationToken);

        Assert.False(result.Started);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }
}
