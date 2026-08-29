using System.Text.Json;
using Peerfluence.Services.Mcp;

namespace Peerfluence.Tests.Services.Mcp;

/// <summary>
/// The shape every MCP tool answers in.
/// </summary>
/// <remarks>
/// An agent on the other end parses these, so the envelope is a contract: whether it says success,
/// whether the error carries a code it can branch on, and whether the transport-level
/// <c>isError</c> flag agrees with the body.
/// </remarks>
public sealed class McpResultFactoryTests
{
    [Fact]
    public void ASuccess_SaysSoInBothTheFlagAndTheBody()
    {
        var result = McpResultFactory.Success("it worked");

        Assert.False(result.IsError);

        var body = Body(result);
        Assert.True(body.GetProperty("Success").GetBoolean());
        Assert.Equal("it worked", body.GetProperty("Message").GetString());
    }

    [Fact]
    public void AnError_SaysSoInBothTheFlagAndTheBody_AndCarriesItsCode()
    {
        var result = McpResultFactory.Error("no such torrent", "torrent_not_found");

        Assert.True(result.IsError);

        var body = Body(result);
        Assert.False(body.GetProperty("Success").GetBoolean());
        Assert.Equal("no such torrent", body.GetProperty("Message").GetString());

        // The code is what an agent branches on. A message it would have to match on strings.
        Assert.Equal("torrent_not_found", body.GetProperty("Code").GetString());
    }

    [Fact]
    public void RawText_IsReturnedUntouched()
    {
        // The resource handlers answer with their own JSON rather than the envelope above.
        var result = McpResultFactory.Text("{\"already\":\"json\"}");

        Assert.False(result.IsError);
        Assert.Equal("{\"already\":\"json\"}", TextOf(result));
    }

    [Fact]
    public void RawText_CanStillBeMarkedAnError()
    {
        Assert.True(McpResultFactory.Text("went wrong", isError: true).IsError);
    }

    private static JsonElement Body(ModelContextProtocol.Protocol.CallToolResult result) =>
        JsonDocument.Parse(TextOf(result)).RootElement;

    private static string TextOf(ModelContextProtocol.Protocol.CallToolResult result)
    {
        var block = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(
            Assert.Single(result.Content));
        return block.Text;
    }
}

/// <summary>
/// The record of what a UI agent did, which is what a failing test case is read back from.
/// </summary>
public sealed class UiAgentTimelineTests
{
    [Fact]
    public void ANewTimeline_HasNothingOnIt()
    {
        Assert.Empty(new UiAgentTimeline().GetEvents());
    }

    [Fact]
    public void EventsAreKept_InTheOrderTheyHappened()
    {
        var timeline = new UiAgentTimeline();

        timeline.Record("action", "clicked add");
        timeline.Record("assertion", "torrent is downloading");

        var events = timeline.GetEvents();

        Assert.Equal(2, events.Count);
        Assert.Equal("action", events[0].EventType);
        Assert.Equal("clicked add", events[0].Message);
        Assert.Equal("assertion", events[1].EventType);
        Assert.Equal("torrent is downloading", events[1].Message);
    }

    [Fact]
    public void ClearingTheTimeline_LeavesNothingBehind()
    {
        var timeline = new UiAgentTimeline();
        timeline.Record("action", "something");

        timeline.Clear();

        Assert.Empty(timeline.GetEvents());
    }

    [Fact]
    public void TheListHandedOut_IsNotTheOneStillBeingWrittenTo()
    {
        // A caller enumerating the timeline while the agent is still acting would otherwise see it
        // change underneath them.
        var timeline = new UiAgentTimeline();
        timeline.Record("action", "first");

        var snapshot = timeline.GetEvents();
        timeline.Record("action", "second");

        Assert.Single(snapshot);
    }
}
