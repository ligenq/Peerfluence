using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Peerfluence.Core.Services.Rpc;
using Peerfluence.Logging;
using Peerfluence.Services;
using Peerfluence.Services.Mcp;
using Peerfluence.ViewModels;
using Velopack;

namespace Peerfluence;

internal sealed class Program
{
    /// <summary>
    /// Deliberately synchronous, despite everything it calls being asynchronous.
    ///
    /// <para>
    /// <see cref="STAThreadAttribute"/> applies to the thread the runtime starts this on, and an
    /// <c>async Task Main</c> keeps that thread only until its first await: the continuation, and so
    /// the Avalonia UI loop started by it, resumes on a thread-pool thread that belongs to no
    /// apartment. The Windows clipboard is OLE, and refused every copy from there with
    /// "CoInitialize has not been called". Blocking rather than awaiting keeps the UI on the thread
    /// the attribute was put here for.
    /// </para>
    /// </summary>
    [STAThread]
    public static void Main(string[] args)
    {
        var startupTracker = new StartupTracker();
        var velopackApp = VelopackApp.Build();
        if (OperatingSystem.IsWindows())
        {
            velopackApp.OnBeforeUninstallFastCallback(_ => UninstallCleanup.Run());
        }

        velopackApp.Run();
        CrashHandler.Register();

        if (args.Contains("--mcp"))
        {
            RunMcpProxyAsync(GetOptionValue(args, "--profile", "--ui-agent-profile")).GetAwaiter().GetResult();
            return;
        }

        // Starts everything, proves it came up, and leaves again. See RunSmokeTest.
        var smokeTest = args.Contains("--smoke-test");
        var uiAgentMode = args.Contains("--ui-agent");
        var profilePath = GetOptionValue(args, "--profile", "--ui-agent-profile");
        var appPaths = new AppPaths(profilePath);
        var avaloniaArgs = StripPeerfluenceArgs(args);
        var activationArgs = GetActivationArguments(avaloniaArgs);

        try
        {
            // 1. Initial Host Setup
            var builder = Host.CreateApplicationBuilder(args);

            // 2. Configure Logging
            ConfigureLogging(builder, appPaths);

            // 3. Register Services
            builder.Services.AddPeerfluenceServices(new McpRuntimeOptions
            {
                ForceEnabled = uiAgentMode,
                ForceAllowDestructiveTools = uiAgentMode,
                EnableUiAgentTools = uiAgentMode,
                SkipSingleInstanceLock = uiAgentMode
            }, appPaths);
            builder.Services.AddSingleton(startupTracker);

            // 4. Build Host
            using var host = builder.Build();

            // 5. Early Check: Single Instance
            var singleInstance = host.Services.GetRequiredService<ISingleInstanceService>();
            var runtimeOptions = host.Services.GetRequiredService<IMcpRuntimeOptions>();
            if (!runtimeOptions.SkipSingleInstanceLock && !singleInstance.TryAcquireSingleInstanceLock())
            {
                singleInstance.SignalExistingInstance(activationArgs);
                return;
            }

            // 6. Start Host (Background services, etc.)
            host.StartAsync().GetAwaiter().GetResult();
            host.Services
                .GetRequiredService<ILogger<Program>>()
                .LogInformation("Application host started in {ElapsedMs} ms", startupTracker.ElapsedMilliseconds);

            if (smokeTest)
            {
                RunSmokeTest(host);
                return;
            }

            // 7. Run Avalonia App (This is a blocking call)
            var appBuilder = BuildAvaloniaApp(host.Services);
            appBuilder.StartWithClassicDesktopLifetime(avaloniaArgs);

            // 8. Graceful Shutdown
            // Clear Avalonia's SynchronizationContext — the dispatcher is dead after
            // StartWithClassicDesktopLifetime returns, so any await that captures it
            // would deadlock.
            SynchronizationContext.SetSynchronizationContext(null);
            host.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (smokeTest)
        {
            // Never the crash dialog here. The smoke test runs where there is nobody to dismiss one,
            // so showing it turns a failure that should take seconds into a job that hangs until its
            // timeout - which is what happened the first time this was tried.
            Console.Error.WriteLine("smoke test failed: " + ex);
            Environment.Exit(1);
        }
        catch (Exception ex)
        {
            CrashHandler.HandleException(ex);
            throw;
        }
    }

    private static void ConfigureLogging(HostApplicationBuilder builder, IAppPaths appPaths)
    {
        Directory.CreateDirectory(appPaths.AppDataDirectory);
        var logPath = Path.Combine(appPaths.AppDataDirectory, "peerfluence.log");

        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new FileLoggerProvider(logPath));

#if DEBUG
        builder.Logging.AddFilter(null, LogLevel.Debug);
#else
        builder.Logging.AddFilter(null, LogLevel.Information);
#endif
    }

