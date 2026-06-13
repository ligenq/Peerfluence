using System.Reflection;
using System.Text.RegularExpressions;

namespace Peerfluence;

internal static class ApplicationVersionInfo
{
    private static readonly Regex DisplayVersionPattern = new(@"^\d+\.\d+\.\d+", RegexOptions.Compiled);

    public static string Version { get; } = GetVersion();

    private static string GetVersion()
    {
        var assembly = typeof(ApplicationVersionInfo).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return GetDisplayVersion(informationalVersion);
        }

        return GetDisplayVersion(assembly.GetName().Version?.ToString() ?? "1.0.0");
    }

    private static string GetDisplayVersion(string version)
    {
        var match = DisplayVersionPattern.Match(version);
        return match.Success ? match.Value : version;
    }
}
