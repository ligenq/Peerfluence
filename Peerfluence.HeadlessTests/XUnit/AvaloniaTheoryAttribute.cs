// Ported from Avalonia 12.0 source (MIT License)

using System.Runtime.CompilerServices;
using Xunit.v3;

namespace Peerfluence.HeadlessTests.XUnit;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
[XunitTestCaseDiscoverer(typeof(AvaloniaTheoryDiscoverer))]
public sealed class AvaloniaTheoryAttribute(
    [CallerFilePath] string? sourceFilePath = null,
    [CallerLineNumber] int sourceLineNumber = -1)
    : TheoryAttribute(sourceFilePath, sourceLineNumber);
