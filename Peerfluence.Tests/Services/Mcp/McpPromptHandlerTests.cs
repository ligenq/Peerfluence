using Peerfluence.Services.Mcp;

namespace Peerfluence.Tests.Services.Mcp;

public sealed class McpPromptHandlerTests
{
    [Fact]
    public async Task Prompts_ReferenceRegisteredResourceUris()
    {
        var sut = new McpPromptHandler();

        var performance = await sut.GetPerformanceAuditPromptAsync();
        var crash = await sut.GetCrashInvestigatorPromptAsync();
        var uiAgent = await sut.GetUiTestCaseRunnerPromptAsync();

        var performanceText = PromptText(performance);
        var crashText = PromptText(crash);
        var uiAgentText = PromptText(uiAgent);

        Assert.Contains(McpConstants.ResourceEngineStats, performanceText);
        Assert.Contains(McpConstants.ResourceActiveTorrents, performanceText);
        Assert.Contains(McpConstants.ResourceRecentAlerts, crashText);
        Assert.Contains(McpConstants.ResourceLogsLatest, crashText);
        Assert.Contains(McpConstants.ToolGetUiTestState, uiAgentText);
        Assert.Contains(McpConstants.ToolLoadTorrentFile, uiAgentText);
        Assert.Contains(McpConstants.ToolWaitForTorrent, uiAgentText);
        Assert.Contains(McpConstants.ToolStopTorrent, uiAgentText);
        Assert.Contains(McpConstants.ToolAssertTorrent, uiAgentText);
        Assert.Contains(McpConstants.ToolGetTimeline, uiAgentText);
        Assert.Contains(McpConstants.ToolCleanup, uiAgentText);
    }

    private static string PromptText(ModelContextProtocol.Protocol.GetPromptResult result)
    {
        var message = Assert.Single(result.Messages);
        return Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(message.Content).Text;
    }
}
