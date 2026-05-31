using System.Collections.Generic;

namespace Peerfluence.Services.Mcp;

public interface IUiAgentTimeline
{
    void Record(string eventType, string message);

    IReadOnlyList<McpConstants.UiAgentTimelineEvent> GetEvents();

    void Clear();
}
