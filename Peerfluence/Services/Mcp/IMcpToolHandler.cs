using System.Threading.Tasks;
using System.Threading;
using ModelContextProtocol.Protocol;

namespace Peerfluence.Services.Mcp;

public interface IMcpToolHandler
{
    Task<CallToolResult> AddTorrentAsync(string magnetOrFile, CancellationToken cancellationToken = default);
    Task<CallToolResult> ManageTorrentAsync(string infoHash, string action, CancellationToken cancellationToken = default);
    Task<CallToolResult> TakeScreenshotAsync(CancellationToken cancellationToken = default);
    Task<CallToolResult> ShutdownApplicationAsync();
    Task<CallToolResult> UpdateSettingsAsync(string settingsJson, CancellationToken cancellationToken = default);
    Task<CallToolResult> InvokeUiActionAsync(string actionName, CancellationToken cancellationToken = default);
    Task<CallToolResult> GetTorrentDiagnosticsAsync(string infoHash);
    Task<CallToolResult> SetFilePriorityAsync(string infoHash, int fileIndex, string priority, CancellationToken cancellationToken = default);
}
