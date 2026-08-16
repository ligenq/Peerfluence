using Peerfluence.Core.Config;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Live;

/// <summary>
/// Talks to a real Torznab endpoint - a Prowlarr or Jackett running on this machine - and checks
/// that what Peerfluence does with the answer still works.
///
/// <para>
/// These skip unless <c>live-contract.local.json</c> exists, so they never run on a build machine
/// and never fail for someone who has no indexer. They are meant to be run on demand, by hand, when
/// something about searching is in doubt:
/// </para>
/// <code>
/// dotnet test Peerfluence.Tests --filter "FullyQualifiedName~Live"
/// </code>
///
/// <para>
/// They exist because every bug this feature has had was invisible to a stub. The Prowlarr address
/// was one no Prowlarr serves, and the Test button called t=caps and then rejected the caps document
/// as "not a Torznab feed" - both of which passed a full suite of unit tests, and both of which the
/// first thirty seconds against a real server exposed. A stub answers the way its author expected;
/// only the real thing answers the way it actually does.
/// </para>
/// </summary>
public sealed class TorznabLiveContractTests
{
    private static (TorznabSearchService Service, AppSettings Settings)? TryCreate()
    {
        if (LiveEndpointConfiguration.TryLoad() is not { } configuration)
        {
            return null;
        }

        var settings = new AppSettings();
        settings.Search.TorznabUrl = configuration.TorznabUrl;
        settings.Search.ApiKey = configuration.ApiKey;

        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(settings);

        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        return (new TorznabSearchService(settingsService, client), settings);
    }

    /// <summary>
    /// What the Test button does. This is the one that was broken: a real server answers with a caps
    /// document, and Peerfluence used to call that not a Torznab feed.
    /// </summary>
    [Fact]
    public async Task TestingTheEndpoint_Succeeds_AgainstARealServer()
    {
        if (TryCreate() is not { } context)
        {
            Assert.Skip("No live-contract.local.json; this test needs a real Torznab endpoint.");
            return;
        }

        var response = await context.Service.TestAsync(TestContext.Current.CancellationToken);

        Assert.False(
            response.HasFailure,
            $"Test failed against {context.Settings.Search.TorznabUrl}: {response.Failure} {response.FailureDetail}");
    }

    /// <summary>
    /// A search that reaches the indexer and comes back parsed. Nothing is asserted about how many
    /// results there are - that is the indexer's business and it changes hourly - only that the
    /// exchange completed and produced something this application understands.
    /// </summary>
    [Fact]
    public async Task ASearch_CompletesAndParses()
    {
        if (TryCreate() is not { } context)
        {
            Assert.Skip("No live-contract.local.json; this test needs a real Torznab endpoint.");
            return;
        }

        var response = await context.Service.SearchAsync("the", TestContext.Current.CancellationToken);

        Assert.False(
            response.HasFailure,
            $"Search failed against {context.Settings.Search.TorznabUrl}: {response.Failure} {response.FailureDetail}");

        // Whatever came back has to be usable: a row with no link is a row that cannot be added.
        Assert.All(response.Results, result =>
        {
            Assert.False(string.IsNullOrWhiteSpace(result.Title));
            Assert.False(string.IsNullOrWhiteSpace(result.Link));
        });
    }

    /// <summary>
    /// The failure the user actually saw, kept as a test because it is the one worth never repeating:
    /// an address the server does not serve must be reported as the address being wrong, and must not
    /// be mistaken for the endpoint being fine.
    /// </summary>
    [Fact]
    public async Task AnAddressTheServerDoesNotServe_IsReportedAsSuch()
    {
        if (TryCreate() is not { } context)
        {
            Assert.Skip("No live-contract.local.json; this test needs a real Torznab endpoint.");
            return;
        }

        // The path that shipped, and that no Prowlarr has.
        var wrong = new Uri(new Uri(context.Settings.Search.TorznabUrl), "/api/v1/indexers/all/results/torznab");
        context.Settings.Search.TorznabUrl = wrong.ToString();

        var response = await context.Service.TestAsync(TestContext.Current.CancellationToken);

        Assert.True(response.HasFailure, "a path the server does not serve was reported as working");
        Assert.NotEqual(SearchFailure.None, response.Failure);
    }

    /// <summary>
    /// A wrong key has to read as a wrong key. Reporting it as anything else sends someone to check
    /// the address, which is the one part that was right.
    /// </summary>
    [Fact]
    public async Task AWrongKey_IsReportedAsTheKeyRatherThanTheAddress()
    {
        if (TryCreate() is not { } context)
        {
            Assert.Skip("No live-contract.local.json; this test needs a real Torznab endpoint.");
            return;
        }

        context.Settings.Search.ApiKey = "0000000000000000000000000000dead";

        var response = await context.Service.TestAsync(TestContext.Current.CancellationToken);

        Assert.True(response.HasFailure, "a wrong API key was accepted");
        Assert.Equal(SearchFailure.Rejected, response.Failure);
    }
}
