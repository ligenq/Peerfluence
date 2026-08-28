// Ported from Avalonia 12.0 source (MIT License)

using Avalonia.Headless;
using Xunit.Sdk;
using Xunit.v3;

namespace Peerfluence.HeadlessTests.XUnit;

internal sealed class AvaloniaTestCaseRunnerContext(
    IXunitTestCase testCase,
    IReadOnlyCollection<IXunitTest> tests,
    IMessageBus messageBus,
    ExceptionAggregator aggregator,
    CancellationTokenSource cancellationTokenSource,
    string displayName,
    string? skipReason,
    ExplicitOption explicitOption,
    object?[] constructorArguments,
    ParallelMode parallelMode,
    ExecutionScheduler scheduler,
    FixtureMappingManager methodFixtureMappings,
    HeadlessUnitTestSession session)
    : XunitTestCaseRunnerContext(
        testCase,
        tests,
        explicitOption,
        messageBus,
        aggregator,
        displayName,
        skipReason,
        cancellationTokenSource,
        parallelMode,
        scheduler,
        constructorArguments,
        methodFixtureMappings)
{
    public HeadlessUnitTestSession Session { get; } = session;
}
