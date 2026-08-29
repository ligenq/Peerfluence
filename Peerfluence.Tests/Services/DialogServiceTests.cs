using Peerfluence.Core.Services;
using Peerfluence.Services;

namespace Peerfluence.Tests.Services;

/// <summary>
/// Opening a dialog by the view model it belongs to.
/// </summary>
/// <remarks>
/// Only the refusal is exercised here. Opening one for real needs a window and a top level, which
/// is what <c>Peerfluence.HeadlessTests</c> exists for; what belongs at this level is that an
/// unregistered view model fails loudly rather than silently doing nothing, because a dialog that
/// never appears is otherwise indistinguishable from one the user dismissed.
/// </remarks>
public sealed class DialogServiceTests
{
    private sealed class UnregisteredViewModel;

    [Fact]
    public void WithNoWindowYet_ItSaysItCannotAsk()
    {
        // True at startup and in tests. A caller with a sensible default checks this rather than
        // reading a dismissed dialog as a refusal.
        var sut = new DialogService(Substitute.For<ITopLevelService>(), []);

        Assert.False(sut.CanPrompt);
    }

    [Fact]
    public void OnceTheWindowHandsOverItsManager_ItCanAsk()
    {
        var sut = new DialogService(Substitute.For<ITopLevelService>(), []);

        ((IDialogHost)sut).DialogManager = Substitute.For<SukiUI.Dialogs.ISukiDialogManager>();

        Assert.True(sut.CanPrompt);
    }

    [Fact]
    public async Task WithNowhereToShowThem_EveryPromptAnswersAsIfItWasDismissed()
    {
        // Rather than throwing or hanging. A prompt nobody can see was not answered, and every
        // caller already handles that.
        var sut = new DialogService(Substitute.For<ITopLevelService>(), []);

        Assert.Null(await sut.PromptForTextAsync(new TextPrompt("Title", "Confirm")));
        Assert.False(await sut.ConfirmAsync(new ConfirmPrompt("Title", "Message", "Yes", "No")));
        Assert.Null(await sut.PromptForRemoveOptionsAsync(new RemoveTorrentPrompt(
            "Title",
            "Message",
            "Remove",
            "Cancel",
            RemoveTorrentAction.RemoveOnly,
            new Dictionary<RemoveTorrentAction, string> { [RemoveTorrentAction.RemoveOnly] = "Remove only" },
            "Remember")));
    }

    [Fact]
    public async Task AskingForADialogNobodyRegistered_SaysWhichOneWasMissing()
    {
        var sut = new DialogService(Substitute.For<ITopLevelService>(), []);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(sut.ShowAsync<UnregisteredViewModel>);

        Assert.Contains(nameof(UnregisteredViewModel), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARegistrationForADifferentViewModel_DoesNotSatisfyTheRequest()
    {
        // Keyed by view model type rather than by order, so registering one dialog must not make
        // another one openable.
        var registration = new DialogRegistration(
            typeof(string),
            () => throw new InvalidOperationException("no window should be built"),
            () => "not a view model");

        var sut = new DialogService(Substitute.For<ITopLevelService>(), [registration]);

        await Assert.ThrowsAsync<InvalidOperationException>(sut.ShowAsync<UnregisteredViewModel>);
    }
}
