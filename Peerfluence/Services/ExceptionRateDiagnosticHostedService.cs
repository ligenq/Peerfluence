using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Peerfluence.Services;

/// <summary>
/// Answers "is this an excessive number of exceptions?" with a number instead of an impression.
///
/// <para>
/// A BitTorrent client throws constantly and most of it is ordinary: peers vanish mid-transfer, connect
/// attempts time out, sockets reset. Watching "Exception thrown" scroll past the debugger's Output
/// window cannot distinguish that from a genuine problem, because the volume looks alarming either way.
/// What separates them is the rate per connection and where the throws come from - one loop producing
/// thousands is a defect, a few per connection spread across many sites is a client doing its job.
/// </para>
///
/// <para>
/// It also reports how many of those notifications are re-throws of an exception already counted. An
/// exception crossing an <c>await</c> is re-raised by the state machine, so one failure at the bottom
/// of a stream stack costs one notification per layer it unwinds through. That multiplier is what a
/// debugger actually pays, and it is set by how deeply the code is layered rather than by how often
/// anything goes wrong.
/// </para>
///
/// <para>
/// <b>This is a debugging aid and is off unless <c>PEERFLUENCE_EXCEPTION_STATS</c> is set.</b> The
/// handler runs on every throw in the process, so leaving it on costs something on exactly the path it
/// is measuring.
/// </para>
/// </summary>
public sealed class ExceptionRateDiagnosticHostedService : IHostedService, IDisposable
{
    private const string EnabledVariable = "PEERFLUENCE_EXCEPTION_STATS";
    private const string IntervalVariable = "PEERFLUENCE_EXCEPTION_STATS_SECONDS";

    private static readonly TimeSpan DefaultReportInterval = TimeSpan.FromSeconds(60);

    private readonly ILogger<ExceptionRateDiagnosticHostedService> _logger;
    private readonly ConcurrentDictionary<string, StrongBox<int>> _counts = new();
    private readonly ConditionalWeakTable<Exception, object> _alreadySeen = [];

    private CancellationTokenSource? _cts;
    private Task? _reportTask;
    private DateTimeOffset _windowStarted;
    private int _total;
    private int _distinct;
    private bool _subscribed;

