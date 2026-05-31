using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Protocol;

namespace Peerfluence.Services.Mcp;

public interface IUiAgentToolHandler
{
    Task<CallToolResult> GetUiTestStateAsync(CancellationToken cancellationToken = default);

    Task<CallToolResult> LoadTorrentFileAsync(string path, CancellationToken cancellationToken = default);

    Task<CallToolResult> ResumeTorrentAsync(string identifier, CancellationToken cancellationToken = default);

    Task<CallToolResult> StopTorrentAsync(string identifier, CancellationToken cancellationToken = default);

    Task<CallToolResult> SelectTorrentAsync(string identifier);

    Task<CallToolResult> AssertTorrentAsync(
        string identifier,
        string? state = null,
        double? minProgressPercent = null,
        double? maxProgressPercent = null,
        CancellationToken cancellationToken = default);

    Task<CallToolResult> WaitForTorrentAsync(
        string identifier,
        string? state = null,
        double? minProgressPercent = null,
        int timeoutSeconds = 60,
        int pollIntervalMilliseconds = 500,
        CancellationToken cancellationToken = default);

    Task<CallToolResult> CleanupAsync(bool removeTorrents = true, bool clearSelection = true, CancellationToken cancellationToken = default);

    Task<CallToolResult> GetTimelineAsync();

    Task<CallToolResult> ClearTimelineAsync();
}
