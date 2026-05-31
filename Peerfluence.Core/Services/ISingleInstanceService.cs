namespace Peerfluence.Core.Services;

public interface ISingleInstanceService
{
    bool TryAcquireSingleInstanceLock();

    void ReleaseLock();

    void StartListening();

    void SignalExistingInstance();
}
