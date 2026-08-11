using Peerfluence.Core.Config;

namespace Peerfluence.Core.Services;

/// <summary>
/// Which interface the user has asked for, and whether they have been asked at all.
/// </summary>
public interface IInterfaceModeService
{
    /// <summary>
    /// The mode in force. Advanced until told otherwise, so a settings file written by an older
    /// version - or one someone edited badly - never hides features that were already in use.
    /// </summary>
    InterfaceMode Current { get; }

    /// <summary>
    /// True while <see cref="Current"/> is <see cref="InterfaceMode.Simple"/>.
    /// </summary>
    bool IsSimple { get; }

    /// <summary>
    /// Whether the user has ever chosen. False only until the welcome has been answered, which is
    /// the one thing that brings the welcome up.
    /// </summary>
    bool HasChosen { get; }

    /// <summary>
    /// Records the choice and persists it. Publishes <see cref="Messaging.InterfaceModeChangedMessage"/>
    /// when the mode actually changed, so the shell can swap what it is showing.
    /// </summary>
    Task SetAsync(InterfaceMode mode, CancellationToken cancellationToken = default);
}
