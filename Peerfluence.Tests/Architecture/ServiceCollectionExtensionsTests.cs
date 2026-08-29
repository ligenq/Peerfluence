using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.Services.Mcp;

namespace Peerfluence.Tests.Architecture;

/// <summary>
/// What one call to <c>AddPeerfluenceServices</c> puts in the container.
/// </summary>
/// <remarks>
/// <see cref="DependencyInjectionTests"/> covers the rules the registrations have to follow -
/// interfaces in constructors, and a graph that resolves. This covers the registrations themselves:
/// that the services with one instance really do have one, since several of them hold state the
/// rest of the application assumes is shared.
/// </remarks>
public sealed class ServiceCollectionExtensionsTests
{
    private static IServiceProvider Build()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddPeerfluenceServices();
        return builder.Build().Services;
    }

    [Fact]
    public void TheEngineAndItsSettings_AreOneInstanceEach()
    {
        // Two engines would mean two listeners on one port, and two settings services would let a
        // change made on one screen be invisible to the next.
        var provider = Build();

        Assert.Same(
            provider.GetRequiredService<ITorrentEngineService>(),
            provider.GetRequiredService<ITorrentEngineService>());
        Assert.Same(
            provider.GetRequiredService<IAppSettingsService>(),
            provider.GetRequiredService<IAppSettingsService>());
    }

    [Fact]
    public void TheSelectionAndNotificationServices_AreShared()
    {
        // The details pane and the list have to agree on which torrent is selected, and a
        // notification published anywhere has to reach the one list the window shows.
        var provider = Build();

        Assert.Same(
            provider.GetRequiredService<ITorrentSelectionService>(),
            provider.GetRequiredService<ITorrentSelectionService>());
        Assert.Same(
            provider.GetRequiredService<INotificationService>(),
            provider.GetRequiredService<INotificationService>());
    }

    [Fact]
    public void TheMetricsReader_IsRegisteredAndShared()
    {
        // It holds a MeterListener subscription, so a second one would mean a second subscription
        // to the same instruments.
        var provider = Build();

        Assert.Same(
            provider.GetRequiredService<IEngineMetricsReader>(),
            provider.GetRequiredService<IEngineMetricsReader>());
    }

    [Fact]
    public void EveryHostedService_IsRegisteredOnlyOnce()
    {
        // Registering one twice starts it twice, which for the ones that bind a port or a pipe
        // means the second copy fails at startup.
        var provider = Build();

        var duplicated = provider.GetServices<IHostedService>()
            .GroupBy(service => service.GetType())
            .Where(group => group.Count() > 1)
            .Select(group => group.Key.Name)
            .ToList();

        Assert.True(duplicated.Count == 0, $"registered more than once: {string.Join(", ", duplicated)}");
    }

    [Fact]
    public void HostedServices_AreRegisteredAfterEverythingTheyConsume()
    {
        // The generic host starts services in registration order. Settings have to exist before
        // anyone reads them; notification consumers must subscribe before the engine restores its
        // session; and servers must not accept work until the engine's alert stream is running.
        var provider = Build();
        var order = provider.GetServices<IHostedService>()
            .Select(service => service.GetType())
            .ToList();

        AssertBefore<AppSettingsHostedService, TorrentNotificationHostedService>(order);
        AssertBefore<AppSettingsHostedService, TorrentCompletionActionHostedService>(order);
        AssertBefore<AppSettingsHostedService, TorrentEngineHostedService>(order);
        AssertBefore<TorrentNotificationHostedService, TorrentEngineHostedService>(order);
        AssertBefore<TorrentCompletionActionHostedService, TorrentEngineHostedService>(order);
        AssertBefore<TorrentEngineHostedService, TorrentAlertsHostedService>(order);
        AssertBefore<TorrentAlertsHostedService, McpServerHostedService>(order);
        AssertBefore<TorrentAlertsHostedService, TransmissionRpcHostedService>(order);
    }

    [Fact]
    public void HostedServices_RunSequentially_SoTheirDependencyOrderIsKept()
    {
        // Registration order is a guarantee only while concurrent lifecycle execution remains off.
        // The host also stops sequentially in reverse order, taking servers down before the engine.
        var provider = Build();
        var options = provider.GetRequiredService<IOptions<HostOptions>>().Value;

        Assert.False(options.ServicesStartConcurrently);
        Assert.False(options.ServicesStopConcurrently);
    }

    private static void AssertBefore<TFirst, TSecond>(IList<Type> order)
    {
        var first = order.IndexOf(typeof(TFirst));
        var second = order.IndexOf(typeof(TSecond));

        Assert.True(first >= 0, $"{typeof(TFirst).Name} is not registered as a hosted service");
        Assert.True(second >= 0, $"{typeof(TSecond).Name} is not registered as a hosted service");
        Assert.True(
            first < second,
            $"{typeof(TFirst).Name} must start before {typeof(TSecond).Name}; actual order: {string.Join(" -> ", order.Select(type => type.Name))}");
    }
}
