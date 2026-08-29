using PeerSharp.Core;
using PeerSharp.Interfaces;
using Peerfluence.Properties;
using Peerfluence.ViewModels;

namespace Peerfluence.Tests.ViewModels;

/// <summary>
/// The enum values the interface shows, and whether any of them reach it untranslated.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these is a switch over an enum PeerSharp owns, falling back to
/// <c>ToString()</c> for anything it does not name. That fallback is invisible in review and
/// invisible at runtime to anyone reading English: a value added to the enum upstream simply starts
/// appearing as its identifier, in every language.
/// </para>
/// <para>
/// Comparing the display name to the identifier would not find it, because the English for
/// <c>Active</c> is "Active". So each value is checked twice: that a string was written for it under
/// the key the switch uses, and that the switch returns that string rather than falling through.
/// </para>
/// </remarks>
[Collection("Localization")]
public sealed class PriorityOptionsTests
{
    [Fact]
    public void EveryPriority_IsNamedByTheResourceWrittenForIt()
    {
        var named = PriorityOptions.Localized.ToDictionary(o => o.Value, o => o.DisplayName);

        foreach (var priority in Enum.GetValues<Priority>())
        {
            AssertNamedByResource($"Priority_{priority}", named[priority], priority);
        }
    }

    [Fact]
    public void ThePriorityList_OffersEveryValueOnce()
    {
        Assert.Equal(
            Enum.GetValues<Priority>().Length,
            PriorityOptions.Localized.Select(o => o.Value).Distinct().Count());
    }

    [Fact]
    public void EveryDownloadStrategy_IsNamedByTheResourceWrittenForIt()
    {
        var named = PriorityOptions.DownloadStrategies.ToDictionary(o => o.Value, o => o.DisplayName);

        foreach (var strategy in Enum.GetValues<DownloadStrategy>())
        {
            AssertNamedByResource($"DownloadStrategy_{strategy}", named[strategy], strategy);
        }
    }

    [Fact]
    public void TheStrategyList_OffersEveryValueOnce()
    {
        Assert.Equal(
            Enum.GetValues<DownloadStrategy>().Length,
            PriorityOptions.DownloadStrategies.Select(o => o.Value).Distinct().Count());
    }

    [Fact]
    public void EveryTrackerStatus_IsNamedByTheResourceWrittenForIt()
    {
        foreach (var status in Enum.GetValues<TrackerStatusType>())
        {
            AssertNamedByResource(
                $"TrackerStatus_{status}",
                PriorityOptions.GetTrackerStatusDisplayName(status),
                status);
        }
    }

    [Fact]
    public void EveryPortMappingResult_IsNamedByTheResourceWrittenForIt()
    {
        foreach (var result in Enum.GetValues<PortMappingResult>())
        {
            AssertNamedByResource(
                $"PortMappingResult_{result}",
                PriorityOptions.GetPortMappingResultDisplayName(result),
                result);
        }
    }

    [Fact]
    public void AllPriorities_AreOffered()
    {
        Assert.Equal(Enum.GetValues<Priority>(), PriorityOptions.All);
    }

    internal static void AssertNamedByResource<TValue>(string key, string actual, TValue value)
        where TValue : struct, Enum
    {
        var resource = Resources.ResourceManager.GetString(key, Resources.Culture);

        Assert.True(
            resource is not null,
            $"{typeof(TValue).Name}.{value} has no resource string under '{key}', so it shows as its identifier.");
        Assert.Equal(resource, actual);
    }
}

/// <summary>
/// The state shown against a torrent in the list.
/// </summary>
[Collection("Localization")]
public sealed class TorrentStateExtensionsTests
{
    [Fact]
    public void EveryTorrentState_IsNamedByTheResourceWrittenForIt()
    {
        foreach (var state in Enum.GetValues<TorrentState>())
        {
            PriorityOptionsTests.AssertNamedByResource(
                $"TorrentState_{state}",
                state.ToDisplayString(),
                state);
        }
    }
}
