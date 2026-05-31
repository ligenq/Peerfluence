using System.Threading.Tasks;

namespace Peerfluence.Services.Mcp;

public interface IMcpResourceHandler
{
    Task<string> GetLatestLogsAsync();
    Task<string> GetEngineStatsAsync();
    Task<string> GetActiveTorrentsAsync();
    Task<string> GetRecentAlertsAsync();
    Task<string> GetTorrentFilesAsync(string infoHash);
    Task<string> GetTorrentPeersAsync(string infoHash);
}
