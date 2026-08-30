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
    public void ItCanAlwaysAsk_BecauseItIsGivenSomewhereToAsk()
    {
        // The manager used to be set on this service after construction, by the view model that
        // created it, so there was a window during startup where it existed and could not prompt.
        // It is a constructor argument now, and there is no such window.
        var sut = new DialogService(
            Substitute.For<ITopLevelService>(), [], Substitute.For<SukiUI.Dialogs.ISukiDialogManager>());

        Assert.True(sut.CanPrompt);
    }

    [Fact]
    public async Task AskingForADialogNobodyRegistered_SaysWhichOneWasMissing()
    {
        var sut = new DialogService(Substitute.For<ITopLevelService>(), [], Substitute.For<SukiUI.Dialogs.ISukiDialogManager>());

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

        var sut = new DialogService(Substitute.For<ITopLevelService>(), [registration], Substitute.For<SukiUI.Dialogs.ISukiDialogManager>());

        await Assert.ThrowsAsync<InvalidOperationException>(sut.ShowAsync<UnregisteredViewModel>);
    }
}
