using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
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
    public void EveryMcpTool_IsRegisteredWithTheServer()
    {
        // A tool implemented and never registered is a tool no agent can see. Nothing reports it:
        // the method compiles, the server starts, and the capability is silently absent.
        var registered = ToolsReferencedByTheServer();
        Assert.NotEmpty(registered);

        var missing = typeof(IMcpToolHandler)
            .GetMethods()
            .Select(method => method.Name)
            .Where(name => !registered.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name => $"  IMcpToolHandler.{name} is implemented but never registered in McpServerHostedService")
            .ToList();

        Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
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

    /// <summary>
    /// The tool-handler methods <see cref="McpServerHostedService"/> hands to the server, read from
    /// its IL.
    /// </summary>
    /// <remarks>
    /// Registration is <c>McpServerTool.Create(_toolHandler.SomeToolAsync, ...)</c>, which compiles
    /// to a method reference rather than to anything reflection can see from outside. The call graph
    /// already built for the coverage rule can read it.
    /// </remarks>
    private static HashSet<string> ToolsReferencedByTheServer()
    {
        var graph = new CallGraph();
        graph.Add(typeof(McpServerHostedService).Assembly.Location);

        var reachable = graph.Reachable([
            CallGraph.Key(typeof(McpServerHostedService).FullName!, "ExecuteAsync"),
            CallGraph.Key(typeof(McpServerHostedService).FullName!, "StartAsync"),
        ]);

        var prefix = CallGraph.Key(typeof(IMcpToolHandler).FullName!, string.Empty);

        return reachable
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(key => key[prefix.Length..])
            .ToHashSet(StringComparer.Ordinal);
    }
}
