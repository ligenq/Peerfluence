using CommunityToolkit.Mvvm.Input;

namespace Peerfluence.ViewModels;

/// <summary>
/// The actions a single row in the downloads list offers.
///
/// <para>
/// Handed to the row rather than reached for. A context menu lives in a popup, which is its own
/// visual tree, so a binding that walks up to the owning ItemsControl finds nothing from inside
/// one - which left every item in the menu permanently disabled.
/// </para>
/// </summary>
public interface ITorrentRowActions
{
    IAsyncRelayCommand<TorrentListItemViewModel?> ToggleTorrentCommand { get; }

    IRelayCommand<TorrentListItemViewModel?> OpenTorrentFolderCommand { get; }

    IAsyncRelayCommand<TorrentListItemViewModel?> RemoveTorrentCommand { get; }
}
