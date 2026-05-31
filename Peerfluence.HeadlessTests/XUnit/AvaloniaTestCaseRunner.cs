// Ported from Avalonia 12.0 source (MIT License)

using Avalonia.Headless;
using Xunit.Sdk;
using Xunit.v3;

namespace Peerfluence.HeadlessTests.XUnit;

internal sealed class AvaloniaTestCaseRunner
    : XunitTestCaseRunnerBase<AvaloniaTestCaseRunnerContext, IXunitTestCase, IXunitTest>
{
    public static AvaloniaTestCaseRunner Instance { get; } = new();

    private AvaloniaTestCaseRunner()
    {
    }

    public async ValueTask<RunSummary> Run(
        IXunitTestCase testCase,
        IReadOnlyCollection<IXunitTest> tests,
        IMessageBus messageBus,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource,
        string displayName,
        string? skipReason,
        ExplicitOption explicitOption,
        object?[] constructorArguments)
    {
        var session = HeadlessUnitTestSession.GetOrStartForAssembly(testCase.TestClass.Class.Assembly);

        await using var ctxt = new AvaloniaTestCaseRunnerContext(
            testCase,
            tests,
            messageBus,
            aggregator,
            cancellationTokenSource,
            displayName,
            skipReason,
            explicitOption,
            constructorArguments,
            session);
        await ctxt.InitializeAsync();

        return await Run(ctxt);
    }

    protected override ValueTask<RunSummary> RunTest(
        AvaloniaTestCaseRunnerContext ctxt,
        IXunitTest test)
        => AvaloniaTestRunner.Instance.Run(
            test,
            ctxt.MessageBus,
            ctxt.ConstructorArguments,
            ctxt.ExplicitOption,
            ctxt.Aggregator.Clone(),
            ctxt.CancellationTokenSource,
            ctxt.BeforeAfterTestAttributes,
            ctxt.Session);
}
