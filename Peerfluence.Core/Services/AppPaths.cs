namespace Peerfluence.Core.Services;

public sealed class AppPaths : IAppPaths
{
    public AppPaths()
        : this(null)
    {
    }

    public AppPaths(string? appDataDirectory)
    {
        AppDataDirectory = string.IsNullOrWhiteSpace(appDataDirectory)
            ? GetAppDataDirectory()
            : Path.GetFullPath(appDataDirectory);
        DefaultDownloadDirectory = string.IsNullOrWhiteSpace(appDataDirectory)
            ? GetDefaultDownloadDirectory(AppDataDirectory)
            : Path.Combine(AppDataDirectory, "Downloads");
        SessionDirectory = Path.Combine(AppDataDirectory, "Session");
        SettingsFilePath = Path.Combine(AppDataDirectory, "settings.json");
    }

    public string AppDataDirectory { get; }

    public string DefaultDownloadDirectory { get; }

    public string SessionDirectory { get; }

    public string SettingsFilePath { get; }

    private static string GetAppDataDirectory()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        }

        if (string.IsNullOrWhiteSpace(basePath))
        {
            basePath = AppContext.BaseDirectory;
        }

        return Path.Combine(basePath, "Peerfluence");
    }

    private static string GetDefaultDownloadDirectory(string appDataDirectory)
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return Path.Combine(appDataDirectory, "Downloads");
        }

        return Path.Combine(userProfile, "Downloads", "Peerfluence");
    }
}