    public ExceptionRateDiagnosticHostedService(ILogger<ExceptionRateDiagnosticHostedService> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnabledVariable)))
        {
            return Task.CompletedTask;
        }

        var interval = DefaultReportInterval;
        if (int.TryParse(Environment.GetEnvironmentVariable(IntervalVariable), out int seconds) && seconds > 0)
        {
            interval = TimeSpan.FromSeconds(seconds);
        }

        _windowStarted = DateTimeOffset.UtcNow;
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
        _subscribed = true;

        _logger.LogInformation(
            "Exception rate diagnostics are on, reporting every {Interval:F0}s. Unset {Variable} to turn this off.",
            interval.TotalSeconds,
            EnabledVariable);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _reportTask = ReportPeriodicallyAsync(interval, _cts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_cts is null)
        {
            return;
        }

        Unsubscribe();
        await _cts.CancelAsync().ConfigureAwait(false);

        if (_reportTask is not null)
        {
            try
            {
                await _reportTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { /* Shutting down. */ }
            catch (TimeoutException) { /* Never hold up shutdown for a diagnostic. */ }
        }

        Report();

        _cts.Dispose();
        _cts = null;
    }

    private void OnFirstChance(object? sender, FirstChanceExceptionEventArgs e)
    {
        Interlocked.Increment(ref _total);

        bool isRethrow = _alreadySeen.TryGetValue(e.Exception, out _);
        if (!isRethrow)
        {
            // Weak, so measuring exceptions does not keep them - and everything they captured - alive.
            _alreadySeen.AddOrUpdate(e.Exception, this);
            Interlocked.Increment(ref _distinct);
        }

        string key = $"{e.Exception.GetType().Name} at {DescribeSite(e.Exception)}{(isRethrow ? " [rethrow]" : string.Empty)}";
        Interlocked.Increment(ref _counts.GetOrAdd(key, static _ => new StrongBox<int>()).Value);
    }

    /// <summary>
    /// Names the code responsible, which is the part a bare type name cannot tell you.
    /// </summary>
    /// <remarks>
    /// Reads metadata that trimming is allowed to remove, and the release build does trim. That is
    /// tolerable only because of what this is: an opt-in diagnostic that already answers
    /// "(unattributed)" or "(stack unavailable)" when it cannot see far enough, so a trimmed build
    /// gets a less specific name rather than a wrong one or a crash. Suppressed rather than left as
    /// a warning so that the warnings that do matter are not buried under four that do not.
    /// </remarks>
    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2026:RequiresUnreferencedCode",
        Justification = "Diagnostic only, and degrades to a less specific name when metadata is trimmed away.")]
    private static string DescribeSite(Exception exception)
    {
        try
        {
            // The live thread stack, not exception.StackTrace: at first-chance time the exception's own
            // trace holds only the throw site, because a trace is built up as it propagates.
            var trace = new System.Diagnostics.StackTrace(fNeedFileInfo: false);

            for (int i = 0; i < trace.FrameCount; i++)
            {
                var method = trace.GetFrame(i)?.GetMethod();
                var declaring = method?.DeclaringType;
                var name = declaring?.FullName;

                if (name is null
                    || (!name.StartsWith("PeerSharp", StringComparison.Ordinal)
                        && !name.StartsWith("Peerfluence", StringComparison.Ordinal)))
                {
                    continue;
                }

                var owner = declaring!.DeclaringType ?? declaring;
                return $"{owner.Name}.{Clean(declaring.Name, method!.Name)}";
            }

            // Normal for a throw completing an async operation: the continuation runs on a pool thread
            // with no trace of who awaited it, so name the thrower and accept the caller is lost.
            var site = exception.TargetSite;
            return site is null ? "(unattributed)" : $"(async) {site.DeclaringType?.Name}.{site.Name}";
        }
        catch
        {
            return "(stack unavailable)";
        }
    }

    /// <summary>
    /// Turns <c>&lt;SendLoopAsync&gt;d__42</c> back into the method the author wrote.
    /// </summary>
    private static string Clean(string declaringName, string methodName)
    {
        int end = declaringName.IndexOf('>');
        return declaringName.StartsWith('<') && end > 1 ? declaringName[1..end] : methodName;
    }

    private async Task ReportPeriodicallyAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Report();
        }
    }

    private void Report()
    {
        int total = Volatile.Read(ref _total);
        if (total == 0)
        {
            return;
        }

        int distinct = Volatile.Read(ref _distinct);
        double elapsed = Math.Max(1, (DateTimeOffset.UtcNow - _windowStarted).TotalSeconds);

        var report = new StringBuilder();
        report.AppendLine(CultureInfo.InvariantCulture, $"exception rate over {elapsed:F0}s:");
        report.AppendLine(CultureInfo.InvariantCulture, $"  notifications : {total:N0} ({total / elapsed:F1}/s)");
        report.AppendLine(CultureInfo.InvariantCulture, $"  actual failures: {distinct:N0} ({distinct / elapsed:F1}/s)");

        if (distinct > 0)
        {
            report.AppendLine(CultureInfo.InvariantCulture,
                $"  amplification : {(double)total / distinct:F1}x (notifications per failure, from re-throws crossing awaits)");
        }

        report.AppendLine("  top sites:");
        foreach (var (site, count) in _counts
                     .Select(static pair => (pair.Key, pair.Value.Value))
                     .OrderByDescending(static entry => entry.Value)
                     .Take(15))
        {
            report.AppendLine(CultureInfo.InvariantCulture, $"    {count,7:N0}  {site}");
        }

        _logger.LogInformation("{Report}", report.ToString());
    }

    private void Unsubscribe()
    {
        if (_subscribed)
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
            _subscribed = false;
        }
    }

    public void Dispose()
    {
        Unsubscribe();
        _cts?.Dispose();
        _cts = null;
    }
}
