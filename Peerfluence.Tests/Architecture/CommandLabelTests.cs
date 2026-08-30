using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Peerfluence.Tests.Architecture;

/// <summary>
/// Requires a command that cannot finish on its own to say so in its label.
/// </summary>
/// <remarks>
/// <para>
/// Microsoft's guidance on ellipses: "Commands: Indicate that a command needs additional
/// information." A button that opens a file or folder picker is exactly that - pressing it does not
/// do the thing, it asks where. The ellipsis is what tells somebody that before they press it.
/// </para>
/// <para>
/// The convention is already here and had drifted: the two buttons in the create-torrent window are
/// labelled "File..." and "Folder...", and the seven browse buttons in settings were not.
/// </para>
/// <para>
/// Which commands open a picker is read from the compiled IL rather than guessed from the name, so
/// a command that stops calling one, or starts, is noticed either way.
/// </para>
/// </remarks>
public sealed class CommandLabelTests
{
    /// <summary>
    /// Asking the user to choose a file or a folder. Anything reaching one of these needs more
    /// information before it can act.
    /// </summary>
    private static readonly string[] PickerMethods =
    [
        "OpenFilePickerAsync", "OpenFolderPickerAsync", "SaveFilePickerAsync",
    ];

    [Fact]
    public void EveryCommandThatOpensAPicker_SaysSoWithAnEllipsis()
    {
        var opensPicker = CommandsThatOpenAPicker();
        Assert.NotEmpty(opensPicker);

        var english = EnglishStrings();
        var usedInCode = KeysReferencedFromCode();
        var offences = new List<string>();

        foreach (var (file, button) in PickerButtons(opensPicker))
        {
            var key = LabelKey(button);
            if (key is null || !english.TryGetValue(key, out var label))
            {
                continue;
            }

            // A string that code also uses is doing more than one job - Details_ChangeDownloadPath
            // is a button and the heading of the notification that follows it - and a heading must
            // not carry an ellipsis. Splitting those is a decision about the wording, not something
            // to be forced by a rule.
            if (usedInCode.Contains(key))
            {
                continue;
            }

            if (!label.TrimEnd().EndsWith("...", StringComparison.Ordinal)
                && !label.TrimEnd().EndsWith('…'))
            {
                offences.Add(
                    $"  {Relative(file)}:{Line(button)} is bound to a command that opens a picker, "
                        + $"but {key} = \"{label}\" has no ellipsis");
            }
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    // ------------------------------------------------------------------------------- reading --

    /// <summary>
    /// The names of the commands whose methods reach a file or folder picker.
    /// </summary>
    /// <remarks>
    /// A command called <c>BrowseGeoIpCommand</c> is generated from, or wraps, a method called
    /// <c>BrowseGeoIp</c> or <c>BrowseGeoIpAsync</c>, so the graph is asked about both.
    /// </remarks>
    private static HashSet<string> CommandsThatOpenAPicker()
    {
        var graph = new CallGraph();
        graph.Add(typeof(Peerfluence.ViewModels.ViewModelBase).Assembly.Location);

        var commands = new HashSet<string>(StringComparer.Ordinal);

        foreach (var method in graph.Methods)
        {
            var separator = method.LastIndexOf("::", StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }

            var name = method[(separator + 2)..];
            if (!name.EndsWith("Async", StringComparison.Ordinal) && !char.IsUpper(name.FirstOrDefault()))
            {
                continue;
            }

            if (!Reaches(graph, method))
            {
                continue;
            }

            var bare = name.EndsWith("Async", StringComparison.Ordinal) ? name[..^"Async".Length] : name;
            commands.Add(bare + "Command");
        }

        return commands;
    }

    private static bool Reaches(CallGraph graph, string method)
    {
        return graph
            .Reachable([method])
            .Any(reached => PickerMethods.Any(picker =>
                reached.EndsWith("::" + picker, StringComparison.Ordinal)));
    }

    private static IEnumerable<(string File, XElement Button)> PickerButtons(HashSet<string> opensPicker)
    {
        var binding = new Regex(@"\{\s*(?:Compiled)?Binding\s+([A-Za-z0-9_]+)", RegexOptions.Compiled);

        foreach (var file in Directory.EnumerateFiles(ViewsDirectory(), "*.axaml", SearchOption.AllDirectories))
        {
            foreach (var button in XDocument.Load(file, LoadOptions.SetLineInfo).Descendants()
                         .Where(element => element.Name.LocalName == "Button"))
            {
                var command = Attribute(button, "Command");
                if (command is null)
                {
                    continue;
                }

                var match = binding.Match(command);
                if (match.Success && opensPicker.Contains(match.Groups[1].Value))
                {
                    yield return (file, button);
                }
            }
        }
    }

    /// <summary>
    /// What the button says: its own text, or the tooltip standing in for it when it shows only an
    /// icon.
    /// </summary>
    private static string? LabelKey(XElement button)
    {
        var reference = new Regex(@"\{\s*m:L\s+([A-Za-z0-9_]+)\s*\}");

        foreach (var name in new[] { "Content", "ToolTip.Tip" })
        {
            if (Attribute(button, name) is { } value && reference.Match(value) is { Success: true } match)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private static HashSet<string> KeysReferencedFromCode()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var reference = new Regex(@"Resources\.([A-Za-z0-9_]+)", RegexOptions.Compiled);

        foreach (var file in Directory.EnumerateFiles(ProjectDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.EndsWith("Resources.Designer.cs", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in reference.Matches(File.ReadAllText(file)))
            {
                keys.Add(match.Groups[1].Value);
            }
        }

        return keys;
    }

    private static Dictionary<string, string> EnglishStrings()
    {
        var path = Path.Combine(ProjectDirectory(), "Properties", "Resources.resx");

        return XDocument.Load(path)
            .Root!
            .Elements("data")
            .Where(data => data.Attribute("name") is not null && data.Element("value") is not null)
            .ToDictionary(
                data => data.Attribute("name")!.Value,
                data => data.Element("value")!.Value,
                StringComparer.Ordinal);
    }

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;

    private static int Line(XElement element)
    {
        var info = (System.Xml.IXmlLineInfo)element;
        return info.HasLineInfo() ? info.LineNumber : 0;
    }

    private static string ViewsDirectory() => Path.Combine(ProjectDirectory(), "Views");

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

        throw new DirectoryNotFoundException(
            $"Could not find the Peerfluence project directory above {AppContext.BaseDirectory}.");
    }

    private static string Relative(string path) =>
        Path.GetRelativePath(Directory.GetParent(ProjectDirectory())!.FullName, path);
}
