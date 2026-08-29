using SukiUI.Dialogs;

namespace Peerfluence.Services;

/// <summary>
/// Where modal prompts appear.
/// </summary>
/// <remarks>
/// Separate from <see cref="Peerfluence.Core.Services.IDialogService"/>, which asks for a prompt and
/// lives in a project that must not reference a UI library. This is the other half: the window
/// telling the dialog service which host to put its dialogs in, once the UI thread exists to make
/// one.
/// </remarks>
public interface IDialogHost
{
    ISukiDialogManager? DialogManager { get; set; }
}
