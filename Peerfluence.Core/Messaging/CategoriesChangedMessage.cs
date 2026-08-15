namespace Peerfluence.Core.Messaging;

/// <summary>
/// The categories, or which torrent is in which, have changed.
///
/// <para>
/// Sent rather than exposing an event because several screens show the same information - the list's
/// column and filters, the settings manager, the add dialog's picker - and none of them owns it.
/// </para>
/// </summary>
public sealed class CategoriesChangedMessage;
