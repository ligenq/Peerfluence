namespace Peerfluence.ViewModels;

/// <summary>
/// A page with work worth doing only while somebody can see it.
///
/// <para>
/// Every feature view model is a singleton built at startup, so a page that keeps itself fresh from
/// its constructor does so for the life of the application, on a screen that is mostly not open.
/// The window says which page is showing instead, and the page starts and stops on that.
/// </para>
/// </summary>
public interface IOnScreenAware
{
    /// <summary>
    /// Whether this page is the one on screen. Set by <see cref="MainWindowViewModel"/>, which is
    /// the only thing that knows; the page reacts to it rather than deciding it.
    /// </summary>
    bool IsOnScreen { get; set; }
}
