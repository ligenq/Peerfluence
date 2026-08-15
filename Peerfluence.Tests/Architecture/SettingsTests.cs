using System.Reflection;
using System.Text.Json.Serialization;
using Peerfluence.Core.Config;

namespace Peerfluence.Tests.Architecture;

public class SettingsTests
{
    /// <summary>
    /// A settings property with no setter is derived from the others, and writing it to the file
    /// would store a second answer to a question that already has one - read back into nothing,
    /// because there is nowhere for it to land.
    ///
    /// <para>
    /// Mechanical because being careful was not enough: this was got wrong for
    /// <c>SearchSettings.IsConfigured</c>, fixed, and then got wrong again for
    /// <c>RemoteSettings.IsUsable</c> and <c>RequiresAuthentication</c>. A rule broken twice is a
    /// rule the compiler should be checking.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryDerivedSettingsValue_IsKeptOutOfTheSettingsFile()
    {
        var offenders = new List<string>();

        foreach (var type in SettingsTypes())
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var isDerived = property.GetMethod != null && property.SetMethod == null;
                if (isDerived && property.GetCustomAttribute<JsonIgnoreAttribute>() == null)
                {
                    offenders.Add($"{type.Name}.{property.Name} is computed and would be written to settings.json; mark it [JsonIgnore]");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Everything the file holds has to survive a round trip. A property the serializer can write but
    /// not read back is a setting that silently resets, which is worse than one that was never there.
    /// </summary>
    [Fact]
    public void EveryStoredSettingsValue_CanBeReadBack()
    {
        var offenders = new List<string>();

        foreach (var type in SettingsTypes())
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null)
                {
                    continue;
                }

                if (property.GetMethod != null && property.SetMethod == null)
                {
                    // Already reported by the test above; not worth saying twice.
                    continue;
                }

                if (property.SetMethod is { IsPublic: false })
                {
                    offenders.Add($"{type.Name}.{property.Name} is written but cannot be read back");
                }
            }
        }

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The guard is worth nothing if it is looking at an empty set, and a new settings class in a new
    /// namespace would slip past it silently.
    /// </summary>
    [Fact]
    public void TheGuard_IsActuallyLookingAtTheSettings()
    {
        var types = SettingsTypes().Select(type => type.Name).ToList();

        Assert.Contains(nameof(SearchSettings), types);
        Assert.Contains(nameof(RemoteSettings), types);
        Assert.Contains(nameof(NetworkSettings), types);
        Assert.True(types.Count >= 8, $"only found {types.Count} settings types: {string.Join(", ", types)}");
    }

    private static IEnumerable<Type> SettingsTypes()
    {
        return typeof(AppSettings).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => type.Namespace == typeof(AppSettings).Namespace)
            .Where(type => type.Name.EndsWith("Settings", StringComparison.Ordinal));
    }
}
