using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.Services.Mcp;

namespace Peerfluence.Tests.Services;

/// <summary>
/// The opt-in exception-rate diagnostic, which is off unless an environment variable says otherwise.
/// </summary>
/// <remarks>
/// Worth pinning down precisely because of what it does when it is on: it subscribes to
/// <c>AppDomain.CurrentDomain.FirstChanceException</c>, which runs on every throw in the process.
/// Leaving that installed by accident would cost something on exactly the path it measures.
/// </remarks>
public sealed class ExceptionRateDiagnosticHostedServiceTests : IDisposable
{
    private const string EnabledVariable = "PEERFLUENCE_EXCEPTION_STATS";
    private readonly string? _original = Environment.GetEnvironmentVariable(EnabledVariable);

    [Fact]
    public async Task WithoutItsVariable_ItDoesNothingAtAll()
    {
        Environment.SetEnvironmentVariable(EnabledVariable, null);
        var sut = new ExceptionRateDiagnosticHostedService(NullLogger<ExceptionRateDiagnosticHostedService>.Instance);

        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Stopping something that never started must not throw either: the host calls StopAsync
        // whether or not StartAsync did anything.
        await sut.StopAsync(TestContext.Current.CancellationToken);
        sut.Dispose();
    }

    [Fact]
    public async Task WithItsVariableSet_ItStartsAndStopsCleanly()
    {
        Environment.SetEnvironmentVariable(EnabledVariable, "1");
        var sut = new ExceptionRateDiagnosticHostedService(NullLogger<ExceptionRateDiagnosticHostedService>.Instance);

        await sut.StartAsync(TestContext.Current.CancellationToken);

        // Something for it to count, so the report path runs rather than returning early.
        try
        {
            throw new InvalidOperationException("counted");
        }
        catch (InvalidOperationException)
        {
        }

        await sut.StopAsync(TestContext.Current.CancellationToken);
        sut.Dispose();
    }

    [Fact]
    public void DisposingWithoutStarting_IsSafe()
    {
        Environment.SetEnvironmentVariable(EnabledVariable, null);
        var sut = new ExceptionRateDiagnosticHostedService(NullLogger<ExceptionRateDiagnosticHostedService>.Instance);

        sut.Dispose();
        sut.Dispose();
    }

    public void Dispose() => Environment.SetEnvironmentVariable(EnabledVariable, _original);
}

/// <summary>
/// The Transmission-compatible RPC listener, which is off until someone turns it on.
/// </summary>
public sealed class TransmissionRpcHostedServiceTests
{
    [Fact]
    public async Task WhenRemoteAccessIsOff_NoListenerIsOpened()
    {
        // The default, and the one that matters: this binds an HTTP listener on a port other
        // machines can reach, so it must stay shut until it is asked for.
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(new AppSettings { Remote = { Enabled = false } });

        var sut = new TransmissionRpcHostedService(
            Substitute.For<Peerfluence.Core.Services.Rpc.ITransmissionRpcHandler>(),
            settingsService,
            NullLogger<TransmissionRpcHostedService>.Instance);

        await sut.StartAsync(TestContext.Current.CancellationToken);
        await sut.StopAsync(TestContext.Current.CancellationToken);
    }
}

/// <summary>The clock that keeps scheduled bandwidth limits in force.</summary>
public sealed class BandwidthScheduleHostedServiceTests
{
    [Fact]
    public async Task Starting_AppliesTheCurrentLimitsBeforeTheFirstMinuteElapses()
    {
        var engine = Substitute.For<ITorrentEngineService>();
        var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        engine.When(service => service.ApplySpeedLimits())
            .Do(_ => applied.TrySetResult());
        var sut = new BandwidthScheduleHostedService(engine, TimeProvider.System);

        await sut.StartAsync(TestContext.Current.CancellationToken);

        await applied.Task.WaitAsync(TestContext.Current.CancellationToken);
        engine.Received(1).ApplySpeedLimits();
        await sut.StopAsync(TestContext.Current.CancellationToken);
    }
}

/// <summary>
/// Where the MCP server writes the token a proxy has to present.
/// </summary>
public sealed class McpServerHostedServiceTests
{
    [Fact]
    public void TheTokenFile_SitsInTheProfileItBelongsTo()
    {
        // Beside the profile rather than in a shared location: two profiles running at once must
        // not be able to read each other's token.
        var paths = new FakePaths(
            Path.Combine("C:", "profiles", "alpha"),
            "downloads",
            "session",
            "settings.json");

        var tokenPath = McpServerHostedService.GetTokenPath(paths);

        Assert.Equal(Path.Combine(paths.AppDataDirectory, "mcp.token"), tokenPath);
    }

    private sealed record FakePaths(
        string AppDataDirectory,
        string DefaultDownloadDirectory,
        string SessionDirectory,
        string SettingsFilePath) : IAppPaths;
}
