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
    HeadlessUnitTestSession session)
    : XunitTestCaseRunnerContext(
        testCase,
        tests,
        messageBus,
        aggregator,
        cancellationTokenSource,
        displayName,
        skipReason,
        explicitOption,
        constructorArguments)
{
    public HeadlessUnitTestSession Session { get; } = session;
}
