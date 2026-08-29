using System.Text.RegularExpressions;

namespace Peerfluence.Tests.Architecture;

/// <summary>
/// Requires the values that make up the application's visual rhythm to come from the resource
/// dictionaries that define them, rather than being written out where they are used.
/// </summary>
/// <remarks>
/// <para>
/// A margin typed into one view is invisible until the screens are put side by side, and by then
/// there are forty of them and no way to tell which were decisions. The dictionaries under
/// <c>Peerfluence/Resources</c> are the vocabulary - eight spacings, six icon sizes, eight font
/// sizes - and the point of having them is that a change to one is a change everywhere it was
/// meant.
/// </para>
/// <para>
/// Read from the .axaml source rather than from the compiled application, because that is where the
/// mistake is made and where the failure has to point. The compiled form has already resolved every
/// reference into the same value a literal would produce.
/// </para>
/// </remarks>
public sealed class XamlResourceTests
{
    /// <summary>
    /// Properties that must come from a resource, and the dictionary they belong to.
    /// </summary>
    /// <remarks>
    /// <c>BorderThickness</c> is deliberately absent. It is a line weight rather than a spacing,
    /// <c>_Thickness.axaml</c> is a vocabulary of margins - every key in it is named one - and there
    /// is no sensible key for the hairline <c>0,0,0,1</c> that separates rows. Adding one would
    /// muddle the vocabulary to satisfy the rule rather than the other way round.
    /// </remarks>
    private static readonly (string Property, string Dictionary)[] MustComeFromResources =
    [
        ("Margin", "_Thickness.axaml"),
        ("Padding", "_Thickness.axaml"),
        ("Spacing", "_Spacings.axaml"),
        ("ColumnSpacing", "_Spacings.axaml"),
        ("RowSpacing", "_Spacings.axaml"),
        ("CornerRadius", "_CornerRadii.axaml"),
        ("FontSize", "_FontSizes.axaml"),
    ];

    /// <summary>
    /// A value that resolves at runtime rather than being written down here. Bindings are allowed
    /// because a value coming from a view model is not a hardcoded one.
    /// </summary>
    private static readonly Regex ResolvedElsewhere = new(
        @"^\{\s*(Static|Dynamic)Resource\s|^\{\s*(Compiled)?Binding\b|^\{\s*TemplateBinding\b|^\{\s*OnPlatform\b",
        RegexOptions.Compiled);

    [Fact]
    public void NoView_WritesOutASpacingThatBelongsInAResourceDictionary()
    {
        var offences = new List<string>();

        foreach (var file in XamlFiles())
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (var (property, dictionary) in MustComeFromResources)
                {
                    foreach (var value in ValuesOf(property, lines[i]))
                    {
                        if (ResolvedElsewhere.IsMatch(value.Trim()))
                        {
                            continue;
                        }

                        offences.Add(
                            $"  {Relative(file)}:{i + 1}  {property}=\"{value}\" "
                                + $"- use a key from {dictionary}, or add one if none of them says this");
                    }
                }
            }
        }

        Assert.True(
            offences.Count == 0,
            $"{offences.Count} values are written out where they are used:{Environment.NewLine}"
                + string.Join(Environment.NewLine, offences));
    }

    [Fact]
    public void EveryResourceKeyReferenced_IsOneThatExists()
    {
        // A misspelled key is not a compile error: Avalonia resolves StaticResource at load, so the
        // window it is on throws when it opens. That is a crash in whichever screen nobody opened
        // during testing.
        var defined = DefinedKeys();
        Assert.NotEmpty(defined);

        var missing = new List<string>();
        var reference = new Regex(@"\{\s*(?:Static|Dynamic)Resource\s+([A-Za-z0-9_.]+)\s*\}", RegexOptions.Compiled);

        foreach (var file in XamlFiles())
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in reference.Matches(lines[i]))
                {
                    var key = match.Groups[1].Value;

                    // Only the keys this application defines can be checked. SukiUI and Avalonia
                    // bring their own, and their names are not knowable from here.
                    if (key.StartsWith("Suki", StringComparison.Ordinal)
                        || key.StartsWith("System", StringComparison.Ordinal)
                        || key.StartsWith("Theme", StringComparison.Ordinal)
                        || defined.Contains(key))
                    {
                        continue;
                    }

                    missing.Add($"  {Relative(file)}:{i + 1}  {{StaticResource {key}}} is not defined in Peerfluence/Resources");
                }
            }
        }

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} resource keys are referenced but never defined:{Environment.NewLine}"
                + string.Join(Environment.NewLine, missing));
    }

    // ------------------------------------------------------------------------------- reading --

    /// <summary>
    /// Attribute values for one property on a line, ignoring properties that merely end with the
    /// same word - <c>BorderThickness</c> is not <c>Thickness</c>, and <c>FontSize</c> is not
    /// <c>Size</c>.
    /// </summary>
    private static IEnumerable<string> ValuesOf(string property, string line)
    {
        foreach (Match match in Regex.Matches(line, $@"(?<![A-Za-z0-9_.]){Regex.Escape(property)}\s*=\s*""([^""]*)"""))
        {
            yield return match.Groups[1].Value;
        }

        // <Setter Property="Margin" Value="4"/>, which sets the same thing from a style.
        foreach (Match match in Regex.Matches(
            line,
            $@"Property\s*=\s*""{Regex.Escape(property)}""\s+Value\s*=\s*""([^""]*)"""))
        {
            yield return match.Groups[1].Value;
        }
    }

    private static HashSet<string> DefinedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var declaration = new Regex(@"x:Key\s*=\s*""([^""]+)""", RegexOptions.Compiled);

        // Anywhere a key is declared, not only the dictionaries: App.axaml holds the converter
        // instances, and a view may declare its own.
        foreach (var file in XamlFiles().Concat(ResourceDictionaries()))
        {
            foreach (Match match in declaration.Matches(File.ReadAllText(file)))
            {
                keys.Add(match.Groups[1].Value);
            }
        }

        return keys;
    }

    /// <summary>
    /// The application's own markup: views, windows, controls and <c>App.axaml</c>.
    /// </summary>
    private static IEnumerable<string> XamlFiles()
    {
        var files = Directory
            .EnumerateFiles(ProjectDirectory(), "*.axaml", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !IsResourceDictionary(path))
            .ToList();

        Assert.NotEmpty(files);
        return files;
    }

    /// <summary>
    /// Where the literals belong. These files exist to say what a spacing is.
    /// </summary>
    private static IEnumerable<string> ResourceDictionaries()
    {
        return Directory
            .EnumerateFiles(Path.Combine(ProjectDirectory(), "Resources"), "*.axaml")
            .Where(path => !Path.GetFileName(path).Equals("SukiSeparatorFix.axaml", StringComparison.Ordinal));
    }

    private static bool IsResourceDictionary(string path)
    {
        return path.Contains(
            $"{Path.DirectorySeparatorChar}Resources{Path.DirectorySeparatorChar}",
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Walks up from the test binary to the application's project directory.
    /// </summary>
    private static string ProjectDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Peerfluence", "Peerfluence.csproj");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }

            directory = directory.Parent;
        }

        // Failing loudly rather than passing on an empty set: a test that silently checks nothing
        // is worse than no test, because it reports success.
        throw new DirectoryNotFoundException(
            $"Could not find the Peerfluence project directory above {AppContext.BaseDirectory}.");
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(Directory.GetParent(ProjectDirectory())!.FullName, path);
}
