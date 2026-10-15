using Peerfluence.Theme.Controls;
using Avalonia.Threading;

namespace Peerfluence.Tests.Architecture;

public sealed class CallGraphTests
{
    [Fact]
    public void DispatcherDelegateTarget_IsRecordedPrecisely()
    {
        var graph = new CallGraph();
        graph.Add(typeof(CallGraphTests).Assembly.Location);

        Assert.Contains(
            graph.MarshalledMethods,
            method => method.Contains("<Posted>", StringComparison.Ordinal));
        Assert.DoesNotContain(
            graph.MarshalledMethods,
            method => method.Contains("<Unposted>", StringComparison.Ordinal));
    }

    private static class UiThreadFixtures
    {
        public static void Posted(ToastManager manager)
        {
            Dispatcher.UIThread.Post(() => manager.Show(new Toast { Title = "fixture" }));
        }

        public static void Unposted(ToastManager manager)
        {
            Task.Run(() => manager.Show(new Toast { Title = "fixture" }));
        }
    }
}
