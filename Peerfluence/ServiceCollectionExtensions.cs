using System.IO.Abstractions;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IAddTorrentDialogService, AddTorrentDialogService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IMagnetMetadataPreviewService, MagnetMetadataPreviewService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<ISingleInstanceService, SingleInstanceService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ITopLevelService, TopLevelService>();
        services.AddSingleton<ICompletionActionRunner, CompletionActionRunner>();
        services.AddSingleton<ITorrentEngineService, TorrentEngineService>();
        services.AddSingleton<ITorrentSelectionService, TorrentSelectionService>();
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
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AboutViewModel>();
        services.AddTransient<CreateTorrentViewModel>();

        // IFeatureViewModel discovery (order matters for navigation)
        services.AddSingleton<IFeatureViewModel>(sp => sp.GetRequiredService<DownloadsViewModel>());
        services.AddTransient<IFeatureViewModel>(sp => sp.GetRequiredService<SettingsViewModel>());

        return services;
    }

    private static IServiceCollection AddViews(this IServiceCollection services)
    {
        services.AddTransient<MainWindowView>();
        services.AddTransient<DownloadsView>();
        services.AddTransient<DetailsView>();
        services.AddTransient<SettingsView>();
        services.AddTransient<AboutView>();
        services.AddTransient<CreateTorrentWindow>();
        services.AddTransient<AddTorrentOptionsWindow>();
        return services;
    }

    private static IServiceCollection AddHostedServices(this IServiceCollection services)
    {
        // Registered in startup order
        services.AddHostedService<AppSettingsHostedService>();
        services.AddHostedService<TorrentEngineHostedService>();
        services.AddHostedService<TorrentAlertsHostedService>();
        services.AddHostedService<TorrentNotificationHostedService>();
        services.AddHostedService<TorrentCompletionActionHostedService>();
        services.AddHostedService<McpServerHostedService>();
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
