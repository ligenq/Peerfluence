using PeerSharp.Interfaces;

namespace Peerfluence.Core.Services;

public interface ITorrentEngineService : IAsyncDisposable
{
    IClientEngine Engine { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    Task LoadOptionalDataAsync(CancellationToken cancellationToken);

    Task ShutdownAsync(CancellationToken cancellationToken);
}
