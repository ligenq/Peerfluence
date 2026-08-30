using System.Reflection;
using System.Runtime.CompilerServices;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Architecture;

/// <summary>
/// Requires that code carrying logic has an obvious place for its tests to live, and that every way
/// into that logic is reached by at least one of them.
/// </summary>
/// <remarks>
/// <para>
/// The class rule is a naming convention: <c>TorrentCategoryService</c> is tested by
/// <c>TorrentCategoryServiceTests</c>, in the namespace the production type would have if the test
/// project were the production one. It is worth enforcing because the cost of a missing test is
/// usually not a decision to skip it - it is nobody noticing there was nothing there.
/// </para>
/// <para>
/// The method rule is not a naming convention. It reads the compiled IL and asks what the tests
/// actually reach, so a test may be named after the behaviour it pins down rather than after the
/// method it happens to enter through. See <see cref="CallGraph"/>.
/// </para>
/// </remarks>
public sealed class TestCoverageTests
{
    [Fact]
    public void EveryTypeWithLogic_HasATestClassNamedAfterIt()
    {
        var testTypeNames = TestAssemblyPaths()
            .SelectMany(CallGraph.TypeNames)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ProductionTypes()
            .Where(t => !testTypeNames.Contains(t.Name + "Tests"))
            .OrderBy(t => t.FullName, StringComparer.Ordinal)
            .Select(t => $"  {t.FullName} has no {t.Name}Tests ({TestableMembers(t).Count} members needing one)")
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} types carry logic and have no test class named after them:{Environment.NewLine}"
                + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryPublicOrInternalMember_IsReachedByATest()
    {
        var graph = new CallGraph();
        foreach (var path in ProductionAssemblyPaths().Concat(TestAssemblyPaths()))
        {
            graph.Add(path);
        }

        Assert.True(
            graph.TestEntryPoints.Count > 0,
            "No test methods were found in the compiled assemblies, so nothing could be judged covered.");

        var reached = graph.Reachable(graph.TestEntryPoints);

        // A member that only throws has nothing to test but the throw.
        var onlyThrows = ProductionAssemblyPaths()
            .SelectMany(CallGraph.MethodsThatOnlyThrow)
            .ToHashSet(StringComparer.Ordinal);

        var missing = new List<string>();
        foreach (var type in ProductionTypes())
        {
            foreach (var member in TestableMembers(type))
            {
                if (IsReached(type, member, reached)
                    || onlyThrows.Contains(CallGraph.Key(TypeKey(type), member)))
                {
                    continue;
                }

                missing.Add($"  {type.FullName}.{member}");
            }
        }

        missing.Sort(StringComparer.Ordinal);

        Assert.True(
            missing.Count == 0,
            $"{missing.Count} public or internal members are never reached by a test:{Environment.NewLine}"
                + string.Join(Environment.NewLine, missing));
    }

    /// <summary>
    /// Whether a member is entered by any test, directly or through the interface it is called by.
    /// </summary>
    /// <remarks>
    /// Interfaces matter here because almost nothing in this application is called by its concrete
    /// type: a test holds an <see cref="ITorrentService"/> and the call site in the IL names the
    /// interface method, not the implementation behind it.
    /// </remarks>
    private static bool IsReached(Type type, string member, HashSet<string> reached)
    {
        if (reached.Contains(CallGraph.Key(TypeKey(type), member)))
        {
            return true;
        }

        foreach (var contract in type.GetInterfaces())
        {
            if (contract.Namespace?.StartsWith("Peerfluence", StringComparison.Ordinal) == true
                && reached.Contains(CallGraph.Key(TypeKey(contract), member)))
            {
                return true;
            }
        }

        // A base class declaring the member the caller used, for the same reason as interfaces.
        for (var parent = type.BaseType; parent is not null; parent = parent.BaseType)
        {
            if (reached.Contains(CallGraph.Key(TypeKey(parent), member)))
            {
                return true;
            }
        }

        return false;
    }

    private static string TypeKey(Type type)
    {
        var name = type.FullName ?? type.Name;

        // Reflection writes a nested type as Outer+Inner and a generic as Type`1, both of which is
        // what the metadata reader produces too.
        int generic = name.IndexOf('[');
        return generic < 0 ? name : name[..generic];
    }

    // ---------------------------------------------------------------- the surface under test --

    /// <summary>
    /// The production types a test is expected for.
    /// </summary>
    /// <remarks>
    /// The exclusions are all "there is no logic here to test", never "this would be inconvenient
    /// to test". Anything left out because it is awkward belongs in a test with the awkward part
    /// mocked, not in this list.
    /// </remarks>
    private static IEnumerable<Type> ProductionTypes()
    {
        // Ours, rather than everything that ends up in our assemblies: the coverage collector
        // instruments statically on Linux, which writes a tracker type into the assembly on disk,
        // and the rule then asked for tests for a type nobody wrote and nobody ships.
        return ProductionAssemblies()
            .SelectMany(SafeGetTypes)
            .Where(type => type.Namespace?.StartsWith("Peerfluence", StringComparison.Ordinal) == true)
            .Where(type => !type.IsInterface)
            .Where(type => !type.IsEnum)
            .Where(type => !typeof(Delegate).IsAssignableFrom(type))
            .Where(type => !type.IsNested)
            .Where(type => !IsGenerated(type))
            .Where(type => !typeof(Attribute).IsAssignableFrom(type))
            .Where(type => !IsProcessBoundary(type))
            .Where(type => TestableMembers(type).Count > 0);
    }

