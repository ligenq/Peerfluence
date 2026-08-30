using Avalonia.Threading;
using SukiUI.Toasts;

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
        public static void Posted(ISukiToastManager manager)
        {
            Dispatcher.UIThread.Post(() => manager.CreateToast());
        }

        public static void Unposted(ISukiToastManager manager)
        {
            Task.Run(() => manager.CreateToast());
        }
    }
}
