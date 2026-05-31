using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Peerfluence.Services.Mcp;

[JsonSerializable(typeof(McpConstants.EngineStatsResponse))]
[JsonSerializable(typeof(McpConstants.TorrentListResponse))]
[JsonSerializable(typeof(List<McpConstants.TorrentSummary>))]
[JsonSerializable(typeof(McpConstants.UiAgentStateResponse))]
[JsonSerializable(typeof(McpConstants.UiAgentWaitResult))]
[JsonSerializable(typeof(McpConstants.UiAgentAssertionResult))]
[JsonSerializable(typeof(McpConstants.UiAgentCleanupResult))]
[JsonSerializable(typeof(List<McpConstants.UiAgentTimelineEvent>))]
[JsonSerializable(typeof(List<McpConstants.AlertSummary>))]
[JsonSerializable(typeof(List<McpConstants.TorrentFileSummary>))]
[JsonSerializable(typeof(List<McpConstants.TorrentPeerSummary>))]
[JsonSerializable(typeof(McpConstants.TorrentDiagnosticsResponse))]
[JsonSerializable(typeof(McpConstants.McpOperationResult))]
internal partial class McpJsonContext : JsonSerializerContext
{
}
