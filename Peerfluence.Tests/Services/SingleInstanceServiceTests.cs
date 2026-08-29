using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Core.Messaging;
using Peerfluence.Services;

namespace Peerfluence.Tests.Services;

[Collection("Messenger")]
public sealed class SingleInstanceServiceTests
{
    [Fact]
    public async Task ReleaseLock_CanRunOnDifferentThreadThanAcquire()
    {
        using var sut = new SingleInstanceService(
            NullLogger<SingleInstanceService>.Instance,
            new Peerfluence.Core.Services.AppPaths(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

        var acquired = sut.TryAcquireSingleInstanceLock();

        Assert.True(acquired);
        await Task.Run(sut.ReleaseLock, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void DifferentProfiles_CanAcquireIndependentLocks()
    {
        using var first = CreateService();
        using var second = CreateService();

        Assert.True(first.TryAcquireSingleInstanceLock());
        Assert.True(second.TryAcquireSingleInstanceLock());
    }

    private static SingleInstanceService CreateService() => new(
        NullLogger<SingleInstanceService>.Instance,
        new Peerfluence.Core.Services.AppPaths(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))));

    [Fact]
    public void ListeningStarts_AndDisposingStopsIt()
    {
        // The listener is what makes a second launch hand its arguments over instead of opening a
        // second window. Disposing has to close the pipe, or the next run of this process could not
        // acquire the same profile.
        using var sut = CreateService();
        Assert.True(sut.TryAcquireSingleInstanceLock());

        sut.StartListening();
        sut.Dispose();

        // Disposing twice is what happens when the host disposes it after an explicit shutdown.
        sut.Dispose();
    }

    [Fact]
    public void SignallingWithNobodyListening_DoesNotThrow()
    {
        // The ordinary case for a first launch: the signal is best effort, and a failure to connect
        // means there was nothing there, not that anything went wrong.
        using var sut = CreateService();

        sut.SignalExistingInstance(["magnet:?xt=urn:btih:abc"]);
        sut.SignalExistingInstance();
    }

    [Fact]
    public async Task ASignalledArgument_ReachesTheInstanceThatIsListening()
    {
        using var listener = CreateService();
        Assert.True(listener.TryAcquireSingleInstanceLock());
        listener.StartListening();

        var received = new TaskCompletionSource<IReadOnlyList<string>>();
        WeakReferenceMessenger.Default.Register<ActivationRequestedMessage>(
            this,
            (_, message) => received.TrySetResult(message.Arguments));

        try
        {
            listener.SignalExistingInstance(["magnet:?xt=urn:btih:abc"]);

            var arguments = await received.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken);

            Assert.Equal("magnet:?xt=urn:btih:abc", Assert.Single(arguments));
        }
        finally
        {
            WeakReferenceMessenger.Default.Unregister<ActivationRequestedMessage>(this);
        }
    }

}
