using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;
using Peerfluence.Core;
using Peerfluence.Services.Mcp;
using Peerfluence.ViewModels;

namespace Peerfluence.Tests.Architecture;

/// <summary>
/// The hand-maintained lists that have to agree with each other for the application to work, and
/// which the compiler has no opinion about.
/// </summary>
/// <remarks>
/// Every rule here guards a failure that is invisible until someone opens the screen or calls the
/// tool: nothing throws, nothing fails to build, the feature is simply not there. That is the shape
/// of problem an architecture test is for - the ones the compiler already catches do not need one.
/// </remarks>
public sealed class CompositionTests
{
    [Fact]
    public void EveryNavigationPage_HasAViewToShowIt()
    {
        // A view model reaching ViewLocator with no entry renders a TextBlock reading "view not
        // found" where the screen should be. It is a runtime answer to a question that could have
        // been asked here.
        var missing = FeatureViewModels()
            .Where(viewModel => !ViewLocatorMap().ContainsKey(viewModel))
            .Select(viewModel => $"  {viewModel.Name} is a navigation page with no entry in ViewLocator")
            .ToList();

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryNavigationPage_IsRegisteredForFeatureDiscoveryExactlyOnce()
    {
        // MainWindowViewModel builds navigation from IEnumerable<IFeatureViewModel>. Implementing
        // the marker and adding a locator entry are not enough: without this registration the page
        // simply never appears, and registering it twice produces two indistinguishable entries.
        var services = new ServiceCollection();
        services.AddPeerfluenceServices();

        var registered = services
            .Where(descriptor => descriptor.ServiceType == typeof(IFeatureViewModel))
            .Select(ImplementationType)
            .ToList();

        var problems = FeatureViewModels()
            .Select(type => (Type: type, Count: registered.Count(candidate => candidate == type)))
            .Where(item => item.Count != 1)
            .Select(item => $"  {item.Type.Name} is registered {item.Count} times as IFeatureViewModel; expected exactly once")
            .ToList();

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryNavigationPage_LivesAsLongAsTheNavigationItHangsOn()
    {
        // A page is created once and kept on a navigation item for the life of the window. A
        // transient registration does not fail anything - the item holds the instance it was given
        // - but it means a second enumeration silently produces a second copy of the screen, with
        // its own unsaved edits and its own subscriptions. The count rule above cannot see this,
        // because the registration is there either way.
        var services = new ServiceCollection();
        services.AddPeerfluenceServices();

        var wrong = services
            .Where(descriptor => descriptor.ServiceType == typeof(IFeatureViewModel)
                || FeatureViewModels().Contains(descriptor.ServiceType))
            .Where(descriptor => descriptor.Lifetime != ServiceLifetime.Singleton)
            .Select(descriptor =>
                $"  {(descriptor.ImplementationType ?? descriptor.ServiceType).Name} is registered as "
                    + $"{descriptor.Lifetime} against {descriptor.ServiceType.Name}; navigation pages are kept, so they must be Singleton")
            .ToList();

        Assert.True(wrong.Count == 0, string.Join(Environment.NewLine, wrong));
    }

    [Fact]
    public void EveryViewTheLocatorNames_CanBeBuiltByTheContainer()
    {
        // The second half of the same failure. ViewLocator asks the container for the view it
        // mapped to, and a view that was mapped but never registered comes back null - landing on
        // the same "view not found" TextBlock, by a different route.
        var services = new ServiceCollection();
        services.AddPeerfluenceServices();
        var registered = services.Select(descriptor => descriptor.ServiceType).ToHashSet();

        var missing = ViewLocatorMap()
            .Where(pair => !registered.Contains(pair.Value))
            .Select(pair => $"  {pair.Value.Name} is mapped from {pair.Key.Name} but is not registered, so the locator will get null")
            .ToList();

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryViewModelTheLocatorNames_CanBeBuiltByTheContainer()
    {
        // The view half was checked above; the other half matters for pages opened directly rather
        // than through IFeatureViewModel discovery, such as About and the create-torrent dialog.
        var services = new ServiceCollection();
        services.AddPeerfluenceServices();
        var registered = services.Select(descriptor => descriptor.ServiceType).ToHashSet();

        var missing = ViewLocatorMap()
            .Keys
            .Where(viewModel => !registered.Contains(viewModel))
            .Select(viewModel => $"  {viewModel.Name} is mapped by ViewLocator but is not registered")
            .ToList();

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryMcpTool_IsRegisteredWithTheServer()
    {
        // A tool implemented and never registered is a tool no agent can see. Nothing reports it:
        // the method compiles, the server starts, and the capability is silently absent.
        var missing = MissingServerRegistrations(typeof(IMcpToolHandler));

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryUiAgentTool_IsRegisteredWithTheServer()
    {
        var missing = MissingServerRegistrations(typeof(IUiAgentToolHandler));

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryMcpResource_IsRegisteredWithTheServer()
    {
        var missing = MissingServerRegistrations(typeof(IMcpResourceHandler));

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryMcpPrompt_IsRegisteredWithTheServer()
    {
        var missing = MissingServerRegistrations(typeof(IMcpPromptHandler));

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
    }

    [Theory]
    [InlineData("Tool")]
    [InlineData("Resource")]
    [InlineData("Prompt")]
    public void EveryMcpSurfaceName_IsUniqueAndDescribed(string prefix)
    {
        // Duplicate names are accepted by C# and remain invisible until an MCP client connects and
        // the server builds its collections. A missing description is similarly legal but leaves a
        // client unable to tell when to use the capability.
        var fields = typeof(McpConstants)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field is { IsLiteral: true, FieldType: not null })
            .Where(field => field.FieldType == typeof(string))
            .Where(field => field.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Where(field => !field.Name.EndsWith("Description", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(fields);

        var problems = new List<string>();
        foreach (var field in fields)
        {
            var value = (string?)field.GetRawConstantValue();
            if (string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"  McpConstants.{field.Name} is blank");
            }

            var description = typeof(McpConstants).GetField(
                field.Name + "Description",
                BindingFlags.Public | BindingFlags.Static);
            if (description?.GetRawConstantValue() is not string text || string.IsNullOrWhiteSpace(text))
            {
                problems.Add($"  McpConstants.{field.Name} has no non-blank {field.Name}Description");
            }
        }

        problems.AddRange(fields
            .GroupBy(field => (string?)field.GetRawConstantValue(), StringComparer.Ordinal)
            .Where(group => group.Key is not null && group.Count() > 1)
            .Select(group => $"  MCP {prefix.ToLowerInvariant()} name '{group.Key}' is used by {string.Join(", ", group.Select(field => field.Name))}"));

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void EveryPromptIsBuiltInOnePlace_SoOneThingDecidesItsButtonOrder()
    {
        // Dialogs written in markup have their button order read out of the markup, and the ones
        // built in code are all assembled by DialogService, whose order is pinned by a headless
        // test. A dialog put together anywhere else would be covered by neither, and the failure
        // would be a Cancel button on the wrong side that nobody notices until it is shipped.
        var offenders = Directory
            .EnumerateFiles(ProjectDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("WithActionButton", StringComparison.Ordinal))
            .Where(file => Path.GetFileName(file) != "DialogService.cs")
            .Select(file => $"  {Path.GetFileName(file)} builds a dialog of its own; use IDialogService instead")
            .ToList();

        Assert.True(offenders.Count == 0, string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheCoreLibrary_KnowsNothingOfTheUserInterface()
    {
        // Peerfluence.Core is what the Transmission RPC server and the engine run on, and it has to
        // stay runnable with no window. A single using of Avalonia would not fail anything today -
        // the desktop application references both - which is exactly why it needs saying here.
        var forbidden = typeof(Peerfluence.Core.Services.ITorrentService).Assembly
            .GetReferencedAssemblies()
            .Where(reference =>
                reference.Name is not null
                && (reference.Name.StartsWith("Avalonia", StringComparison.Ordinal)
                    || reference.Name.StartsWith("SukiUI", StringComparison.Ordinal)
                    || reference.Name.StartsWith("Material.Icons", StringComparison.Ordinal)))
            .Select(reference => $"  Peerfluence.Core references {reference.Name}")
            .ToList();

        Assert.True(forbidden.Count == 0, string.Join(Environment.NewLine, forbidden));
    }

    // -------------------------------------------------------------------------------- reading --

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

    private static IEnumerable<Type> FeatureViewModels()
    {
        return typeof(ViewModelBase).Assembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .Where(type => typeof(IFeatureViewModel).IsAssignableFrom(type));
    }

    /// <summary>
    /// The locator's view-model-to-view table, which is private because nothing but the locator
    /// should be routing on it.
    /// </summary>
    private static Dictionary<Type, Type> ViewLocatorMap()
    {
        var field = typeof(ViewLocator).GetField(
            "ViewModelToViewMap",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.True(field is not null, "ViewLocator no longer has a ViewModelToViewMap; this test needs updating with it.");

        return (Dictionary<Type, Type>)field!.GetValue(null)!;
    }

    private static List<string> MissingServerRegistrations(Type handlerContract)
    {
        var registered = MethodsReferencedByTheServer(handlerContract);
        Assert.NotEmpty(registered);

        return handlerContract
            .GetMethods()
            .Select(method => method.Name)
            .Where(name => !registered.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => $"  {handlerContract.Name}.{name} is implemented but never registered in McpServerHostedService")
            .ToList();
    }

    /// <summary>
    /// The handler methods <see cref="McpServerHostedService"/> hands to the server, read from its IL.
    /// </summary>
    /// <remarks>
    /// Registration compiles to a method reference rather than to anything reflection can see from
    /// outside. The call graph already built for the coverage rule can read it.
    /// </remarks>
    private static HashSet<string> MethodsReferencedByTheServer(Type handlerContract)
    {
        var graph = new CallGraph();
        graph.Add(typeof(McpServerHostedService).Assembly.Location);

        var reachable = graph.Reachable([
            CallGraph.Key(typeof(McpServerHostedService).FullName!, "ExecuteAsync"),
            CallGraph.Key(typeof(McpServerHostedService).FullName!, "StartAsync"),
        ]);

        var prefix = CallGraph.Key(handlerContract.FullName!, string.Empty);

        return reachable
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(key => key[prefix.Length..])
            .ToHashSet(StringComparer.Ordinal);
    }

    private static Type ImplementationType(ServiceDescriptor descriptor)
    {
        if (descriptor.ImplementationType is { } implementationType)
        {
            return implementationType;
        }

        if (descriptor.ImplementationInstance is { } instance)
        {
            return instance.GetType();
        }

        Assert.NotNull(descriptor.ImplementationFactory);
        return descriptor.ImplementationFactory!(TypeOnlyServiceProvider.Instance).GetType();
    }

    /// <summary>
    /// Runs the tiny feature-registration factories without constructing view models or starting
    /// their background loops. Each factory only asks for its concrete implementation type.
    /// </summary>
    private sealed class TypeOnlyServiceProvider : IServiceProvider
    {
        public static TypeOnlyServiceProvider Instance { get; } = new();

        public object? GetService(Type serviceType)
        {
            return serviceType is { IsClass: true, IsAbstract: false }
                ? RuntimeHelpers.GetUninitializedObject(serviceType)
                : null;
        }
    }
}