    /// <summary>
    /// Whether this is where the process starts or ends, rather than something running inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Excluded because a test cannot enter these without starting the application, not because
    /// they are awkward. <c>Main</c> runs the Avalonia loop and does not return until the window
    /// closes; <c>OnFrameworkInitializationCompleted</c> builds the host and the main window. There
    /// is no seam to mock, because the thing being asked for is the process itself.
    /// </para>
    /// <para>
    /// What can be said about the entry point is said in <see cref="EntryPointTests"/>, which
    /// reflects over <c>Main</c> rather than calling it - that it is synchronous and
    /// <c>[STAThread]</c>, which is what the clipboard depends on.
    /// </para>
    /// </remarks>
    private static bool IsProcessBoundary(Type type)
    {
        // Where the process starts: Main runs the Avalonia loop and does not return until the
        // window closes.
        if (type.Assembly.EntryPoint?.DeclaringType == type)
        {
            return true;
        }

        // Where the UI framework starts. OnFrameworkInitializationCompleted builds the host and the
        // main window.
        if (typeof(Avalonia.Application).IsAssignableFrom(type))
        {
            return true;
        }

        // Where the process ends. HandleException blocks on a native modal dialog - MessageBoxW on
        // Windows, osascript or zenity elsewhere - so calling it from a test hangs the run until
        // somebody clicks OK, and Register installs handlers on AppDomain.UnhandledException and
        // TaskScheduler.UnobservedTaskException that would then intercept every later test. There is
        // also a static latch making it one-shot per process. Excluded for the same reason as the
        // entry point rather than for being awkward: a test cannot enter it without taking the
        // process with it.
        if (type == typeof(Peerfluence.CrashHandler))
        {
            return true;
        }

        // Where the application removes itself. Run deletes the real profile directories and the
        // registry associations, resolved from the machine's own special folders with no seam to
        // point elsewhere - calling it from a test destroys the developer's own installation, which
        // is exactly what it did once. The decisions it is made of are tested: GetTraceDirectories
        // for what is removed, DeleteDirectories for how.
        return type == typeof(Peerfluence.Services.UninstallCleanup);
    }

    private static bool IsGenerated(Type type)
    {
        return type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
            || type.IsDefined(typeof(System.CodeDom.Compiler.GeneratedCodeAttribute), inherit: false);
    }

    /// <summary>
    /// The members of a type that a test would be written against: what it declares itself, public
    /// or internal, that carries logic of its own.
    /// </summary>
    private static List<string> TestableMembers(Type type)
    {
        return type
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => method.IsPublic || method.IsAssembly)
            .Where(IsWorthTesting)
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Whether a member exists to be called by a framework rather than by this application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An override of something declared in a type we do not own is entered by whoever owns it -
    /// Avalonia calls <c>Render</c>, not us - so no call graph rooted in the tests can ever reach
    /// it. Requiring one would mean calling it artificially, and the better test already exists:
    /// <c>PieceMapControlTests</c> shows a window and lets the framework render it, which exercises
    /// far more than a direct call would.
    /// </para>
    /// <para>
    /// Deliberately narrow: it is the override that is excused, not the type. Everything else the
    /// control declares is still expected to be reached.
    /// </para>
    /// </remarks>
    private static bool IsFrameworkCallback(MethodInfo method)
    {
        if (!method.IsVirtual || method.GetBaseDefinition() == method)
        {
            return false;
        }

        var declaring = method.GetBaseDefinition().DeclaringType;
        return declaring is not null && !ProductionAssemblies().Contains(declaring.Assembly);
    }

    private static bool IsWorthTesting(MethodInfo method)
    {
        if (IsFrameworkCallback(method))
        {
            return false;
        }

        // An auto-property's accessor and a record's generated members hold no logic. A property
        // that computes something is not compiler-generated and does count - IsBindAddressValid
        // decides whether an address can be bound, and that is exactly the sort of thing that
        // should not go untested for being spelled as a property.
        if (method.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
        {
            return false;
        }

        // Generated by the XAML compiler, and calling it is what every view test already does.
        if (method.Name == "InitializeComponent")
        {
            return false;
        }

        // Operators, and the accessors of events.
        if (method.IsSpecialName && !method.Name.StartsWith("get_", StringComparison.Ordinal)
                                 && !method.Name.StartsWith("set_", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    // ------------------------------------------------------------------------- the assemblies --

    private static Assembly[] ProductionAssemblies() =>
        [typeof(ITorrentService).Assembly, typeof(Peerfluence.ServiceCollectionExtensions).Assembly];

    private static IEnumerable<string> ProductionAssemblyPaths() =>
        ProductionAssemblies().Select(a => a.Location);

    /// <summary>
    /// Both test assemblies, including the headless one this project cannot reference.
    /// </summary>
    /// <remarks>
    /// It is found on disk rather than referenced: referencing it would drag Avalonia's headless
    /// platform into this project and offer its tests up for discovery twice. Not finding it fails
    /// loudly, because quietly carrying on would report every view model as untested.
    /// </remarks>
    private static IEnumerable<string> TestAssemblyPaths()
    {
        yield return typeof(TestCoverageTests).Assembly.Location;

        var headless = FindHeadlessTestAssembly();
        Assert.True(
            headless is not null,
            "Peerfluence.HeadlessTests.dll was not found. Build the whole solution before running this test: "
                + "without it every view model looks untested.");

        yield return headless!;
    }

    private static string? FindHeadlessTestAssembly()
    {
        // bin/<Configuration>/<TargetFramework> under each project, so the sibling project's copy
        // is at the same depth from the repository root.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        var tail = Path.Combine(
            "Peerfluence.HeadlessTests",
            "bin",
            directory.Parent?.Name ?? "Debug",
            directory.Name,
            "Peerfluence.HeadlessTests.dll");

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, tail);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null)!;
        }
    }
}
