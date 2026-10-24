using System.Reflection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Peerfluence.Core.Services;
using Peerfluence.Logging;
using Peerfluence.Services.Mcp;

namespace Peerfluence.Tests.Architecture;

public class EntryPointTests
{
    /// <summary>
    /// The entry point has to stay synchronous, and it is not obvious why from reading it.
    ///
    /// <para>
    /// <c>[STAThread]</c> applies to the thread the runtime creates. An <c>async Task Main</c> holds
    /// that thread only as far as its first await; everything after resumes on a thread-pool thread,
    /// which belongs to no apartment. Since the Avalonia UI loop is started after that point, the UI
    /// thread itself ends up outside the apartment, and the OLE clipboard refuses every call from
    /// there with "CoInitialize has not been called" - which is how the copy commands came to fail
    /// silently.
    /// </para>
    /// </summary>
    [Fact]
    public void Main_IsSynchronous_SoTheUiKeepsTheApartmentItWasGiven()
    {
        var main = EntryPoint();

        Assert.Equal(typeof(void), main.ReturnType);
    }

    [Fact]
    public void Main_RunsInASingleThreadedApartment()
    {
        var main = EntryPoint();

        Assert.True(
            main.IsDefined(typeof(STAThreadAttribute), inherit: false),
            "The entry point must be [STAThread]: the Windows clipboard, drag and drop and the shell dialogs are all OLE.");
    }

    /// <summary>
    /// A host that stops has to take the window with it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The window closing stops the host, and that direction always worked. The other one did not
    /// exist. ConsoleLifetime answers SIGTERM by telling the operating system not to terminate the
    /// process and raising ApplicationStopping instead; with nothing listening, the dispatcher kept
    /// running and the process survived the signal indefinitely - observed on Linux, where it had
    /// to be killed outright after logging "Application is shutting down..." and nothing more.
    /// </para>
    /// <para>
    /// The hang itself needs a real process and a real signal to reproduce. This pins the wiring
    /// whose absence caused it.
    /// </para>
    /// </remarks>
    [Fact]
    public void HostShutdown_ClosesTheDesktop()
    {
        using var stopping = new CancellationTokenSource();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(stopping.Token);
        var closed = false;

        using var bridge = Peerfluence.Program.BridgeHostShutdownToTheDesktop(lifetime, () => closed = true);
        stopping.Cancel();

        Assert.True(closed, "Stopping the host must ask the desktop lifetime to shut down.");
    }

    [Fact]
    public void TheBridge_StopsListeningOnceTheWindowHasGone()
    {
        // The ordinary way out raises the same event, after the dispatcher has ended. Posting to it
        // then is at best pointless, so the registration is withdrawn first.
        using var stopping = new CancellationTokenSource();
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(stopping.Token);
        var closed = false;

        var bridge = Peerfluence.Program.BridgeHostShutdownToTheDesktop(lifetime, () => closed = true);
        bridge.Dispose();
        stopping.Cancel();

        Assert.False(closed);
    }

    /// <summary>
    /// A magnet link clicked while the application is open starts a second process that only hands
    /// the link over. It used to empty the running instance's log on the way.
    /// </summary>
    [Fact]
    public void ASecondLaunch_HandsOverItsArguments_AndLeavesTheRunningInstancesLogAlone()
    {
        using var profile = new TemporaryLog();
        File.WriteAllText(profile.Path, "the running instance's log\n");
        var singleInstance = Substitute.For<ISingleInstanceService>();
        singleInstance.TryAcquireSingleInstanceLock().Returns(false);
        string[] arguments = ["magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567"];

        using var fileLog = new FileLoggerProvider(profile.Path);
        var claimed = Peerfluence.Program.ClaimProfile(singleInstance, RuntimeOptions(skipLock: false), fileLog, arguments);

        Assert.False(claimed);
        singleInstance.Received(1).SignalExistingInstance(arguments);
        Assert.Equal("the running instance's log\n", File.ReadAllText(profile.Path));
    }

    [Fact]
    public void TheInstanceThatOwnsTheProfile_TakesOverTheLog()
    {
        using var profile = new TemporaryLog();
        var singleInstance = Substitute.For<ISingleInstanceService>();
        singleInstance.TryAcquireSingleInstanceLock().Returns(true);

        using (var fileLog = new FileLoggerProvider(profile.Path))
        {
            fileLog.CreateLogger("Startup").LogInformation("logged before the lock was decided");
            var claimed = Peerfluence.Program.ClaimProfile(singleInstance, RuntimeOptions(skipLock: false), fileLog, []);

            Assert.True(claimed);
        }

        singleInstance.DidNotReceiveWithAnyArgs().SignalExistingInstance(default);
        Assert.Contains("logged before the lock was decided", File.ReadAllText(profile.Path));
    }

    [Fact]
    public void UiAgentMode_SkipsTheLock_ButStillWritesItsLog()
    {
        using var profile = new TemporaryLog();
        var singleInstance = Substitute.For<ISingleInstanceService>();

        using (var fileLog = new FileLoggerProvider(profile.Path))
        {
            var claimed = Peerfluence.Program.ClaimProfile(singleInstance, RuntimeOptions(skipLock: true), fileLog, []);
            fileLog.CreateLogger("Startup").LogInformation("agent run");

            Assert.True(claimed);
        }

        singleInstance.DidNotReceive().TryAcquireSingleInstanceLock();
        Assert.Contains("agent run", File.ReadAllText(profile.Path));
    }

    private static IMcpRuntimeOptions RuntimeOptions(bool skipLock)
    {
        var options = Substitute.For<IMcpRuntimeOptions>();
        options.SkipSingleInstanceLock.Returns(skipLock);
        return options;
    }

    private sealed class TemporaryLog : IDisposable
    {
        private readonly string _directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), $"peerfluence_entry_{Guid.NewGuid():N}");

        public TemporaryLog()
        {
            Directory.CreateDirectory(_directory);
        }

        public string Path => System.IO.Path.Combine(_directory, "peerfluence.log");

        public void Dispose()
        {
            try { Directory.Delete(_directory, recursive: true); } catch (IOException) { }
        }
    }

    private static MethodInfo EntryPoint()
    {
        var program = typeof(Peerfluence.App).Assembly.GetType("Peerfluence.Program");
        Assert.NotNull(program);

        var main = program.GetMethod("Main", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.NotNull(main);
        return main;
    }
}
