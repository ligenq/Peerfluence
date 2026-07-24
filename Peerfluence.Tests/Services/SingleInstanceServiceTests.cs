using Microsoft.Extensions.Logging.Abstractions;
using Peerfluence.Services;

namespace Peerfluence.Tests.Services;

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
}