    private static async Task RunMcpProxyAsync(string? profilePath)
    {
        var appPaths = new AppPaths(profilePath);
        var pipeName = ProfileIpcNames.GetMcpPipeName(appPaths);
        await using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", pipeName, System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(5000);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or System.Net.Sockets.SocketException)
        {
            Console.Error.WriteLine("Failed to connect to Peerfluence MCP server. Is Peerfluence running?");
            return;
        }

        await using var stdIn = Console.OpenStandardInput();
        await using var stdOut = Console.OpenStandardOutput();

        var tokenPath = McpServerHostedService.GetTokenPath(appPaths);
        if (!File.Exists(tokenPath))
        {
            Console.Error.WriteLine("Failed to find Peerfluence MCP token. Is MCP enabled and is Peerfluence running?");
            return;
        }

        var token = (await File.ReadAllTextAsync(tokenPath)).Trim();
        var tokenBytes = Encoding.UTF8.GetBytes(token + "\n");
        await pipe.WriteAsync(tokenBytes);
        await pipe.FlushAsync();

        var t1 = stdIn.CopyToAsync(pipe);
        var t2 = pipe.CopyToAsync(stdOut);

        await Task.WhenAny(t1, t2);
    }

    /// <summary>
    /// Starts the whole application except its window, checks it came up, and stops it again.
    ///
    /// <para>
    /// For continuous integration, which has no desktop to show a window on but can still run
    /// everything behind one. What it covers is the part no unit test reaches: that the dependency
    /// graph actually resolves, that every hosted service starts and stops without throwing, and
    /// that settings survive a round trip on a machine that has never run this before. A missing
    /// registration or a service that throws on startup is invisible until something does this.
    /// </para>
    /// </summary>
    private static void RunSmokeTest(IHost host)
    {
        // Resolved rather than assumed: a registration can be missing and nothing notices until the
        // screen that needs it is opened, which in a windowless run is never.
        _ = host.Services.GetRequiredService<ITorrentService>();
        _ = host.Services.GetRequiredService<ITorrentSearchService>();
        _ = host.Services.GetRequiredService<ITorrentCategoryService>();
        _ = host.Services.GetRequiredService<ITransmissionRpcHandler>();
        _ = host.Services.GetRequiredService<MainWindowViewModel>();

        var settings = host.Services.GetRequiredService<IAppSettingsService>();
        settings.SaveAsync(CancellationToken.None).GetAwaiter().GetResult();

        SynchronizationContext.SetSynchronizationContext(null);
        host.StopAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();

        Console.WriteLine("smoke test: started, resolved and stopped cleanly");
    }

    private static string? GetOptionValue(string[] args, params string[] names)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!names.Contains(args[i], StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static string[] StripPeerfluenceArgs(string[] args)
    {
        var result = new System.Collections.Generic.List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--ui-agent" or "--smoke-test")
            {
                continue;
            }

            if (args[i] is "--profile" or "--ui-agent-profile")
            {
                i++;
                continue;
            }

            result.Add(args[i]);
        }

        return result.ToArray();
    }

    private static string[] GetActivationArguments(string[] args)
    {
        return args
            .Where(arg =>
                arg.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(arg), ".torrent", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    public static AppBuilder BuildAvaloniaApp() => BuildAvaloniaApp(null!);

    public static AppBuilder BuildAvaloniaApp(IServiceProvider services)
    {
        var builder = AppBuilder
            .Configure<App>(() => new App(services))
            .UsePlatformDetect()
            .With(new X11PlatformOptions
            {
                EnableSessionManagement = false
            });

        if (Environment.GetEnvironmentVariable("PEERFLUENCE_DISABLE_X11_IME") == "1")
        {
            builder = builder.With(new X11PlatformOptions
            {
                EnableSessionManagement = false,
                EnableIme = false
            });
        }

        return builder.LogToTrace();
    }
}
