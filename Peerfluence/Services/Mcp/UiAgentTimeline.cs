using System;
using System.Collections.Generic;

namespace Peerfluence.Services.Mcp;

public sealed class UiAgentTimeline : IUiAgentTimeline
{
    private readonly object _gate = new();
    private readonly List<McpConstants.UiAgentTimelineEvent> _events = new();

    public void Record(string eventType, string message)
    {
        lock (_gate)
        {
            _events.Add(new McpConstants.UiAgentTimelineEvent(
                DateTimeOffset.UtcNow,
                eventType,
                message));
        }
    }

    public IReadOnlyList<McpConstants.UiAgentTimelineEvent> GetEvents()
    {
        lock (_gate)
        {
            return _events.ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _events.Clear();
        }
    }
}
