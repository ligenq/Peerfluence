using Peerfluence.Core.Config;

namespace Peerfluence.Core.Services;

public interface IAppSettingsService
{
    /// <summary>
    /// Raised after settings have been persisted, so long-lived services can apply changes that do
    /// not belong to the settings screen itself.
    /// </summary>
    event Func<CancellationToken, Task>? SettingsSaved;

    AppSettings Current { get; }

    AppSettings CreateDefaultSettings();

    Task LoadAsync(CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);
}
