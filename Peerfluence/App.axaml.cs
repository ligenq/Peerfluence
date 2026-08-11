using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Peerfluence.Core.Messaging;
using Peerfluence.Services;
using Peerfluence.ViewModels;

namespace Peerfluence;

public class App : Application
{
    private readonly IServiceProvider? _services;
    private readonly CancellationTokenSource _optionalStartupCts = new();

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
        FocusContextMenusWhenOpened();
    }

    /// <summary>
    /// Moves keyboard focus into a context menu as it opens.
    ///
    /// <para>
    /// A context menu opened by the mouse never took focus, which left it with no keyboard at all:
    /// the arrows did not walk the items, Enter did not invoke one, and Escape did not close it -
    /// every key went to whatever was focused behind the menu. A class handler covers each context
    /// menu in the application rather than only the one that was noticed.
    /// </para>
    /// </summary>
    private static void FocusContextMenusWhenOpened()
    {
        MenuBase.OpenedEvent.AddClassHandler<ContextMenu>((menu, _) =>
        {
            // A ContextMenu ships as Focusable=false, so asking it to take focus does nothing until
            // it is allowed to hold any.
            menu.Focusable = true;
            menu.Focus();
        });
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

        // GeoIP data can be large and is not required to construct a usable
        // window. Start it only after Avalonia has shown the window. The blocklist
        // remains part of engine startup so it is active before user interaction.
        mainWindow.Opened += OnMainWindowOpened;

        async void OnMainWindowOpened(object? sender, EventArgs args)
        {
            mainWindow.Opened -= OnMainWindowOpened;
            _services
                .GetRequiredService<ILogger<App>>()
                .LogInformation(
                    "Main window opened in {ElapsedMs} ms",
                    _services.GetRequiredService<StartupTracker>().ElapsedMilliseconds);
            try
            {
                var engineService = _services.GetRequiredService<ITorrentEngineService>();
                await Task.Run(
                    () => engineService.LoadOptionalDataAsync(_optionalStartupCts.Token),
                    _optionalStartupCts.Token);
            }
            catch (OperationCanceledException) when (_optionalStartupCts.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _services
                    .GetRequiredService<ILogger<App>>()
                    .LogWarning(ex, "Optional torrent data failed to load after startup");
            }
        }

        desktop.Exit += (_, _) => _optionalStartupCts.Cancel();

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

        Dispatcher.UIThread.Post(async () => await viewModel.CheckForUpdatesOnStartupAsync());

        base.OnFrameworkInitializationCompleted();
    }
}
