using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using ModelContextProtocol.Protocol;

namespace Peerfluence.Services.Mcp;

public class McpServerHostedService : BackgroundService
{
    private readonly IMcpToolHandler _toolHandler;
    private readonly IUiAgentToolHandler _uiAgentToolHandler;
    private readonly IMcpResourceHandler _resourceHandler;
    private readonly IMcpPromptHandler _promptHandler;
    private readonly IAppSettingsService _settingsService;
    private readonly IMcpRuntimeOptions _runtimeOptions;
    private readonly IAppPaths _appPaths;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<McpServerHostedService> _logger;

    public McpServerHostedService(
        IMcpToolHandler toolHandler,
        IUiAgentToolHandler uiAgentToolHandler,
        IMcpResourceHandler resourceHandler,
        IMcpPromptHandler promptHandler,
        IAppSettingsService settingsService,
        IMcpRuntimeOptions runtimeOptions,
        IAppPaths appPaths,
        ILoggerFactory loggerFactory,
        IServiceProvider serviceProvider,
        ILogger<McpServerHostedService> logger)
    {
        _toolHandler = toolHandler;
        _uiAgentToolHandler = uiAgentToolHandler;
        _resourceHandler = resourceHandler;
        _promptHandler = promptHandler;
        _settingsService = settingsService;
        _runtimeOptions = runtimeOptions;
        _appPaths = appPaths;
        _loggerFactory = loggerFactory;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting MCP Server Hosted Service...");

        if (!_settingsService.Current.Mcp.Enabled && !_runtimeOptions.ForceEnabled)
        {
            _logger.LogInformation("MCP server is disabled by settings.");
            return;
        }

        var tokenPath = GetTokenPath(_appPaths);
        var token = CreateToken();
        WriteTokenFile(tokenPath, token);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await using var pipeServer = CreatePipeServer();

                    _logger.LogInformation("Waiting for MCP proxy connection...");

                    await pipeServer.WaitForConnectionAsync(stoppingToken);
                    _logger.LogInformation("MCP proxy connected.");

                    if (!await ValidateTokenAsync(pipeServer, token, stoppingToken))
                    {
                        _logger.LogWarning("Rejected MCP proxy connection with invalid token.");
                        continue;
                    }

                    var transport = new StreamServerTransport(pipeServer, pipeServer, "Peerfluence", _loggerFactory);

                    var options = new McpServerOptions
                    {
                        ServerInfo = new Implementation { Name = "Peerfluence", Version = "1.0.0" },
                        ToolCollection = new McpServerPrimitiveCollection<McpServerTool>(),
                        ResourceCollection = new McpServerResourceCollection(),
                        PromptCollection = new McpServerPrimitiveCollection<McpServerPrompt>()
                    };

                    // Register Tools
                    options.ToolCollection.Add(McpServerTool.Create(
                        _toolHandler.AddTorrentAsync,
                        new McpServerToolCreateOptions { Name = McpConstants.ToolAddTorrent, Description = McpConstants.ToolAddTorrentDescription }));

                    options.ToolCollection.Add(McpServerTool.Create(
                        _toolHandler.ManageTorrentAsync,
                        new McpServerToolCreateOptions { Name = McpConstants.ToolManageTorrent, Description = McpConstants.ToolManageTorrentDescription }));

                    options.ToolCollection.Add(McpServerTool.Create(
                        _toolHandler.TakeScreenshotAsync,
                        new McpServerToolCreateOptions { Name = McpConstants.ToolTakeScreenshot, Description = McpConstants.ToolTakeScreenshotDescription }));

                    options.ToolCollection.Add(McpServerTool.Create(
                        _toolHandler.ShutdownApplicationAsync,
                        new McpServerToolCreateOptions { Name = McpConstants.ToolShutdownApplication, Description = McpConstants.ToolShutdownApplicationDescription }));

                    options.ToolCollection.Add(McpServerTool.Create(
                        _toolHandler.UpdateSettingsAsync,
                        new McpServerToolCreateOptions { Name = McpConstants.ToolUpdateSettings, Description = McpConstants.ToolUpdateSettingsDescription }));

                    options.ToolCollection.Add(McpServerTool.Create(
                        _toolHandler.InvokeUiActionAsync,
                        new McpServerToolCreateOptions { Name = McpConstants.ToolInvokeUiAction, Description = McpConstants.ToolInvokeUiActionDescription }));

                    options.ToolCollection.Add(McpServerTool.Create(
                        _toolHandler.GetTorrentDiagnosticsAsync,
                        new McpServerToolCreateOptions { Name = McpConstants.ToolGetTorrentDiagnostics, Description = McpConstants.ToolGetTorrentDiagnosticsDescription }));

                    options.ToolCollection.Add(McpServerTool.Create(
                        _toolHandler.SetFilePriorityAsync,
                        new McpServerToolCreateOptions { Name = McpConstants.ToolSetFilePriority, Description = McpConstants.ToolSetFilePriorityDescription }));

                    if (_runtimeOptions.EnableUiAgentTools)
                    {
                        options.ToolCollection.Add(McpServerTool.Create(
                            _uiAgentToolHandler.GetUiTestStateAsync,
                            new McpServerToolCreateOptions { Name = McpConstants.ToolGetUiTestState, Description = McpConstants.ToolGetUiTestStateDescription }));

                        options.ToolCollection.Add(McpServerTool.Create(
                            _uiAgentToolHandler.LoadTorrentFileAsync,
                            new McpServerToolCreateOptions { Name = McpConstants.ToolLoadTorrentFile, Description = McpConstants.ToolLoadTorrentFileDescription }));

                        options.ToolCollection.Add(McpServerTool.Create(
                            _uiAgentToolHandler.ResumeTorrentAsync,
                            new McpServerToolCreateOptions { Name = McpConstants.ToolResumeTorrent, Description = McpConstants.ToolResumeTorrentDescription }));

                        options.ToolCollection.Add(McpServerTool.Create(
                            _uiAgentToolHandler.StopTorrentAsync,
                            new McpServerToolCreateOptions { Name = McpConstants.ToolStopTorrent, Description = McpConstants.ToolStopTorrentDescription }));

                        options.ToolCollection.Add(McpServerTool.Create(
                            _uiAgentToolHandler.SelectTorrentAsync,
                            new McpServerToolCreateOptions { Name = McpConstants.ToolSelectTorrent, Description = McpConstants.ToolSelectTorrentDescription }));

                        options.ToolCollection.Add(McpServerTool.Create(
                            _uiAgentToolHandler.AssertTorrentAsync,
                            new McpServerToolCreateOptions { Name = McpConstants.ToolAssertTorrent, Description = McpConstants.ToolAssertTorrentDescription }));

                        options.ToolCollection.Add(McpServerTool.Create(
                            _uiAgentToolHandler.WaitForTorrentAsync,
                            new McpServerToolCreateOptions { Name = McpConstants.ToolWaitForTorrent, Description = McpConstants.ToolWaitForTorrentDescription }));

                        options.ToolCollection.Add(McpServerTool.Create(
                            _uiAgentToolHandler.CleanupAsync,
                            new McpServerToolCreateOptions { Name = McpConstants.ToolCleanup, Description = McpConstants.ToolCleanupDescription }));

                        options.ToolCollection.Add(McpServerTool.Create(
                            _uiAgentToolHandler.GetTimelineAsync,
                            new McpServerToolCreateOptions { Name = McpConstants.ToolGetTimeline, Description = McpConstants.ToolGetTimelineDescription }));

                        options.ToolCollection.Add(McpServerTool.Create(
                            _uiAgentToolHandler.ClearTimelineAsync,
                            new McpServerToolCreateOptions { Name = McpConstants.ToolClearTimeline, Description = McpConstants.ToolClearTimelineDescription }));
                    }

                    // Register Resources
                    options.ResourceCollection.Add(McpServerResource.Create(
                        _resourceHandler.GetLatestLogsAsync,
                        new McpServerResourceCreateOptions { UriTemplate = McpConstants.ResourceLogsLatest, Name = "Logs", Description = McpConstants.ResourceLogsLatestDescription }));

                    options.ResourceCollection.Add(McpServerResource.Create(
                        _resourceHandler.GetEngineStatsAsync,
                        new McpServerResourceCreateOptions { UriTemplate = McpConstants.ResourceEngineStats, Name = "Engine Stats", Description = McpConstants.ResourceEngineStatsDescription }));

                    options.ResourceCollection.Add(McpServerResource.Create(
                        _resourceHandler.GetActiveTorrentsAsync,
                        new McpServerResourceCreateOptions { UriTemplate = McpConstants.ResourceActiveTorrents, Name = "Active Torrents", Description = McpConstants.ResourceActiveTorrentsDescription }));

                    options.ResourceCollection.Add(McpServerResource.Create(
                        _resourceHandler.GetRecentAlertsAsync,
                        new McpServerResourceCreateOptions { UriTemplate = McpConstants.ResourceRecentAlerts, Name = "Recent Alerts", Description = McpConstants.ResourceRecentAlertsDescription }));

                    options.ResourceCollection.Add(McpServerResource.Create(
                        _resourceHandler.GetTorrentFilesAsync,
                        new McpServerResourceCreateOptions { UriTemplate = McpConstants.ResourceTorrentFiles, Name = "Torrent Files", Description = McpConstants.ResourceTorrentFilesDescription }));

                    options.ResourceCollection.Add(McpServerResource.Create(
                        _resourceHandler.GetTorrentPeersAsync,
                        new McpServerResourceCreateOptions { UriTemplate = McpConstants.ResourceTorrentPeers, Name = "Torrent Peers", Description = McpConstants.ResourceTorrentPeersDescription }));

                    // Register Prompts
                    options.PromptCollection.Add(McpServerPrompt.Create(
                        _promptHandler.GetPerformanceAuditPromptAsync,
                        new McpServerPromptCreateOptions { Name = McpConstants.PromptPerformanceAudit, Description = McpConstants.PromptPerformanceAuditDescription }));

                    options.PromptCollection.Add(McpServerPrompt.Create(
                        _promptHandler.GetCrashInvestigatorPromptAsync,
                        new McpServerPromptCreateOptions { Name = McpConstants.PromptCrashInvestigator, Description = McpConstants.PromptCrashInvestigatorDescription }));

                    if (_runtimeOptions.EnableUiAgentTools)
                    {
                        options.PromptCollection.Add(McpServerPrompt.Create(
                            _promptHandler.GetUiTestCaseRunnerPromptAsync,
                            new McpServerPromptCreateOptions { Name = McpConstants.PromptUiTestCaseRunner, Description = McpConstants.PromptUiTestCaseRunnerDescription }));
                    }

                    // Start Server
                    await using var server = McpServer.Create(transport, options, _loggerFactory, _serviceProvider);

                    _logger.LogInformation("MCP Server running over pipe transport.");

                    // RunAsync will block until the transport is disconnected or cancellation is requested.
                    await server.RunAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in MCP Server loop.");
                    await Task.Delay(1000, stoppingToken); // Backoff on error
                }
                finally
                {
                    _logger.LogInformation("MCP Proxy disconnected.");
                }
            }
        }
        finally
        {
            TryDeleteTokenFile(tokenPath);
        }
    }

    private static NamedPipeServerStream CreatePipeServer()
    {
        if (OperatingSystem.IsWindows())
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                WindowsIdentity.GetCurrent().User!,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            return NamedPipeServerStreamAcl.Create(
                "PeerfluenceMcpPipe",
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous,
                0,
                0,
                security);
        }

        return new NamedPipeServerStream(
            "PeerfluenceMcpPipe",
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
    }

    internal static string GetTokenPath(IAppPaths appPaths)
    {
        return Path.Combine(appPaths.AppDataDirectory, "mcp.token");
    }

    private static string CreateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static void WriteTokenFile(string path, string token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, token, Encoding.UTF8);
    }

    private static async Task<bool> ValidateTokenAsync(Stream stream, string expectedToken, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var buffer = new byte[1];

        while (builder.Length < 256)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            var ch = (char)buffer[0];
            if (ch == '\n')
            {
                break;
            }

            if (ch != '\r')
            {
                builder.Append(ch);
            }
        }

        var actualBytes = Encoding.UTF8.GetBytes(builder.ToString());
        var expectedBytes = Encoding.UTF8.GetBytes(expectedToken);
        return actualBytes.Length == expectedBytes.Length
            && CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes);
    }

    private static void TryDeleteTokenFile(string tokenPath)
    {
        try
        {
            if (File.Exists(tokenPath))
            {
                File.Delete(tokenPath);
            }
        }
        catch
        {
            // Best-effort cleanup
        }
    }
}
