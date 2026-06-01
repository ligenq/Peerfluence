using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Peerfluence.Core.Messaging;
using Peerfluence.ViewModels;

namespace Peerfluence;

public class App : Application
{
    private readonly IServiceProvider? _services;

    public App()
    {
        // Parameterless constructor for designer support
    }

    public App(IServiceProvider services)
    {
        _services = services;
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:Members annotated with 'RequiresUnreferencedCodeAttribute' require dynamic access otherwise can break functionality when trimming application code", Justification = "Safe in this context")]
    public override void OnFrameworkInitializationCompleted()
    {
        if (_services is null || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        // Wire ViewLocator to DI
        ViewLocator.Services = _services;

        // Initial Setup
        var settings = _services.GetRequiredService<IAppSettingsService>();

        _services
            .GetRequiredService<ILocalizationService>()
            .Apply(settings.Current.Language);

        _services
            .GetRequiredService<IThemeService>()
            .Apply(settings.Current.Theme);

        // Create Main Window
        var viewModel = _services.GetRequiredService<MainWindowViewModel>();
        var mainWindow = (Window)DataTemplates[0].Build(viewModel)!;
        desktop.MainWindow = mainWindow;

        // Register Top-Level for Dialogs
        _services
            .GetRequiredService<ITopLevelService>()
            .SetTopLevel(mainWindow);

        // Handle Activation (Single Instance) via messenger
        WeakReferenceMessenger.Default.Register<ActivationRequestedMessage>(this, (_, _) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                mainWindow.WindowState = WindowState.Normal;
                mainWindow.Activate();
            });
        });

        var singleInstance = _services.GetRequiredService<ISingleInstanceService>();
        singleInstance.StartListening();

        var startupArguments = desktop.Args?
            .Where(arg =>
                arg.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(System.IO.Path.GetExtension(arg), ".torrent", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (startupArguments?.Length > 0)
        {
            Dispatcher.UIThread.Post(() =>
                WeakReferenceMessenger.Default.Send(new ActivationRequestedMessage(startupArguments)));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
