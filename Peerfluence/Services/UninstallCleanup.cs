using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Peerfluence.Services;

internal static class UninstallCleanup
{
    private const string TorrentExtension = ".torrent";
    private const string TorrentProgId = "Peerfluence.Torrent";
    private const string MagnetProtocol = "magnet";
    private const string MagnetProgId = "Peerfluence.Magnet";
    private const string ApplicationRegistryName = "Peerfluence.exe";
    private const string TorrentUserChoiceSubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.torrent\UserChoice";
    private const string MagnetUserChoiceSubKey = @"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\magnet\UserChoice";

    public static void Run()
    {
        DeleteDirectories(GetTraceDirectories());
        CleanupRegistryAssociations();
    }

    internal static IReadOnlyList<string> GetTraceDirectories()
    {
        return new[]
        {
            Path.Combine(GetSpecialFolder(Environment.SpecialFolder.LocalApplicationData), "Peerfluence"),
            Path.Combine(GetSpecialFolder(Environment.SpecialFolder.ApplicationData), "Peerfluence"),
            Path.Combine(Path.GetTempPath(), "Peerfluence")
        };
    }

    internal static void DeleteDirectories(IEnumerable<string> directories)
    {
        foreach (var directory in directories)
        {
            DeleteDirectory(directory);
        }
    }

    private static string GetSpecialFolder(Environment.SpecialFolder folder)
    {
        var path = Environment.GetFolderPath(folder);
        return string.IsNullOrWhiteSpace(path)
            ? AppContext.BaseDirectory
            : path;
    }

    private static void DeleteDirectory(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch
        {
            // Best-effort cleanup. Uninstall must not fail because a log or temp file is locked.
        }
    }

    private static void CleanupRegistryAssociations()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        CleanupRegistryAssociationsWindows();
    }

    [SupportedOSPlatform("windows")]
    private static void CleanupRegistryAssociationsWindows()
    {
        ClearClassDefaultIfMatches(TorrentExtension, TorrentProgId);
        ClearClassDefaultIfMatches(MagnetProtocol, MagnetProgId);
        DeleteUserChoiceIfMatches(TorrentUserChoiceSubKey, TorrentProgId);
        DeleteUserChoiceIfMatches(MagnetUserChoiceSubKey, MagnetProgId);

        DeleteCurrentUserClassesSubKeyTree(TorrentProgId);
        DeleteCurrentUserClassesSubKeyTree(MagnetProgId);
        DeleteCurrentUserClassesSubKeyTree($@"Applications\{ApplicationRegistryName}");
    }

    [SupportedOSPlatform("windows")]
    private static void ClearClassDefaultIfMatches(string className, string expectedValue)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{className}", writable: true);
            if (key != null &&
                string.Equals(key.GetValue(null) as string, expectedValue, StringComparison.OrdinalIgnoreCase))
            {
                key.DeleteValue(string.Empty, throwOnMissingValue: false);
            }
        }
        catch
        {
            // Best-effort registry cleanup.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteCurrentUserClassesSubKeyTree(string subKey)
    {
        try
        {
            using var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", writable: true);
            classes?.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
        }
        catch
        {
            // Best-effort registry cleanup.
        }
    }

    [SupportedOSPlatform("windows")]
    private static void DeleteUserChoiceIfMatches(string subKey, string expectedProgId)
    {
        try
        {
            using var userChoice = Registry.CurrentUser.OpenSubKey(subKey);
            if (!string.Equals(userChoice?.GetValue("ProgId") as string, expectedProgId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var lastSeparator = subKey.LastIndexOf('\\');
            if (lastSeparator < 0)
            {
                return;
            }

            var parentSubKey = subKey[..lastSeparator];
            var keyName = subKey[(lastSeparator + 1)..];
            using var parent = Registry.CurrentUser.OpenSubKey(parentSubKey, writable: true);
            parent?.DeleteSubKeyTree(keyName, throwOnMissingSubKey: false);
        }
        catch
        {
            // Best-effort registry cleanup. Windows may protect UserChoice keys.
        }
    }
}
