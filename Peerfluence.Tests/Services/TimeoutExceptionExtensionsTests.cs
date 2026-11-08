using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

public sealed class TimeoutExceptionExtensionsTests
{
    /// <summary>
    /// The work can finish between the wait giving up and the catch looking at it. That has to stay a
    /// finished start, because a failed one removes the torrent from an add.
    /// </summary>
    [Fact]
    public void ATimeoutFromTheWait_IsToldFromOneFromTheWork_EvenAfterTheWorkFinished()
    {
        var timeout = new TimeoutException();

        Assert.True(timeout.IsFromWaitingFor(Task.CompletedTask));
        Assert.True(timeout.IsFromWaitingFor(new TaskCompletionSource().Task));
        Assert.True(timeout.IsFromWaitingFor(Task.FromException(new InvalidOperationException())));
        Assert.False(timeout.IsFromWaitingFor(Task.FromException(timeout)));
    }
}
