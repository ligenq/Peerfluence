namespace Peerfluence.Services.Mcp;

public interface IMcpRuntimeOptions
{
    bool ForceEnabled { get; }

    bool ForceAllowDestructiveTools { get; }

    bool EnableUiAgentTools { get; }

    bool SkipSingleInstanceLock { get; }
}
