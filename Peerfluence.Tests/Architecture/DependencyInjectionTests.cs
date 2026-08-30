using Peerfluence.Core.Services;
using Peerfluence.Services.Mcp;
using Peerfluence.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Peerfluence.Tests.Architecture;

public class DependencyInjectionTests
{
    [Fact]
    public void Services_MustOnlyInjectAbstractions_InConstructors()
    {
        var assembly = typeof(ServiceCollectionExtensions).Assembly;

        var serviceTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && (t.GetInterfaces().Any(i =>
                i.Namespace?.StartsWith("Peerfluence", StringComparison.Ordinal) == true
                || i.Namespace?.StartsWith("PeerSharp", StringComparison.Ordinal) == true)
                || typeof(ViewModelBase).IsAssignableFrom(t))
                && !t.IsDefined(typeof(ExcludeFromDIAttribute), inherit: true))
            .ToList();

        var errors = new List<string>();

        foreach (var type in serviceTypes)
        {
            var constructors = type.GetConstructors();
            foreach (var ctor in constructors)
            {
                foreach (var param in ctor.GetParameters())
                {
                    var paramType = param.ParameterType;

                    // Allow primitives, value types, and strings
                    if (paramType.IsPrimitive || paramType.IsValueType || paramType == typeof(string))
                        continue;

                    // Allow arrays/enumerables of interfaces
                    if (paramType.IsArray && paramType.GetElementType()!.IsInterface)
                        continue;

                    if (paramType.IsGenericType && paramType.GetGenericTypeDefinition() == typeof(IEnumerable<>) && paramType.GetGenericArguments()[0].IsInterface)
                        continue;

                    // Allow specific delegates if necessary
                    if (typeof(Delegate).IsAssignableFrom(paramType))
                        continue;

                    // Allow ViewModels
                    if (typeof(ViewModelBase).IsAssignableFrom(paramType))
                        continue;

                    // Interfaces and abstract framework abstractions such as TimeProvider are both
                    // replaceable in tests and cannot accidentally couple a service to an
                    // implementation.
                    if (!paramType.IsInterface && !paramType.IsAbstract)
                    {
                        errors.Add($"Violation in '{type.Name}': Constructor injects concrete type '{paramType.Name}'. Must use an abstraction.");
                    }
                }
            }
        }

        Assert.True(errors.Count == 0, string.Join("\n", errors));
    }

    [Fact]
    public void ServiceCollection_CanResolveCoreServices_AndHostedServices()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddPeerfluenceServices();

        using var host = builder.Build();
        var provider = host.Services;

        Assert.NotNull(provider.GetRequiredService<IAppSettingsService>());
        Assert.NotNull(provider.GetRequiredService<ITorrentEngineService>());
        Assert.NotNull(provider.GetRequiredService<ITorrentService>());
        Assert.NotNull(provider.GetRequiredService<ITorrentSearchService>());
        Assert.NotNull(provider.GetRequiredService<IMcpToolHandler>());
        Assert.NotNull(provider.GetRequiredService<IMcpResourceHandler>());
        Assert.NotNull(provider.GetRequiredService<IUiAgentToolHandler>());
        Assert.NotNull(provider.GetRequiredService<IUiAgentTimeline>());
        Assert.NotNull(provider.GetRequiredService<IMcpRuntimeOptions>());

        var hostedServices = provider.GetServices<IHostedService>().ToList();
        Assert.True(hostedServices.Count >= 5);
    }

    [Fact]
    public void EveryRegisteredConstructionPath_IsValid()
    {
        // Host validation defaults vary with environment. Make it unconditional here so a missing
        // dependency in a lazily-created page, dialog or server fails this test rather than the
        // first time somebody opens or enables it.
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddPeerfluenceServices();

        using var provider = builder.Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        Assert.NotNull(provider);
    }
}
