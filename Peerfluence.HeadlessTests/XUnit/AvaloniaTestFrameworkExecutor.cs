// Ported from Avalonia 12.0 source (MIT License)

using Avalonia.Headless;
using Xunit.v3;

namespace Peerfluence.HeadlessTests.XUnit;

internal sealed class AvaloniaTestFrameworkExecutor(IXunitTestAssembly testAssembly)
    : XunitTestFrameworkExecutor(testAssembly)
{
    private readonly HeadlessUnitTestSession _session = HeadlessUnitTestSession.GetOrStartForAssembly(testAssembly.Assembly);

    protected override ITestFrameworkDiscoverer CreateDiscoverer()
        => new AvaloniaTestFrameworkDiscoverer(TestAssembly);

    public override async ValueTask DisposeAsync()
    {
        _session.Dispose();
        await base.DisposeAsync();
    }
}
