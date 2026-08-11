using Peerfluence.Core;
using Peerfluence.Core.Config;
using Peerfluence.Core.Messaging;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

public sealed class InterfaceModeServiceTests
{
    [Fact]
    public void BeforeAnyoneHasChosen_TheModeIsAdvancedAndTheChoiceIsStillPending()
    {
        var sut = Create(new AppSettings(), out _);

        // Advanced rather than Simple: an unanswered question should not hide features.
        Assert.Equal(InterfaceMode.Advanced, sut.Current);
        Assert.False(sut.IsSimple);
        Assert.False(sut.HasChosen);
    }

    [Theory]
    [InlineData("Simple", InterfaceMode.Simple)]
    [InlineData("simple", InterfaceMode.Simple)]
    [InlineData("Advanced", InterfaceMode.Advanced)]
    public void AStoredModeIsRead(string stored, InterfaceMode expected)
    {
        var sut = Create(new AppSettings { InterfaceMode = stored }, out _);

        Assert.Equal(expected, sut.Current);
        Assert.True(sut.HasChosen);
    }

    [Fact]
    public void AnUnrecognisedModeReadsAsAdvanced_AndCountsAsUnanswered()
    {
        // A settings file from a future version, or one edited by hand.
        var sut = Create(new AppSettings { InterfaceMode = "Expert" }, out _);

        Assert.Equal(InterfaceMode.Advanced, sut.Current);
        Assert.False(sut.HasChosen);
    }

    [Fact]
    public async Task Setting_PersistsTheChoiceAndAnnouncesIt()
    {
        var settings = new AppSettings();
        var sut = Create(settings, out var context);

        await sut.SetAsync(InterfaceMode.Simple, TestContext.Current.CancellationToken);

        Assert.Equal("Simple", settings.InterfaceMode);
        Assert.True(sut.IsSimple);
        Assert.True(sut.HasChosen);
        await context.SettingsService.Received(1).SaveAsync(Arg.Any<CancellationToken>());
        context.Messenger.Received(1).Publish(Arg.Is<InterfaceModeChangedMessage>(m => m.Mode == InterfaceMode.Simple));
    }

    [Fact]
    public async Task SettingTheModeItIsAlreadyIn_SavesButAnnouncesNothing()
    {
        var settings = new AppSettings { InterfaceMode = "Advanced" };
        var sut = Create(settings, out var context);

        await sut.SetAsync(InterfaceMode.Advanced, TestContext.Current.CancellationToken);

        await context.SettingsService.Received(1).SaveAsync(Arg.Any<CancellationToken>());
        context.Messenger.DidNotReceive().Publish(Arg.Any<InterfaceModeChangedMessage>());
    }

    [Fact]
    public async Task AnsweringTheWelcomeWithAdvanced_AnnouncesIt_EvenThoughAdvancedWasAlreadyInForce()
    {
        // The mode did not change, but the shell still has a welcome to take down.
        var sut = Create(new AppSettings(), out var context);

        await sut.SetAsync(InterfaceMode.Advanced, TestContext.Current.CancellationToken);

        context.Messenger.Received(1).Publish(Arg.Is<InterfaceModeChangedMessage>(m => m.Mode == InterfaceMode.Advanced));
    }

    private static InterfaceModeService Create(AppSettings settings, out Context context)
    {
        var settingsService = Substitute.For<IAppSettingsService>();
        settingsService.Current.Returns(settings);
        var messenger = Substitute.For<IAppMessenger>();
        context = new Context(settingsService, messenger);
        return new InterfaceModeService(settingsService, messenger);
    }

    private sealed record Context(IAppSettingsService SettingsService, IAppMessenger Messenger);
}
