namespace Peerfluence.Core.Services;

internal static class TimeoutExceptionExtensions
{
    /// <summary>
    /// Whether this came from waiting for <paramref name="work"/> rather than from the work itself.
    /// Asking whether the work has finished cannot tell: it can finish between the wait giving up and
    /// the question, which would then treat a successful start as a failed one.
    /// </summary>
    public static bool IsFromWaitingFor(this TimeoutException exception, Task work) =>
        !ReferenceEquals(exception, work.Exception?.InnerException);
}
