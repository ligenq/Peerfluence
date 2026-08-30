using Peerfluence.Core.Config;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests;

/// <summary>
/// Settings for the many tests that are not about seeding goals.
/// </summary>
/// <remarks>
/// Adding a torrent now consults the settings for a default ratio and seeding time, which every
/// test that adds one has to be able to answer. Defaults are off, so options arrive as written.
/// </remarks>
internal static class SeedingDefaults
{
    public static IAppSettingsService Off
    {
        get
        {
            var settings = Substitute.For<IAppSettingsService>();
            settings.Current.Returns(new AppSettings());
            return settings;
        }
    }
}
