// Ported from Avalonia 12.0 source (MIT License)

using Xunit.v3;

namespace Peerfluence.HeadlessTests.XUnit;

internal sealed class AvaloniaTestFrameworkDiscoverer : XunitTestFrameworkDiscoverer
{
    public AvaloniaTestFrameworkDiscoverer(
        IXunitTestAssembly testAssembly,
        IXunitTestCollectionFactory? collectionFactory = null)
        : base(testAssembly, collectionFactory)
    {
        DiscovererTypeCache[typeof(FactAttribute)] = typeof(AvaloniaFactDiscoverer);
        DiscovererTypeCache[typeof(TheoryAttribute)] = typeof(AvaloniaTheoryDiscoverer);
    }
}
