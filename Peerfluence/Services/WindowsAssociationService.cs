using System;
using System.IO;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Peerfluence.Services;

public sealed class WindowsAssociationService : IWindowsAssociationService
{
    private const string TorrentExtension = ".torrent";
    private const string TorrentProgId = "Peerfluence.Torrent";
    private const string MagnetProtocol = "magnet";
    private const string MagnetProgId = "Peerfluence.Magnet";
    private const string ApplicationRegistryName = "Peerfluence.exe";
    private const string ApplicationName = "Peerfluence";

    public bool IsSupported => OperatingSystem.IsWindows();

    public bool IsTorrentFileAssociated
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            return IsClassDefault(TorrentExtension, TorrentProgId);
        }
    }

    public bool IsMagnetLinkAssociated
    {
        get
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            return IsClassDefault(MagnetProtocol, MagnetProgId);
        }
    }

    public void ApplyAssociations(bool associateTorrentFiles, bool associateMagnetLinks)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var executablePath = GetAssociationExecutablePath();
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return;
        }

        RegisterTorrentProgId(executablePath);
        RegisterMagnetProgId(executablePath);

        SetOrClearClassDefault(TorrentExtension, TorrentProgId, associateTorrentFiles);
        SetOrClearClassDefault(MagnetProtocol, MagnetProgId, associateMagnetLinks);
    }

    [SupportedOSPlatform("windows")]
    private static void RegisterTorrentProgId(string executablePath)
    {
        using var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{TorrentProgId}");
        progId?.SetValue(null, "Peerfluence torrent file");

        using var icon = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{TorrentProgId}\DefaultIcon");
        icon?.SetValue(null, CreateIconReference(executablePath));

        using var command = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{TorrentProgId}\shell\open\command");
        command?.SetValue(null, $"{Quote(executablePath)} \"%1\"");

        using var app = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{ApplicationRegistryName}");
        app?.SetValue("ApplicationName", ApplicationName);

        using var appIcon = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{ApplicationRegistryName}\DefaultIcon");
        appIcon?.SetValue(null, CreateIconReference(executablePath));

        using var supportedTypes = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{ApplicationRegistryName}\SupportedTypes");
        supportedTypes?.SetValue(TorrentExtension, string.Empty);

        using var appCommand = Registry.CurrentUser.CreateSubKey($@"Software\Classes\Applications\{ApplicationRegistryName}\shell\open\command");
        appCommand?.SetValue(null, $"{Quote(executablePath)} \"%1\"");
    }

    [SupportedOSPlatform("windows")]
    private static void RegisterMagnetProgId(string executablePath)
    {
        using var progId = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{MagnetProgId}");
        progId?.SetValue(null, "Peerfluence magnet link");
        progId?.SetValue("URL Protocol", string.Empty);

        using var icon = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{MagnetProgId}\DefaultIcon");
        icon?.SetValue(null, CreateIconReference(executablePath));

        using var command = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{MagnetProgId}\shell\open\command");
        command?.SetValue(null, $"{Quote(executablePath)} \"%1\"");
    }

    [SupportedOSPlatform("windows")]
    private static bool IsClassDefault(string className, string expectedValue)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{className}");
        return string.Equals(key?.GetValue(null) as string, expectedValue, StringComparison.OrdinalIgnoreCase);
    }

    [SupportedOSPlatform("windows")]
    private static void SetOrClearClassDefault(string className, string value, bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{className}");
        if (enabled)
        {
            key?.SetValue(null, value);
            return;
        }

        if (string.Equals(key?.GetValue(null) as string, value, StringComparison.OrdinalIgnoreCase))
        {
            key?.DeleteValue(string.Empty, throwOnMissingValue: false);
        }
    }

    private static string Quote(string value)
    {
        return $"\"{value}\"";
    }

    private static string CreateIconReference(string executablePath)
    {
        return $"{Quote(executablePath)},0";
    }

    private static string? GetAssociationExecutablePath()
    {
        return ResolveAssociationExecutablePath(Environment.ProcessPath);
    }

    internal static string? ResolveAssociationExecutablePath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        var executableDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory))
        {
            return executablePath;
        }

        var directory = new DirectoryInfo(executableDirectory);
        if (!string.Equals(directory.Name, "current", StringComparison.OrdinalIgnoreCase) ||
            directory.Parent is null)
        {
            return executablePath;
        }

        var stableExecutablePath = Path.Combine(directory.Parent.FullName, Path.GetFileName(executablePath));
        return File.Exists(stableExecutablePath)
            ? stableExecutablePath
            : executablePath;
    }
}
