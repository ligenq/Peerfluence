using Peerfluence.Core.Config;

namespace Peerfluence.Core.Services;

public interface IAppSettingsStore
{
    Task<AppSettings?> LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken);
}
