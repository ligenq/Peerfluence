// Ported from Avalonia 12.0 source (MIT License) to support xUnit v3 with Avalonia 11.x
// https://github.com/AvaloniaUI/Avalonia/tree/master/src/Headless/Avalonia.Headless.XUnit

using System.Runtime.CompilerServices;
using Xunit.v3;

namespace Peerfluence.HeadlessTests.XUnit;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
[XunitTestCaseDiscoverer(typeof(AvaloniaFactDiscoverer))]
public sealed class AvaloniaFactAttribute(
    [CallerFilePath] string? sourceFilePath = null,
    [CallerLineNumber] int sourceLineNumber = -1)
    : FactAttribute(sourceFilePath, sourceLineNumber);
