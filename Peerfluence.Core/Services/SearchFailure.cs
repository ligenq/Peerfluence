namespace Peerfluence.Core.Services;

/// <summary>
/// Why a search did not work, in the terms the user can act on rather than the terms the network
/// stack reports it in.
///
/// <para>
/// A classification rather than a message, because the message belongs to the interface: this
/// application ships in ten languages, and "No connection could be made because the target machine
/// actively refused it" is neither translated nor useful. What the user needs to know is that
/// nothing is listening where they pointed it, and where to go to fix that.
/// </para>
/// </summary>
public enum SearchFailure
{
    /// <summary>It worked.</summary>
    None = 0,

    /// <summary>No endpoint has been set up at all.</summary>
    NotConfigured,

    /// <summary>
    /// The address is set but nothing answered it. Overwhelmingly the common case, and almost always
    /// means the indexer manager is not running - a preset button fills in an address for software
    /// the user may not have installed yet.
    /// </summary>
    Unreachable,

    /// <summary>Something answered and refused the request: a missing, wrong or expired API key.</summary>
    Rejected,

    /// <summary>Something answered, but it was not a Torznab feed. Usually the wrong path.</summary>
    NotTorznab,

    /// <summary>
    /// The source asked to be left alone for a while. Not a fault and not something to fix - the
    /// answer is to wait, so it is worth distinguishing from a source that is actually broken.
    /// </summary>
    RateLimited,

    /// <summary>Anything else. The detail is carried alongside, because there is nothing better to say.</summary>
    Other
}
