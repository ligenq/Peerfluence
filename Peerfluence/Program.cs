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
using Peerfluence.Logging;
using Peerfluence.Services.Mcp;
#if !MICROSOFT_STORE
using Velopack;
#endif

namespace Peerfluence;

internal sealed class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
#if !MICROSOFT_STORE
        VelopackApp.Build().Run();
#endif
        CrashHandler.Register();

        if (args.Contains("--mcp"))
        {
            await RunMcpProxyAsync(GetOptionValue(args, "--profile", "--ui-agent-profile"));
            return;
        }

        var uiAgentMode = args.Contains("--ui-agent");
        var profilePath = GetOptionValue(args, "--profile", "--ui-agent-profile");
        var appPaths = new AppPaths(profilePath);
        var avaloniaArgs = StripPeerfluenceArgs(args);

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

            // 4. Build Host
            using var host = builder.Build();

            // 5. Early Check: Single Instance
            var singleInstance = host.Services.GetRequiredService<ISingleInstanceService>();
            var runtimeOptions = host.Services.GetRequiredService<IMcpRuntimeOptions>();
            if (!runtimeOptions.SkipSingleInstanceLock && !singleInstance.TryAcquireSingleInstanceLock())
            {
                singleInstance.SignalExistingInstance();
                return;
            }

            // 6. Start Host (Background services, etc.)
            await host.StartAsync();

            // 7. Run Avalonia App (This is a blocking call)
            var appBuilder = BuildAvaloniaApp(host.Services);
            appBuilder.StartWithClassicDesktopLifetime(avaloniaArgs);

            // 8. Graceful Shutdown
            // Clear Avalonia's SynchronizationContext — the dispatcher is dead after
            // StartWithClassicDesktopLifetime returns, so any await that captures it
            // would deadlock.
            SynchronizationContext.SetSynchronizationContext(null);
            await host.StopAsync(TimeSpan.FromSeconds(5));
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
        await using var pipe = new System.IO.Pipes.NamedPipeClientStream(".", "PeerfluenceMcpPipe", System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
        try
        {
            await pipe.ConnectAsync(5000);
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("Failed to connect to Peerfluence MCP server. Is Peerfluence running?");
            return;
        }

        await using var stdIn = Console.OpenStandardInput();
        await using var stdOut = Console.OpenStandardOutput();

        var tokenPath = McpServerHostedService.GetTokenPath(new AppPaths(profilePath));
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
            if (args[i] == "--ui-agent")
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
