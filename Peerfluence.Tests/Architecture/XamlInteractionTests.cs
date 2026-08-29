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
    public void EveryModalWindow_PresentsCancelBeforeItsDefaultAction()
    {
        // A house convention rather than a correctness rule, and worth saying so: Microsoft's own
        // Win32 guidance puts the affirmative button first, and this puts it last. Both dialogs
        // already did it this way, every other row in the application reads left to right towards
        // the action being committed, and one order applied everywhere is worth more than the
        // choice between them. Flipping it is this comment and two lines of markup.
        //
        // Source order is also visual and keyboard traversal order for the horizontal action
        // panels Peerfluence uses.
        var problems = new List<string>();

        foreach (var file in ModalWindows())
        {
            var buttons = Load(file).Descendants().Where(IsButton).ToList();
            var cancel = buttons.FindIndex(button => IsTrue(button, "IsCancel"));
            var primary = buttons.FindIndex(button => IsTrue(button, "IsDefault"));

            if (cancel >= 0 && primary >= 0 && cancel > primary)
            {
                problems.Add($"  {Relative(file)} declares its default action before Cancel");
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
}
