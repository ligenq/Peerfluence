using Microsoft.Extensions.DependencyInjection;
using Peerfluence.Services.Mcp;

namespace Peerfluence.Tests.Architecture;

/// <summary>The version and product name advertised through each integration surface.</summary>
public sealed class McpConstantsTests
{
    [Fact]
    public void EveryIntegration_AdvertisesTheBuiltApplicationVersion()
    {
        var assembly = typeof(App).Assembly;
        var expectedVersion = assembly.GetName().Version?.ToString(3);

        Assert.False(string.IsNullOrWhiteSpace(expectedVersion));
        Assert.Equal(expectedVersion, ApplicationVersionInfo.Version);
        Assert.Equal(expectedVersion, McpConstants.Version);
    }

    [Fact]
    public void TheMcpServer_GetsItsVersionFromTheApplicationIdentity()
    {
        // Comparing McpConstants with the assembly is only useful if the server actually reads it.
        // Follow the compiled call graph so replacing the property with another hard-coded MCP
        // version fails here even though every individual value remains valid C#.
        var graph = new CallGraph();
        graph.Add(typeof(McpServerHostedService).Assembly.Location);

        var reached = graph.Reachable([
            CallGraph.Key(typeof(McpServerHostedService).FullName!, "ExecuteAsync")
        ]);

        Assert.Contains(CallGraph.Key(typeof(McpConstants).FullName!, "get_Version"), reached);
    }

    [Fact]
    public void TheHttpUserAgent_AdvertisesTheSameNameAndVersion()
    {
        var services = new ServiceCollection();
        services.AddPeerfluenceServices();
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<HttpClient>();

        var product = Assert.Single(client.DefaultRequestHeaders.UserAgent, value => value.Product is not null);
        Assert.Equal(McpConstants.Name, product.Product!.Name);
        Assert.Equal(ApplicationVersionInfo.Version, product.Product.Version);
    }
}
