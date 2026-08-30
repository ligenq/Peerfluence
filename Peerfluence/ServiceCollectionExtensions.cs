using System;
using System.IO.Abstractions;
using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using Peerfluence.Core.Services.Rpc;
using Peerfluence.Services.Mcp;
using Peerfluence.ViewModels;
using Peerfluence.Views;

namespace Peerfluence;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPeerfluenceServices(
        this IServiceCollection services,
        IMcpRuntimeOptions? mcpRuntimeOptions = null,
        IAppPaths? appPaths = null)
    {
        return services
            .AddInfrastructure(mcpRuntimeOptions, appPaths)
            .AddCoreServices()
            .AddMcpHandlers()
            .AddViewModels()
            .AddViews()
            .AddHostedServices()
            .AddDialogRegistrations();
    }

    private static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IMcpRuntimeOptions? mcpRuntimeOptions,
        IAppPaths? appPaths)
    {
        services.AddSingleton(mcpRuntimeOptions ?? new McpRuntimeOptions());
        services.AddSingleton<IFileSystem, FileSystem>();
        services.AddSingleton(appPaths ?? new AppPaths());
        services.AddSingleton<IAppSettingsStore, JsonAppSettingsStore>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<IAppMessenger, AppMessenger>();
        return services;
    }

    private static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        // One instance behind two interfaces: the window sets the host, everything else asks for
        // a prompt.
        services.AddSingleton<DialogService>();
        services.AddSingleton<IDialogService>(sp => sp.GetRequiredService<DialogService>());
        services.AddSingleton<IDialogHost>(sp => sp.GetRequiredService<DialogService>());
        services.AddSingleton<IAddTorrentDialogService, AddTorrentDialogService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IMagnetMetadataPreviewService, MagnetMetadataPreviewService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<ISingleInstanceService, SingleInstanceService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ITopLevelService, TopLevelService>();
        services.AddSingleton<ICompletionActionRunner, CompletionActionRunner>();
        services.AddSingleton<ITorrentEngineService, TorrentEngineService>();
        services.AddSingleton<IEngineMetricsReader, EngineMetricsReader>();
        services.AddSingleton<IInterfaceModeService, InterfaceModeService>();
        services.AddSingleton<ITorrentSelectionService, TorrentSelectionService>();
        services.AddSingleton<ITorrentCategoryService, TorrentCategoryService>();
        services.AddSingleton<ITorrentTransferSnapshots, TorrentTransferSnapshots>();
        services.AddSingleton<ITransmissionRpcHandler>(sp => new TransmissionRpcHandler(
            sp.GetRequiredService<ITorrentService>(),
            sp.GetRequiredService<IAppSettingsService>(),
            sp.GetRequiredService<ITorrentTransferSnapshots>(),
            sp.GetRequiredService<ITorrentCategoryService>(),
            ApplicationVersionInfo.Version));

        // One client for the lifetime of the app rather than one per search: a new HttpClient per
        // call leaves sockets in TIME_WAIT, and searching is something people do repeatedly. Fifteen
        // seconds because a Torznab endpoint is normally on this machine, and one that has not
        // answered by then is not going to.
        //
        // Named rather than anonymous: an indexer being asked questions by this is entitled to
        // know what is asking, and a bare .NET user agent tells whoever runs it nothing.
        services.AddSingleton(_ =>
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                $"Peerfluence/{ApplicationVersionInfo.Version} (+https://github.com/ligenq/Peerfluence)");
            return client;
        });

        services.AddSingleton<ITorrentSearchService, TorznabSearchService>();
        services.AddSingleton<ITorrentService, TorrentService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<IWindowsAssociationService, WindowsAssociationService>();
        return services;
    }

    private static IServiceCollection AddMcpHandlers(this IServiceCollection services)
    {
        services.AddSingleton<IMcpPromptHandler, McpPromptHandler>();
        services.AddSingleton<IMcpResourceHandler, McpResourceHandler>();
        services.AddSingleton<IMcpToolHandler, McpToolHandler>();
        services.AddSingleton<IUiAgentTimeline, UiAgentTimeline>();
        services.AddSingleton<IUiAgentToolHandler, UiAgentToolHandler>();
        return services;
    }

    private static IServiceCollection AddViewModels(this IServiceCollection services)
    {
        // Singletons
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<DownloadsViewModel>();
        services.AddSingleton<DetailsViewModel>();

        // Transients
        services.AddSingleton<SettingsViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<CreateTorrentViewModel>();
        services.AddSingleton<FindTorrentsViewModel>();
        services.AddSingleton<StatisticsViewModel>();

        // IFeatureViewModel discovery (order matters for navigation)
        services.AddSingleton<IFeatureViewModel>(sp => sp.GetRequiredService<DownloadsViewModel>());
        services.AddSingleton<IFeatureViewModel>(sp => sp.GetRequiredService<FindTorrentsViewModel>());
        services.AddSingleton<IFeatureViewModel>(sp => sp.GetRequiredService<StatisticsViewModel>());
        services.AddSingleton<IFeatureViewModel>(sp => sp.GetRequiredService<SettingsViewModel>());

        return services;
    }

    private static IServiceCollection AddViews(this IServiceCollection services)
    {
        services.AddTransient<MainWindowView>();
        services.AddTransient<DownloadsView>();
        services.AddTransient<DetailsView>();
        services.AddTransient<SettingsView>();
        services.AddTransient<FindTorrentsView>();
        services.AddTransient<StatisticsView>();
        services.AddTransient<AboutView>();
        services.AddTransient<CreateTorrentWindow>();
        services.AddTransient<AddTorrentOptionsWindow>();
        return services;
    }

    private static IServiceCollection AddHostedServices(this IServiceCollection services)
    {
        // Registered in startup order
        services.AddHostedService<AppSettingsHostedService>();
        // Consumers subscribe before the engine restores torrents so no lifecycle
        // notifications can race their registration.
        services.AddHostedService<TorrentNotificationHostedService>();
        services.AddHostedService<TorrentCompletionActionHostedService>();
        services.AddHostedService<WatchFolderHostedService>();
        services.AddHostedService<BandwidthScheduleHostedService>();
        services.AddHostedService<TorrentEngineHostedService>();
        services.AddHostedService<TorrentAlertsHostedService>();
        services.AddHostedService<McpServerHostedService>();
        // Last of the servers, and does nothing unless switched on.
        services.AddHostedService<TransmissionRpcHostedService>();
        // Opt-in diagnostic; does nothing unless PEERFLUENCE_EXCEPTION_STATS is set.
        services.AddHostedService<ExceptionRateDiagnosticHostedService>();
        return services;
    }

    private static IServiceCollection AddDialogRegistrations(this IServiceCollection services)
    {
        services.AddSingleton(sp => new DialogRegistration(
            typeof(CreateTorrentViewModel),
            () => sp.GetRequiredService<CreateTorrentWindow>(),
            () => sp.GetRequiredService<CreateTorrentViewModel>()));
        return services;
    }
}
