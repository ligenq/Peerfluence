// Ported from Avalonia 12.0 source (MIT License)

using System.Reflection;
using Xunit.v3;

namespace Peerfluence.HeadlessTests.XUnit;

internal sealed class AvaloniaTestFramework : XunitTestFramework
{
    protected override ITestFrameworkDiscoverer CreateDiscoverer(Assembly assembly)
        => new AvaloniaTestFrameworkDiscoverer(CreateTestAssembly(assembly));

    protected override ITestFrameworkExecutor CreateExecutor(Assembly assembly)
        => new AvaloniaTestFrameworkExecutor(CreateTestAssembly(assembly));

    /// <summary>
    /// Everything but the assembly and its version is left to xunit to derive, which is what the
    /// three-argument constructor this replaced did before 4.0 deprecated it.
    /// </summary>
    private static XunitTestAssembly CreateTestAssembly(Assembly assembly)
        => new(
            assembly,
            configFilePath: null,
            assemblyName: null,
            assemblyPath: null,
            targetFramework: null,
            uniqueID: null,
            version: assembly.GetName().Version);
}
