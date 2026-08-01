using System.Globalization;
using Peerfluence.ViewModels;
using PeerSharp.Core;

namespace Peerfluence.Tests.ViewModels;

public sealed class PeerInfoItemViewModelTests
{
    [Fact]
    public void ProgressText_ShowsAPercentageOnceThePeerHasReportedWhatItHolds()
    {
        var sut = new PeerInfoItemViewModel(CreatePeer(progress: 0.25f, hasReportedPieces: true));

        Assert.True(sut.HasReportedPieces);
        Assert.Equal(0.25f.ToString("P1", CultureInfo.CurrentCulture), sut.ProgressText);
    }

    [Fact]
    public void ProgressText_DoesNotClaimZeroForAPeerThatHasSaidNothing()
    {
        // A peer that has sent no bitfield reports zero progress, which would otherwise read as
        // "has none of it" rather than "has not told us".
        var sut = new PeerInfoItemViewModel(CreatePeer(progress: 0f, hasReportedPieces: false));

        Assert.False(sut.HasReportedPieces);
        Assert.Equal("—", sut.ProgressText);
    }

    [Fact]
    public void UpdateFrom_RaisesProgressTextWhenThePeerFinallyReports()
    {
        var sut = new PeerInfoItemViewModel(CreatePeer(progress: 0f, hasReportedPieces: false));
        var changed = new List<string?>();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        sut.UpdateFrom(CreatePeer(progress: 1f, hasReportedPieces: true));

        Assert.Contains(nameof(PeerInfoItemViewModel.ProgressText), changed);
        Assert.Equal(1f.ToString("P1", CultureInfo.CurrentCulture), sut.ProgressText);
    }

    private static PeerInfo CreatePeer(float progress, bool hasReportedPieces)
    {
        return new PeerInfo(
            new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, 6881),
            Country: "SE",
            ClientName: "Test 1.0",
            Progress: progress)
        {
            HasReportedPieces = hasReportedPieces
        };
    }
}
