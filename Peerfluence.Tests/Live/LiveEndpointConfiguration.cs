using System.Text.Json;

namespace Peerfluence.Tests.Live;

/// <summary>
/// Where the live contract tests get an address and a key from, and the reason they are skipped
/// rather than failed when there is nothing to talk to.
///
/// <para>
/// Read from <c>live-contract.local.json</c> at the repository root, but only after
/// <c>PEERFLUENCE_RUN_LIVE_TESTS=1</c> explicitly opts in. The name ends in
/// <c>.local.json</c>, which the repository ignores, because this file holds an API key and an API
/// key does not belong in version control. Nobody who clones this gets one, so on their machine
/// these tests skip, which is the correct behaviour for a test that needs someone else's server.
/// </para>
/// </summary>
public sealed record LiveEndpointConfiguration(string TorznabUrl, string ApiKey)
{
    private const string FileName = "live-contract.local.json";
    private const string EnableVariable = "PEERFLUENCE_RUN_LIVE_TESTS";

    /// <summary>
    /// The configuration if there is one, or null. Null is the ordinary case - on a build machine,
    /// on a contributor's laptop, and here whenever live tests have not been explicitly enabled.
    /// </summary>
    public static LiveEndpointConfiguration? TryLoad()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(EnableVariable), "1", StringComparison.Ordinal))
        {
            return null;
        }

        if (FindFile() is not { } path)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;

            var url = root.TryGetProperty("torznabUrl", out var urlElement) ? urlElement.GetString() : null;
            var key = root.TryGetProperty("apiKey", out var keyElement) ? keyElement.GetString() : null;

            return string.IsNullOrWhiteSpace(url) ? null : new LiveEndpointConfiguration(url, key ?? string.Empty);
        }
        catch (Exception)
        {
            // A malformed local file skips rather than fails: it is not part of the product, and a
            // typo in it should not look like a defect in the thing being tested.
            return null;
        }
    }

    /// <summary>
    /// Walks up from the test binary to the repository root. The tests run from
    /// <c>bin/Debug/net10.0</c>, and the file sits beside the solution.
    /// </summary>
    private static string? FindFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
