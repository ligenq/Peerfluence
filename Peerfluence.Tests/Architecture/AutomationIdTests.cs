using System.Text.RegularExpressions;

namespace Peerfluence.Tests.Architecture;

/// <summary>
/// Requires every control a person can act on to carry a stable identifier that a test or an agent
/// can find it by.
/// </summary>
/// <remarks>
/// <para>
/// <c>AutomationProperties.AutomationId</c> is what Avalonia puts on the platform accessibility
/// tree, and what Appium's <c>FindElementByAccessibilityId</c> reads. The reason it matters here
/// rather than anywhere else is the ten languages: anything keyed on the text a control displays
/// works in English and fails in the other nine, and an id is by definition not translated.
/// </para>
/// <para>
/// Only controls a person acts on, and only outside a template. A <c>Grid</c> is layout and nobody
/// automates it; a button inside a <c>DataTemplate</c> is instantiated once per row, so one id
/// there names every row at once and identifies nothing. Those are found by scoping to the row,
/// which is a job for the test rather than for the markup.
/// </para>
/// </remarks>
public sealed class AutomationIdTests
{
    /// <summary>
    /// The controls a test or an agent would click, type into, or read a value from.
    /// </summary>
    private static readonly HashSet<string> Interactive =
    [
        "Button", "ToggleButton", "CheckBox", "RadioButton", "ComboBox", "TextBox",
        "NumericUpDown", "ToggleSwitch", "Slider", "DataGrid", "TabItem", "MenuItem",
        "ListBox", "AutoCompleteBox",
    ];

    /// <summary>
    /// Inside one of these, a control is a blueprint rather than a control: it exists once per item
    /// bound to it, and every copy would carry the same id.
    /// </summary>
    private static readonly HashSet<string> Templates = ["DataTemplate", "ControlTemplate"];

