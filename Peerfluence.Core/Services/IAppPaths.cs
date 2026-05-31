namespace Peerfluence.Core.Services;

public interface IAppPaths
{
    string AppDataDirectory { get; }

    string DefaultDownloadDirectory { get; }

    string SessionDirectory { get; }

    string SettingsFilePath { get; }
}
