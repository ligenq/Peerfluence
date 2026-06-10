using Peerfluence.ViewModels;
using PeerSharp.Core;

namespace Peerfluence.Tests.ViewModels;

public class PortMappingStatusViewModelTests
{
    [Fact]
    public void Constructor_SetsPropertiesFromStatus()
    {
        var status = new PortMappingStatus("UPnP", PortMappingResult.Success, 6881, null);
        var sut = new PortMappingStatusViewModel(status);

        Assert.Equal("UPnP", sut.Protocol);
        Assert.Equal(PortMappingResult.Success, sut.Result);
        Assert.Equal(6881, sut.ExternalPort);
        Assert.Null(sut.ErrorMessage);
    }

    [Theory]
    [InlineData(PortMappingResult.Success, "#22C55E")]
    [InlineData(PortMappingResult.Failed, "#EF4444")]
    [InlineData(PortMappingResult.Pending, "#F59E0B")]
    [InlineData(PortMappingResult.NotAttempted, "#6B7280")]
    public void StatusColor_ReturnsCorrectColor(PortMappingResult result, string expectedColor)
    {
        var status = new PortMappingStatus("UPnP", result);
        var sut = new PortMappingStatusViewModel(status);

        Assert.Equal(expectedColor, sut.StatusColor);
    }

    [Fact]
    public void DisplayText_ShowsExternalPort_WhenSuccess()
    {
        var status = new PortMappingStatus("UPnP", PortMappingResult.Success, 6881, null);
        var sut = new PortMappingStatusViewModel(status);

        Assert.Contains("6881", sut.DisplayText);
    }

    [Fact]
    public void DisplayText_ShowsErrorMessage_WhenFailed()
    {
        var status = new PortMappingStatus("NAT-PMP", PortMappingResult.Failed, null, "Gateway not found");
        var sut = new PortMappingStatusViewModel(status);

        Assert.Equal("Gateway not found", sut.DisplayText);
    }

    [Fact]
    public void DisplayText_ShowsResultDisplayName_WhenFailedWithNoError()
    {
        var status = new PortMappingStatus("NAT-PMP", PortMappingResult.Failed, null, null);
        var sut = new PortMappingStatusViewModel(status);

        Assert.Equal("Failed", sut.DisplayText);
    }

    [Fact]
    public void DisplayText_ShowsResultDisplayName_WhenPending()
    {
        var status = new PortMappingStatus("UPnP", PortMappingResult.Pending);
        var sut = new PortMappingStatusViewModel(status);

        Assert.Equal("Pending", sut.DisplayText);
    }

    [Fact]
    public void DisplayText_ShowsResultDisplayName_WhenNotAttempted()
    {
        var status = new PortMappingStatus("UPnP", PortMappingResult.NotAttempted);
        var sut = new PortMappingStatusViewModel(status);

        Assert.Equal("Not attempted", sut.DisplayText);
    }

    [Fact]
    public void DisplayText_ShowsResultDisplayName_WhenSuccessWithNoPort()
    {
        var status = new PortMappingStatus("UPnP", PortMappingResult.Success, null, null);
        var sut = new PortMappingStatusViewModel(status);

        Assert.Equal("Success", sut.DisplayText);
    }
}
