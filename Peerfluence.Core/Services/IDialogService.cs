namespace Peerfluence.Core.Services;

public interface IDialogService
{
    Task ShowAsync<TViewModel>() where TViewModel : class;

    /// <summary>
    /// Whether there is anywhere to show a prompt yet.
    /// </summary>
    /// <remarks>
    /// False before the window exists, and in tests. A caller about to ask a question it has a
    /// sensible answer for - "remove the torrent but touch no files" - can take that answer instead
    /// of putting up a dialog nobody would see and treating the silence as a refusal.
    /// </remarks>
    bool CanPrompt { get; }

    /// <summary>
    /// Asks the user for a line of text.
    /// </summary>
    /// <returns>What they typed, or <see langword="null"/> if they dismissed the dialog.</returns>
    Task<string?> PromptForTextAsync(TextPrompt prompt);

    /// <summary>
    /// Asks the user to confirm something, usually something that cannot be undone.
    /// </summary>
    /// <returns><see langword="true"/> only if they chose the confirming action.</returns>
    Task<bool> ConfirmAsync(ConfirmPrompt prompt);

    /// <summary>
    /// Asks what should happen to a torrent's files as well as to the torrent.
    /// </summary>
    /// <returns>The choice, or <see langword="null"/> if they dismissed the dialog.</returns>
    Task<RemoveTorrentChoice?> PromptForRemoveOptionsAsync(RemoveTorrentPrompt prompt);
}

/// <summary>A request for a single line of text.</summary>
/// <param name="Title">The dialog's heading.</param>
/// <param name="ConfirmLabel">What the accepting button says - "Add", "Rename".</param>
/// <param name="InitialText">What the box starts with, for editing rather than entering.</param>
/// <param name="Watermark">Placeholder text shown while the box is empty.</param>
public sealed record TextPrompt(
    string Title,
    string ConfirmLabel,
    string? InitialText = null,
    string? Watermark = null);

/// <summary>A request to confirm an action.</summary>
/// <param name="Title">The dialog's heading.</param>
/// <param name="Message">What is about to happen, in full.</param>
/// <param name="ConfirmLabel">What the accepting button says.</param>
/// <param name="CancelLabel">What the declining button says.</param>
/// <param name="Severity">How alarming the dialog should look.</param>
public sealed record ConfirmPrompt(
    string Title,
    string Message,
    string ConfirmLabel,
    string CancelLabel,
    PromptSeverity Severity = PromptSeverity.Warning);

/// <summary>How much attention a dialog asks for.</summary>
public enum PromptSeverity
{
    /// <summary>Something offered, such as an available update.</summary>
    Information,

    /// <summary>Something that cannot be undone.</summary>
    Warning
}

/// <summary>A request to choose how much of a torrent to remove.</summary>
/// <param name="Title">The dialog's heading.</param>
/// <param name="Message">Which torrent is being removed.</param>
/// <param name="ConfirmLabel">What the accepting button says.</param>
/// <param name="CancelLabel">What the declining button says.</param>
/// <param name="DefaultAction">The option selected when the dialog opens.</param>
/// <param name="OptionLabels">What each option is called, in the user's language.</param>
/// <param name="RememberChoiceLabel">The label on the "do not ask again" box.</param>
public sealed record RemoveTorrentPrompt(
    string Title,
    string Message,
    string ConfirmLabel,
    string CancelLabel,
    RemoveTorrentAction DefaultAction,
    IReadOnlyDictionary<RemoveTorrentAction, string> OptionLabels,
    string RememberChoiceLabel);

/// <summary>What the user chose when removing a torrent.</summary>
/// <param name="Action">How much to remove.</param>
/// <param name="RememberChoice">Whether to stop asking and use this from now on.</param>
public sealed record RemoveTorrentChoice(RemoveTorrentAction Action, bool RememberChoice);

/// <summary>
/// How much of a torrent to remove: the torrent alone, or some of what it downloaded with it.
/// </summary>
/// <remarks>
/// Here rather than on the view model that used to own it, because the dialog service takes it and
/// the dialog service's contract cannot depend on the user interface.
/// </remarks>
public enum RemoveTorrentAction
{
    /// <summary>Forget the torrent and leave everything on disk.</summary>
    RemoveOnly,

    /// <summary>Delete what was downloaded.</summary>
    DeleteFiles,

    /// <summary>Delete the torrent's own metadata and resume data.</summary>
    DeleteMetadata,

    /// <summary>Delete both.</summary>
    DeleteAll
}
