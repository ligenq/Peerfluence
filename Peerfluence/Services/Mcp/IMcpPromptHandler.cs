using System.Threading.Tasks;
using ModelContextProtocol.Protocol;

namespace Peerfluence.Services.Mcp;

public interface IMcpPromptHandler
{
    Task<GetPromptResult> GetPerformanceAuditPromptAsync();
    Task<GetPromptResult> GetCrashInvestigatorPromptAsync();
    Task<GetPromptResult> GetUiTestCaseRunnerPromptAsync();
}
