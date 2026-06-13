using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Services;

namespace Peerfluence.Tests.Services;

public sealed class SingleInstanceServiceTests
{
    [Fact]
    public async Task ReleaseLock_CanRunOnDifferentThreadThanAcquire()
    {
        using var sut = new SingleInstanceService(NullLogger<SingleInstanceService>.Instance);

        var acquired = sut.TryAcquireSingleInstanceLock();

        Assert.True(acquired);
        await Task.Run(sut.ReleaseLock, TestContext.Current.CancellationToken);
    }
}
