using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Peerfluence.Services;

internal static class ProfileIpcNames
{
    internal static string GetSingleInstancePipeName(IAppPaths appPaths) =>
        $"Peerfluence-SingleInstance-{GetScopeId(appPaths)}";

    internal static string GetMcpPipeName(IAppPaths appPaths) =>
        $"Peerfluence-Mcp-{GetScopeId(appPaths)}";

    internal static string GetLockFilePath(IAppPaths appPaths) =>
        Path.Combine(Path.GetTempPath(), $"Peerfluence-{GetScopeId(appPaths)}.lock");

    internal static string GetScopeId(IAppPaths appPaths)
    {
        var profilePath = Path.GetFullPath(appPaths.AppDataDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var identity = $"{Environment.UserName}\n{profilePath}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return Convert.ToHexString(hash.AsSpan(0, 12));
    }
}
