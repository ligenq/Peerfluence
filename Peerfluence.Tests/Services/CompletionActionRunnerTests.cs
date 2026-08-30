using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core.Config;
using Peerfluence.Services;
using PeerSharp.Interfaces;

namespace Peerfluence.Tests.Services;

/// <summary>
/// The program a finished torrent runs, and the checks made before anything is started.
/// </summary>
/// <remarks>
/// These are the branches that matter: this launches an arbitrary executable the user named, so
/// every refusal to launch is a case where something was wrong with what they configured, and the
/// message has to say which. Only the paths that stop short of starting a process are exercised
/// here - starting one is the operating system's job, not this application's logic.
/// </remarks>
public sealed class CompletionActionRunnerTests
{
    private static ITorrent Torrent(string name = "Some Torrent")
    {
        var torrent = Substitute.For<ITorrent>();
        torrent.Name.Returns(name);
        return torrent;
    }

    private static CompletionActionRunner Runner() =>
        new(NullLogger<CompletionActionRunner>.Instance);

    [Fact]
    public async Task WithNoProgramConfigured_NothingIsStartedAndTheReasonSaysSo()
    {
        var result = await Runner().RunAsync(
            Torrent(),
            new CompletionActionSettings { ProgramPath = "   " },
            TestContext.Current.CancellationToken);

        Assert.False(result.Started);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task WhenTheProgramIsNotThere_ItSaysWhichPathItLookedAt()
    {
        // The path is the whole content of the complaint: a completion action that silently does
        // nothing is indistinguishable from one that ran and did nothing.
        var missing = Path.Combine(Path.GetTempPath(), $"peerfluence-not-here-{Guid.NewGuid():N}.exe");

        var result = await Runner().RunAsync(
            Torrent(),
            new CompletionActionSettings { ProgramPath = missing },
            TestContext.Current.CancellationToken);

        Assert.False(result.Started);
        Assert.Contains(missing, result.Error);
    }

    [Fact]
    public async Task WhenTheWorkingDirectoryIsNotThere_NothingIsStarted()
    {
        // Checked before the process is created rather than left to fail on start, where the error
        // would come back as an opaque Win32 code.
        var program = Path.Combine(Path.GetTempPath(), $"peerfluence-runner-{Guid.NewGuid():N}.cmd");
        await File.WriteAllTextAsync(program, "@echo off", TestContext.Current.CancellationToken);

        try
        {
            var result = await Runner().RunAsync(
                Torrent(),
                new CompletionActionSettings
                {
                    ProgramPath = program,
                    WorkingDirectoryTemplate = Path.Combine(Path.GetTempPath(), $"gone-{Guid.NewGuid():N}")
                },
                TestContext.Current.CancellationToken);

            Assert.False(result.Started);
            Assert.False(string.IsNullOrWhiteSpace(result.Error));
        }
        finally
        {
            File.Delete(program);
        }
    }
}
