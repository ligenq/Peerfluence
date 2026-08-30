using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Peerfluence.Tests.Architecture;

/// <summary>
/// Protects interaction and layering conventions that live only in AXAML. These rules deliberately
/// avoid pixels and theme details: they cover behavior that otherwise fails silently for keyboard,
/// accessibility and localization users.
/// </summary>
public sealed class XamlInteractionTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly HashSet<string> UserFacingAttributes =
    [
        "Content", "Header", "PlaceholderText", "Text", "Title", "ToolTip.Tip"
    ];

    /// <summary>
    /// The one choice chip still doing its work in a command.
    /// </summary>
    /// <remarks>
    /// It sits in a DataTemplate over a bare string, so there is nothing for a two way IsChecked to
    /// bind to; it needs an item view model of its own first. Listed here rather than passed over,
    /// so the gap is visible and this rule stays honest about what it does not yet cover.
    /// </remarks>
    private const string TheCategoryChipStillToDo = "SetCategoryFilterCommand";

    [Fact]
    public void EveryDataTemplate_SaysWhatItIsGiven()
    {
        // The project compiles its bindings, which is what turns a renamed property into a build
        // error instead of a blank column. Inside a DataTemplate the DataContext is the item rather
        // than the view, so the compiler cannot work out the type on its own: without x:DataType it
        // falls back to binding by reflection, and the checking silently stops applying exactly
        // where it is hardest to notice - list rows and grid cells.
        //
        // Half the templates here were doing that. The build was green either way.
        var untyped = new List<string>();

        foreach (var file in Directory.EnumerateFiles(ViewsDirectory(), "*.axaml", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var start = lines[i].IndexOf("<DataTemplate", StringComparison.Ordinal);
                if (start < 0)
                {
                    continue;
                }

                var end = EndOfTag(lines[i], start);
                var tag = end < 0 ? lines[i][start..] : lines[i][start..(end + 1)];

                if (!tag.Contains("x:DataType", StringComparison.Ordinal))
                {
                    untyped.Add($"  {Path.GetFileName(file)}:{i + 1} declares a DataTemplate without "
                        + "x:DataType, so its bindings are not compiled and not checked.");
                }
            }
        }

        Assert.True(untyped.Count == 0, string.Join(Environment.NewLine, untyped));
    }

    [Fact]
    public void EveryBoundPropertyThatCanChange_SaysWhenItDoes()
    {
        // A compiled binding proves the property exists. Nothing proves it tells anyone when it
        // changes, and a property that quietly does not is the oldest bug in this framework: the
        // screen is simply wrong, with nothing in the log and nothing to catch.
        var silent = new List<string>();

        foreach (var file in Directory.EnumerateFiles(ViewsDirectory(), "*.axaml", SearchOption.AllDirectories))
        {
            var markup = File.ReadAllText(file);
            var dataType = Regex.Match(markup, @"x:DataType=""vm:(\w+)""");
            if (!dataType.Success)
            {
                continue;
            }

            var viewModel = Path.Combine(ProjectDirectory(), "ViewModels", dataType.Groups[1].Value + ".cs");
            if (!File.Exists(viewModel))
            {
                continue;
            }

            var source = File.ReadAllText(viewModel);

            foreach (var name in Regex.Matches(markup, @"\{Binding (\w+)[,}]")
                         .Select(match => match.Groups[1].Value)
                         .Distinct(StringComparer.Ordinal))
            {
                if (!TryFindProperty(source, name, out var kind, out var body))
                {
                    continue;
                }

                // Read only, so it cannot change behind the view's back.
                if (!Regex.IsMatch(body, @"\bset\b"))
                {
                    continue;
                }

                // Announced by the setter, or by whichever method changes the state behind it.
                if (body.Contains("SetProperty", StringComparison.Ordinal)
                    || body.Contains("OnPropertyChanged", StringComparison.Ordinal)
                    || source.Contains($"OnPropertyChanged(nameof({name}))", StringComparison.Ordinal))
                {
                    continue;
                }

                // A command is handed over once and never replaced.
                if (kind.Contains("Command", StringComparison.Ordinal))
                {
                    continue;
                }

                silent.Add($"  {dataType.Groups[1].Value}.{name} is bound in {Path.GetFileName(file)} and "
                    + "can be set, but never raises a change. The view will show whatever it saw first.");
            }
        }

        Assert.True(silent.Count == 0, string.Join(Environment.NewLine, silent));
    }

    [Fact]
    public void EveryColumnThatFormatsItsValue_BindsOneWay()
    {
        // A DataGridTextColumn binds two ways unless told otherwise, and the grid asks the converter
        // for the way back merely to show a row - on a grid that is read only, with nobody editing
        // anything. The display converters have nothing to answer with, so a size column left at the
        // default mode used to write a display string into the source, and later throw.
        //
        // Reproduced rather than reasoned about: ByteSizeConverterBindingTests shows the default
        // binding asking and the one way binding not asking.
        var offences = new List<string>();

        foreach (var file in Directory.EnumerateFiles(ViewsDirectory(), "*.axaml", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var start = line.IndexOf("Binding=\"{Binding ", StringComparison.Ordinal);
                if (start < 0)
                {
                    continue;
                }

                var end = line.IndexOf("}\"", start, StringComparison.Ordinal);
                if (end < 0)
                {
                    continue;
                }

                var binding = line[start..end];
                if (!binding.Contains("Converter=", StringComparison.Ordinal)
                    || binding.Contains("Mode=OneWay", StringComparison.Ordinal))
                {
                    continue;
                }

                offences.Add(
                    $"  {Path.GetFileName(file)}:{i + 1} binds a column through a converter without "
                        + "Mode=OneWay, so the grid will ask that converter to convert back.");
            }
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    [Fact]
    public void NoChoiceChip_DoesItsWorkInACommand()
    {
        // A RadioButton bound to a Command only acts when it is clicked. Avalonia answers a
        // selection made through the accessibility API - which is how a screen reader chooses one -
        // by moving the dot without raising Click, so the interface ends up showing a choice that
        // was never made. Driving it from a two way IsChecked binding instead means every way of
        // choosing arrives in the same place.
        //
        // Verified against the running application rather than assumed: see the end to end test
        // AChoiceChip_CanBeMadeTheWayAssistiveTechnologyMakesIt.
        var offences = new List<string>();

        foreach (var file in Directory.EnumerateFiles(ViewsDirectory(), "*.axaml", SearchOption.AllDirectories))
        {
            var markup = File.ReadAllText(file);

            foreach (Match match in Regex.Matches(markup, @"<RadioButton[^>]*>", RegexOptions.Singleline))
            {
                if (!match.Value.Contains("Command=", StringComparison.Ordinal)
                    || match.Value.Contains(TheCategoryChipStillToDo, StringComparison.Ordinal))
                {
                    continue;
                }

                offences.Add(
                    $"  {Path.GetFileName(file)}: a RadioButton acts through a Command, so it cannot "
                        + "be chosen through the accessibility API. Bind IsChecked TwoWay instead.");
            }

            foreach (Match match in Regex.Matches(markup, @"<RadioButton[^>]*>", RegexOptions.Singleline))
            {
                if (match.Value.Contains("IsChecked=", StringComparison.Ordinal)
                    && match.Value.Contains("Mode=OneWay", StringComparison.Ordinal))
                {
                    offences.Add(
                        $"  {Path.GetFileName(file)}: a RadioButton binds IsChecked one way, so "
                            + "choosing it cannot reach the view model.");
                }
            }
        }

        Assert.True(offences.Count == 0, string.Join(Environment.NewLine, offences));
    }

    [Fact]
    public void EveryModalWindow_HasOneKeyboardDefaultAndCancelAction()
    {
        // Avalonia maps IsDefault to Enter and IsCancel to Escape. Requiring commands as well means
        // the key has an action to invoke instead of merely giving the button a semantic label.
        var problems = new List<string>();

        foreach (var file in ModalWindows())
        {
            var buttons = Load(file).Descendants().Where(IsButton).ToList();
            var defaults = buttons.Where(button => IsTrue(button, "IsDefault")).ToList();
            var cancels = buttons.Where(button => IsTrue(button, "IsCancel")).ToList();

            if (defaults.Count != 1)
            {
                problems.Add($"  {Relative(file)} has {defaults.Count} default buttons; expected exactly one");
            }

            if (cancels.Count != 1)
            {
                problems.Add($"  {Relative(file)} has {cancels.Count} cancel buttons; expected exactly one");
            }

            foreach (var button in defaults.Concat(cancels))
            {
                if (string.IsNullOrWhiteSpace(Attribute(button, "Command")))
                {
                    problems.Add($"  {Relative(file)}:{Line(button)} {ActionName(button)} has no Command");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryModalWindow_PresentsItsDefaultActionBeforeCancel()
    {
        // Windows puts the affirmative action on the left and the safe one on the right, and this
        // is current guidance rather than inherited Win32 lore. Microsoft's dialog documentation for
        // the Windows App SDK says it twice: "the 'do it' action button(s) should appear as the
        // leftmost buttons. The safe, nondestructive action should appear as the rightmost button",
        // and of ContentDialog's own buttons, "CloseButton ... appears as the rightmost button.
        // PrimaryButton ... appears as the leftmost button".
        //
        // macOS is the other way round, which is why the opposite arrangement looks reasonable and
        // why both dialogs here were written that way before anybody checked. Peerfluence installs
        // and associates itself on Windows, so it follows Windows.
        //
        // Source order is also visual and keyboard traversal order for the horizontal action panels
        // Peerfluence uses, so this fixes all three at once.
        var problems = new List<string>();

        foreach (var file in ModalWindows())
        {
            var buttons = Load(file).Descendants().Where(IsButton).ToList();
            var cancel = buttons.FindIndex(button => IsTrue(button, "IsCancel"));
            var primary = buttons.FindIndex(button => IsTrue(button, "IsDefault"));

            if (cancel >= 0 && primary >= 0 && primary > cancel)
            {
                problems.Add($"  {Relative(file)} declares Cancel before its default action");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryModalWindow_CentersOnItsOwner()
    {
        var problems = ModalWindows()
            .Select(file => (File: file, Root: Load(file).Root!))
            .Where(item => Attribute(item.Root, "WindowStartupLocation") != "CenterOwner")
            .Select(item => $"  {Relative(item.File)} must use WindowStartupLocation=\"CenterOwner\"")
            .ToList();

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryView_DeclaresItsClassAndCompiledBindingContext()
    {
        var problems = new List<string>();

        foreach (var file in ViewFiles())
        {
            var root = Load(file).Root!;
            if (string.IsNullOrWhiteSpace((string?)root.Attribute(Xaml + "Class")))
            {
                problems.Add($"  {Relative(file)} has no x:Class");
            }

            if (string.IsNullOrWhiteSpace((string?)root.Attribute(Xaml + "DataType")))
            {
                problems.Add($"  {Relative(file)} has no root x:DataType for compiled bindings");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void NoView_HardcodesUserFacingText()
    {
        var problems = new List<string>();

        foreach (var file in ViewFiles())
        {
            foreach (var attribute in Load(file).Root!.DescendantsAndSelf().Attributes())
            {
                if (!UserFacingAttributes.Contains(attribute.Name.LocalName)
                    || attribute.Value.TrimStart().StartsWith('{')
                    || IsLanguageNeutralExample(attribute))
                {
                    continue;
                }

                problems.Add(
                    $"  {Relative(file)}:{Line(attribute.Parent!)} {attribute.Name.LocalName}=\"{attribute.Value}\" is not localized");
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryIconOnlyButton_HasATooltip()
    {
        var problems = new List<string>();

        foreach (var file in ViewFiles())
        {
            foreach (var button in Load(file).Descendants().Where(IsButton))
            {
                var hasContent = !string.IsNullOrWhiteSpace(Attribute(button, "Content"));
                var hasTextChild = button.Descendants()
                    .Where(element => element.Name.LocalName == "TextBlock")
                    .Any(element => !string.IsNullOrWhiteSpace(Attribute(element, "Text")));
                var hasAccessibleLabel = !string.IsNullOrWhiteSpace(Attribute(button, "AutomationProperties.Name"));
                var hasTooltip = !string.IsNullOrWhiteSpace(Attribute(button, "ToolTip.Tip"));

                if (!hasContent && !hasTextChild && !hasAccessibleLabel && !hasTooltip)
                {
                    problems.Add($"  {Relative(file)}:{Line(button)} icon-only button has no tooltip or accessible name");
                }
            }
        }

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheWelcomeOverlay_RendersAboveEverythingItCovers()
    {
        // The invariant, not the arrangement. The welcome screen has to intercept the shell while
        // it is showing, or the first thing a new user sees is a window they can click through to
        // controls that are not meant to be reachable yet.
        //
        // Found by what it is bound to rather than by a name or a position, so the markup stays
        // free to be rearranged. An earlier version of this test required a VisualLayerManager
        // wrapping a Panel called ShellLayers holding a Border called WelcomeOverlay, which is a
        // description of one file on one day rather than a rule.
        var document = Load(Path.Combine(ViewsDirectory(), "MainWindowView.axaml"));

        var overlay = document.Descendants().Single(element =>
            Attribute(element, "IsVisible") is { } visibility
            && visibility.Contains("IsWelcomeVisible", StringComparison.Ordinal));

        var siblings = overlay.Parent!.Elements().Where(element => element != overlay).ToList();
        Assert.NotEmpty(siblings);

        var covered = siblings
            .Where(sibling => !RendersAbove(overlay, sibling))
            .Select(sibling => $"  <{sibling.Name.LocalName}> at line {Line(sibling)} renders over the welcome overlay")
            .ToList();

        Assert.True(covered.Count == 0, string.Join(Environment.NewLine, covered));
    }

    /// <summary>
    /// Whether <paramref name="element"/> draws over <paramref name="other"/>.
    /// </summary>
    /// <remarks>
    /// Avalonia orders siblings by ZIndex first and by document order second, so a later child wins
    /// a tie. Both halves are checked, which lets the markup say it either way.
    /// </remarks>
    private static bool RendersAbove(XElement element, XElement other)
    {
        int here = ZIndexOf(element);
        int there = ZIndexOf(other);

        if (here != there)
        {
            return here > there;
        }

        return element.ElementsBeforeSelf().Count() > other.ElementsBeforeSelf().Count();
    }

    private static int ZIndexOf(XElement element) =>
        int.TryParse(Attribute(element, "ZIndex"), out var value) ? value : 0;

    private static bool IsButton(XElement element) => element.Name.LocalName == "Button";

    private static bool IsTrue(XElement element, string name) =>
        bool.TryParse(Attribute(element, name), out var value) && value;

    private static string? Attribute(XElement element, string localName) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;

    private static string ActionName(XElement button) =>
        Attribute(button, "AutomationProperties.AutomationId") ?? $"button at line {Line(button)}";

    /// <summary>
    /// Whether a literal says the same thing in every language.
    /// </summary>
    /// <remarks>
    /// An address shown as an example of what to type is not prose, and there is nothing to
    /// translate in <c>192.168.1.50</c>. Stated as a property of the value rather than as the value
    /// itself, so the next example does not have to be added to this test by hand.
    /// </remarks>
    private static bool IsLanguageNeutralExample(XAttribute attribute) =>
        attribute.Name.LocalName == "PlaceholderText"
        && System.Net.IPAddress.TryParse(attribute.Value, out _);

    private static int Line(XElement element)
    {
        var lineInfo = (IXmlLineInfo)element;
        return lineInfo.HasLineInfo() ? lineInfo.LineNumber : 0;
    }

    private static XDocument Load(string file) => XDocument.Load(file, LoadOptions.SetLineInfo);

    private static IEnumerable<string> ModalWindows()
    {
        var files = Directory.EnumerateFiles(ViewsDirectory(), "*Window.axaml").ToList();
        Assert.NotEmpty(files);
        return files;
    }

    private static IEnumerable<string> ViewFiles()
    {
        var files = Directory.EnumerateFiles(ViewsDirectory(), "*.axaml", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(files);
        return files;
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

    /// <summary>
    /// The declared type and body of a property, however it is written.
    /// </summary>
    /// <remarks>
    /// Brace counting rather than a pattern for the shape, because the first version of this matched
    /// only properties written across several lines - and a one-line <c>{ get; set; }</c> is both the
    /// commonest way to write one and the likeliest to be the bug this rule is looking for. It found
    /// nothing until it was asked the question properly.
    /// </remarks>
    private static bool TryFindProperty(string source, string name, out string kind, out string body)
    {
        kind = string.Empty;
        body = string.Empty;

        var declaration = Regex.Match(source, @"(?:public|internal) ([\w<>?\[\]\. ]+?) " + Regex.Escape(name) + @"\s*\{");
        if (!declaration.Success)
        {
            return false;
        }

        int open = source.IndexOf('{', declaration.Index);
        int depth = 0;

        for (int i = open; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    kind = declaration.Groups[1].Value.Trim();
                    body = source[(open + 1)..i];
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The closing angle bracket of the tag opening at <paramref name="start"/>.</summary>
    private static int EndOfTag(string line, int start)
    {
        char quote = '\0';

        for (int i = start + 1; i < line.Length; i++)
        {
            char c = line[i];

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
}
