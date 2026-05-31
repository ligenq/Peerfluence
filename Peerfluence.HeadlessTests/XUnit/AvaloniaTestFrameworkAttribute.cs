// Ported from Avalonia 12.0 source (MIT License)

using Xunit.v3;

namespace Peerfluence.HeadlessTests.XUnit;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class AvaloniaTestFrameworkAttribute : Attribute, ITestFrameworkAttribute
{
    public Type FrameworkType
        => typeof(AvaloniaTestFramework);
}
