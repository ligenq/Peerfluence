using Peerfluence.Core.Config;

namespace Peerfluence.Core.Services;

public interface IAppSettingsService
{
    AppSettings Current { get; }

    AppSettings CreateDefaultSettings();

    Task LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}
