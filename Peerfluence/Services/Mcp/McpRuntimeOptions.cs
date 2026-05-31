namespace Peerfluence.Services.Mcp;

public sealed class McpRuntimeOptions : IMcpRuntimeOptions
{
    public bool ForceEnabled { get; init; }

    public bool ForceAllowDestructiveTools { get; init; }

    public bool EnableUiAgentTools { get; init; }

    public bool SkipSingleInstanceLock { get; init; }
}
