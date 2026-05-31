using System.Collections.Generic;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;

namespace Peerfluence.Services.Mcp;

public class McpPromptHandler : IMcpPromptHandler
{
    public Task<GetPromptResult> GetPerformanceAuditPromptAsync()
    {
        return Task.FromResult(new GetPromptResult
        {
            Description = "Guides the AI to perform a performance audit of the torrent engine.",
            Messages = new List<PromptMessage>
            {
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $@"Please perform a detailed performance audit of the Peerfluence torrent engine. 
1. Read the global engine stats from '{McpConstants.ResourceEngineStats}'.
2. List all active torrents from '{McpConstants.ResourceActiveTorrents}'.
3. For each active torrent with slow speeds, check its deep diagnostics using 'get_torrent_diagnostics'.
4. Identify bottlenecks such as high disk backpressure, low peer-to-seed ratios, or connection issues (e.g. NAT/UPnP).
5. Suggest specific 'update_settings' changes to improve overall throughput."
                    }
                }
            }
        });
    }

    public Task<GetPromptResult> GetCrashInvestigatorPromptAsync()
    {
        return Task.FromResult(new GetPromptResult
        {
            Description = "Guides the AI to investigate recent crashes or errors.",
            Messages = new List<PromptMessage>
            {
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $@"The application or a torrent has encountered an issue. Please investigate the root cause:
1. Read the most recent engine alerts from '{McpConstants.ResourceRecentAlerts}'.
2. Examine the latest application logs from '{McpConstants.ResourceLogsLatest}'.
3. Look for 'TorrentException', 'IOException', or network timeout patterns.
4. If a specific torrent is failing, get its diagnostics via 'get_torrent_diagnostics'.
5. Propose a fix or workaround based on your findings."
                    }
                }
            }
        });
    }

    public Task<GetPromptResult> GetUiTestCaseRunnerPromptAsync()
    {
        return Task.FromResult(new GetPromptResult
        {
            Description = "Guides the AI to execute a natural-language UI test case against the running Peerfluence app.",
            Messages = new List<PromptMessage>
            {
                new PromptMessage
                {
                    Role = Role.User,
                    Content = new TextContentBlock
                    {
                        Text = $@"You are testing the real Peerfluence UI while it is running in UI-agent mode.
Use structured tools for state-changing actions and assertions, and use screenshots only when the expected result is visual.

Recommended flow:
1. Call '{McpConstants.ToolGetUiTestState}' to verify the window is available and capture the initial torrent list.
2. Call '{McpConstants.ToolClearTimeline}' at the start of each test case.
3. If a test asks to load a .torrent file, call '{McpConstants.ToolLoadTorrentFile}' with the exact file path.
4. Use '{McpConstants.ToolWaitForTorrent}' for waits such as progress thresholds or expected states.
5. Use '{McpConstants.ToolAssertTorrent}' for explicit pass/fail assertions.
6. Use '{McpConstants.ToolSelectTorrent}' before checks that depend on the selected torrent details panel.
7. Use '{McpConstants.ToolResumeTorrent}' to start or resume a torrent, and '{McpConstants.ToolStopTorrent}' to stop a torrent.
8. Confirm final assertions with '{McpConstants.ToolGetUiTestState}', '{McpConstants.ToolAssertTorrent}', '{McpConstants.ToolWaitForTorrent}', or 'get_torrent_diagnostics'.
9. On failure, call '{McpConstants.ToolGetTimeline}' and 'take_screenshot' before reporting.
10. Use '{McpConstants.ToolCleanup}' during teardown when the test case should leave no torrents behind.
Report pass/fail with the exact observed state, progress percentage, timeline entries, and any timeout or error details."
                    }
                }
            }
        });
    }
}