    [Fact]
    public void EveryInteractiveControl_CanBeFoundByAnAutomationId()
    {
        var missing = new List<string>();

        foreach (var file in XamlFiles())
        {
            foreach (var element in InteractiveElements(File.ReadAllText(file)))
            {
                if (element.Raw.Contains("AutomationProperties.AutomationId", StringComparison.Ordinal))
                {
                    continue;
                }

                missing.Add(
                    $"  {Relative(file)}:{element.Line}  <{element.Tag}> has no AutomationProperties.AutomationId");
            }
        }

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} controls cannot be found by an automation id:{Environment.NewLine}"
                + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void NoTwoControls_ShareAnAutomationId()
    {
        // An id that names two things names neither. Enforced across every view rather than within
        // one, so a test never has to say which screen it meant.
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        var clashes = new List<string>();

        foreach (var file in XamlFiles())
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in Regex.Matches(lines[i], @"AutomationProperties\.AutomationId\s*=\s*""([^""]*)"""))
                {
                    var id = match.Groups[1].Value;
                    var here = $"{Relative(file)}:{i + 1}";

                    if (seen.TryGetValue(id, out var first))
                    {
                        clashes.Add($"  '{id}' is used at {first} and again at {here}");
                    }
                    else
                    {
                        seen[id] = here;
                    }
                }
            }
        }

        Assert.True(
            clashes.Count == 0,
            $"{clashes.Count} automation ids are not unique:{Environment.NewLine}"
                + string.Join(Environment.NewLine, clashes));
    }

    /// <summary>
    /// Controls with no label of their own. A ToggleSwitch or CheckBox carries its own Content,
    /// which Avalonia already exposes as the accessible name; a text box has nothing.
    /// </summary>
    private static readonly HashSet<string> NeedsAnAccessibleName =
    [
        "TextBox", "ComboBox", "NumericUpDown", "AutoCompleteBox", "Slider",
    ];

    [Fact]
    public void EveryInputWithoutALabelOfItsOwn_HasAnAccessibleName()
    {
        // AutomationId is for tests; this is for people. Avalonia's accessibility documentation
        // calls Name "the most important accessibility property ... the text that a screen reader
        // announces when the control receives focus", and a text box beside a separate TextBlock
        // has no programmatic connection to it - the label is read out when nothing is focused on
        // it, and the box announces itself as an edit box and nothing else.
        //
        // Localized, because it is read aloud: the name comes from the same resource key as the
        // visible label rather than being written out here.
        var missing = new List<string>();

        foreach (var file in XamlFiles())
        {
            foreach (var element in InteractiveElements(File.ReadAllText(file)))
            {
                if (!NeedsAnAccessibleName.Contains(element.Tag)
                    || element.Raw.Contains("AutomationProperties.Name", StringComparison.Ordinal)
                    || element.Raw.Contains("AutomationProperties.LabeledBy", StringComparison.Ordinal))
                {
                    continue;
                }

                missing.Add(
                    $"  {Relative(file)}:{element.Line}  <{element.Tag}> has no AutomationProperties.Name, "
                        + "so a screen reader announces it without saying what it is for");
            }
        }

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryAccessibleName_IsLocalized()
    {
        // A name that is read aloud has to be read aloud in the user's language.
        var literal = new List<string>();

        foreach (var file in XamlFiles())
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in Regex.Matches(lines[i], @"AutomationProperties\.Name\s*=\s*""([^""]*)"""))
                {
                    if (!match.Groups[1].Value.TrimStart().StartsWith('{'))
                    {
                        literal.Add($"  {Relative(file)}:{i + 1} names a control \"{match.Groups[1].Value}\" in English only");
                    }
                }
            }
        }

        Assert.True(literal.Count == 0, string.Join(Environment.NewLine, literal));
    }

    [Fact]
    public void NoAutomationId_IsBlank()
    {
        var blank = new List<string>();

        foreach (var file in XamlFiles())
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in Regex.Matches(lines[i], @"AutomationProperties\.AutomationId\s*=\s*""([^""]*)"""))
                {
                    if (string.IsNullOrWhiteSpace(match.Groups[1].Value))
                    {
                        blank.Add($"  {Relative(file)}:{i + 1} has an empty automation id");
                    }
                }
            }
        }

        Assert.True(blank.Count == 0, string.Join(Environment.NewLine, blank));
    }

    // ------------------------------------------------------------------------------- reading --

    private readonly record struct Element(string Tag, string Raw, int Line);

    /// <summary>
    /// Walks the markup and yields the interactive elements that are not inside a template.
    /// </summary>
    /// <remarks>
    /// A hand-written scan rather than an XML parse, so the failure can name a line number, and
    /// quote-aware because a style selector may contain a <c>&gt;</c> - <c>Selector="MenuItem &gt;
    /// MenuItem"</c> would otherwise cut a tag in half and put the walk out of step.
    /// </remarks>
    private static IEnumerable<Element> InteractiveElements(string markup)
    {
        var open = new Stack<string>();
        int index = 0;

        while (true)
        {
            int start = markup.IndexOf('<', index);
            if (start < 0)
            {
                yield break;
            }

            if (markup.AsSpan(start).StartsWith("<!--"))
            {
                int comment = markup.IndexOf("-->", start, StringComparison.Ordinal);
                index = comment < 0 ? markup.Length : comment + 3;
                continue;
            }

            int end = EndOfTag(markup, start);
            if (end < 0)
            {
                yield break;
            }

            var raw = markup[start..(end + 1)];
            var inner = raw[1..^1].Trim();
            index = end + 1;

            if (inner.StartsWith('?') || inner.StartsWith('!'))
            {
                continue;
            }

            var name = Regex.Match(inner, @"^/?\s*([A-Za-z0-9_:.]+)");
            if (!name.Success)
            {
                continue;
            }

            var tag = name.Groups[1].Value.Split(':')[^1];

            if (inner.StartsWith('/'))
            {
                if (open.Count > 0)
                {
                    open.Pop();
                }

                continue;
            }

            bool selfClosing = inner.EndsWith('/');

            if (Interactive.Contains(tag) && !open.Any(Templates.Contains))
            {
                yield return new Element(tag, raw, LineOf(markup, start));
            }

            if (!selfClosing)
            {
                open.Push(tag);
            }
        }
    }

    /// <summary>The closing angle bracket of the tag opening at <paramref name="start"/>.</summary>
    private static int EndOfTag(string markup, int start)
    {
        char quote = '\0';

        for (int i = start + 1; i < markup.Length; i++)
        {
            char c = markup[i];

            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i;
            }
        }

        return -1;
    }

    private static int LineOf(string markup, int index) =>
        markup.AsSpan(0, index).Count('\n') + 1;

    private static IEnumerable<string> XamlFiles()
    {
        var files = Directory
            .EnumerateFiles(Path.Combine(ProjectDirectory(), "Views"), "*.axaml", SearchOption.AllDirectories)
            .ToList();

        Assert.NotEmpty(files);
        return files;
    }

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
