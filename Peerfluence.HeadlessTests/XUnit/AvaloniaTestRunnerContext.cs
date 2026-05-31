// Ported from Avalonia 12.0 source (MIT License)

using Avalonia.Headless;
using Xunit.Sdk;
using Xunit.v3;

namespace Peerfluence.HeadlessTests.XUnit;

internal sealed class AvaloniaTestRunnerContext(
    IXunitTest test,
    IMessageBus messageBus,
    ExplicitOption explicitOption,
    ExceptionAggregator aggregator,
    CancellationTokenSource cancellationTokenSource,
    IReadOnlyCollection<IBeforeAfterTestAttribute> beforeAfterTestAttributes,
    object?[] constructorArguments,
    HeadlessUnitTestSession session)
    : XunitTestRunnerContext(
        test,
        messageBus,
        explicitOption,
        aggregator,
        cancellationTokenSource,
        beforeAfterTestAttributes,
        constructorArguments)
{
    public HeadlessUnitTestSession Session { get; } = session;
}
