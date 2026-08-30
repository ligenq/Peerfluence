using System.Text.Json;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Protocol;
using Peerfluence.Core.Services;
using Peerfluence.Services;
using Peerfluence.Services.Mcp;

namespace Peerfluence.Tests.Services.Mcp;

/// <summary>
/// The search an agent can run.
/// </summary>
/// <remarks>
/// Without it an agent could add a torrent it already had a link for and could not find one, so
/// half of "find me something and add it" was missing.
/// </remarks>
public sealed class McpToolHandlerSearchTests
{
    private static TorrentSearchResult Result(string title, string link) =>
        new(title, 2048, 12, 3, "an indexer", null, link);

    private static (McpToolHandler Handler, ITorrentSearchService Search) Create(TorrentSearchResponse response)
    {
        var search = Substitute.For<ITorrentSearchService>();
        search.IsConfigured.Returns(true);
        search.SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult(response));

        var settings = Substitute.For<IAppSettingsService>();
        settings.Current.Returns(new Peerfluence.Core.Config.AppSettings());

        var handler = new McpToolHandler(
            Substitute.For<ITorrentService>(),
            Substitute.For<ITopLevelService>(),
            settings,
            Substitute.For<IHostApplicationLifetime>(),
            search);

        return (handler, search);
    }

    private static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(block => block.Text));

    [Fact]
    public async Task ASearch_ComesBackWithWhatTheIndexerFound()
    {
        var (handler, _) = Create(TorrentSearchResponse.Succeeded(
            [Result("A release", "magnet:?xt=urn:btih:abc")]));

        var result = await handler.SearchTorrentsAsync("a release", TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        using var payload = JsonDocument.Parse(Text(result));
        var first = payload.RootElement.GetProperty("Results")[0];
        Assert.Equal("A release", first.GetProperty("Title").GetString());

        // The link is the point: it is what add_torrent takes.
        Assert.Equal("magnet:?xt=urn:btih:abc", first.GetProperty("Link").GetString());
        Assert.True(first.GetProperty("IsMagnet").GetBoolean());
    }

    [Fact]
    public async Task AnEmptyQuery_IsRefusedRatherThanRun()
    {
        var (handler, search) = Create(TorrentSearchResponse.Succeeded([]));

        var result = await handler.SearchTorrentsAsync("   ", TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        await search.DidNotReceive().SearchAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnIndexerThatIsNotSetUp_IsSaidPlainlyRatherThanReportedAsAnError()
    {
        // Talking to somebody else's server failing is an ordinary outcome, and an agent can act on
        // "nothing is configured" far better than on a thrown error.
        var (handler, _) = Create(TorrentSearchResponse.Failed(SearchFailure.NotConfigured, "no endpoint"));

        var result = await handler.SearchTorrentsAsync("anything", TestContext.Current.CancellationToken);

        Assert.False(result.IsError);
        using var payload = JsonDocument.Parse(Text(result));
        Assert.Equal("NotConfigured", payload.RootElement.GetProperty("Failure").GetString());
        Assert.Empty(payload.RootElement.GetProperty("Results").EnumerateArray());
    }
}
