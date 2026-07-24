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

        var downloadsDirectory = OperatingSystem.IsLinux()
            ? GetLinuxDownloadsDirectory(userProfile)
            : Path.Combine(userProfile, "Downloads");

        return Path.Combine(downloadsDirectory, "Peerfluence");
    }

    private static string GetLinuxDownloadsDirectory(string userProfile)
    {
        var configuredDirectory = Environment.GetEnvironmentVariable("XDG_DOWNLOAD_DIR");
        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            configuredDirectory = ReadXdgDownloadsDirectory(userProfile);
        }

        if (string.IsNullOrWhiteSpace(configuredDirectory))
        {
            return Path.Combine(userProfile, "Downloads");
        }

        var expanded = configuredDirectory
            .Trim()
            .Trim('"')
            .Replace("$HOME", userProfile, StringComparison.Ordinal);
        return Path.IsPathRooted(expanded)
            ? Path.GetFullPath(expanded)
            : Path.GetFullPath(expanded, userProfile);
    }

    private static string? ReadXdgDownloadsDirectory(string userProfile)
    {
        try
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            var configPath = string.IsNullOrWhiteSpace(configHome)
                ? Path.Combine(userProfile, ".config", "user-dirs.dirs")
                : Path.Combine(configHome, "user-dirs.dirs");

            return File.ReadLines(configPath)
                .FirstOrDefault(line => line.StartsWith("XDG_DOWNLOAD_DIR=", StringComparison.Ordinal))?
                ["XDG_DOWNLOAD_DIR=".Length..];
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
