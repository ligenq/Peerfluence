// Ported from Avalonia 12.0 source (MIT License)

using System.ComponentModel;
using Xunit.Sdk;
using Xunit.v3;

namespace Peerfluence.HeadlessTests.XUnit;

internal sealed class AvaloniaTestCase : XunitTestCase, ISelfExecutingXunitTestCase
{
    public AvaloniaTestCase(
        IXunitTestMethod testMethod,
        string testCaseDisplayName,
        string uniqueID,
        bool @explicit,
        Type[]? skipExceptions = null,
        string? skipReason = null,
        Type? skipType = null,
        string? skipUnless = null,
        string? skipWhen = null,
        Dictionary<string, HashSet<string>>? traits = null,
        object?[]? testMethodArguments = null,
        string? sourceFilePath = null,
        int? sourceLineNumber = null,
        int? timeout = null)
        : base(
            testMethod,
            testCaseDisplayName,
            uniqueID,
            @explicit,
            skipExceptions,
            skipReason,
            skipType,
            skipUnless,
            skipWhen,
            traits,
            testMethodArguments,
            sourceFilePath,
            sourceLineNumber,
            timeout)
    {
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public AvaloniaTestCase()
    {
    }

    public async ValueTask<RunSummary> Run(
        ExplicitOption explicitOption,
        IMessageBus messageBus,
        object?[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
    {
        var tests = await aggregator.RunAsync(CreateTests, []);

        var runSummary = Task.Run(async () => await AvaloniaTestCaseRunner.Instance.Run(
                this,
                tests,
                messageBus,
                aggregator,
                cancellationTokenSource,
                TestCaseDisplayName,
                SkipReason,
                explicitOption,
                constructorArguments))
            .GetAwaiter()
            .GetResult();

        return runSummary;
    }
}
