using System.Globalization;
using Peerfluence.Core.Config;

namespace Peerfluence.Core.Services;

/// <summary>
/// Decides which speed limits are in force at a given moment.
/// </summary>
/// <remarks>
/// A pure function of the settings and the clock, so every awkward case - a window that crosses
/// midnight, a day that is not selected, a time that will not parse - can be decided in a test
/// rather than by waiting until eight in the morning.
/// </remarks>
public static class BandwidthSchedule
{
    /// <summary>The limits that should be in force, in bytes per second. Zero is unlimited.</summary>
    public static (long Download, long Upload) LimitsFor(AppSettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return IsInWindow(settings.Schedule, now)
            ? (settings.Schedule.DownloadLimitBytesPerSecond, settings.Schedule.UploadLimitBytesPerSecond)
            : (settings.Network.MaxDownloadSpeedBytesPerSecond, settings.Network.MaxUploadSpeedBytesPerSecond);
    }

    /// <summary>
    /// Whether <paramref name="now"/> falls inside the scheduled window.
    /// </summary>
    /// <remarks>
    /// The day is the day the window opened on, which matters when it crosses midnight: a window
    /// from 23:00 to 02:00 on Fridays covers Saturday morning, and does not need Saturday selected.
    /// </remarks>
    public static bool IsInWindow(ScheduleSettings schedule, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        if (!schedule.Enabled
            || !TryParse(schedule.From, out var from)
            || !TryParse(schedule.To, out var to))
        {
            return false;
        }

        var time = TimeOnly.FromTimeSpan(now.TimeOfDay);

        if (from == to)
        {
            // Nothing rather than everything: an empty window is far likelier to be a mistake than
            // a request to throttle around the clock, which selecting every day already does.
            return false;
        }

        if (from < to)
        {
            return IsSelected(schedule, now.DayOfWeek) && time >= from && time < to;
        }

        // Crosses midnight.
        return time >= from
            ? IsSelected(schedule, now.DayOfWeek)
            : time < to && IsSelected(schedule, Yesterday(now.DayOfWeek));
    }

    /// <summary>Whether the window runs on this day at all.</summary>
    public static bool IsSelected(ScheduleSettings schedule, DayOfWeek day)
    {
        ArgumentNullException.ThrowIfNull(schedule);

        return day switch
        {
            DayOfWeek.Monday => schedule.Monday,
            DayOfWeek.Tuesday => schedule.Tuesday,
            DayOfWeek.Wednesday => schedule.Wednesday,
            DayOfWeek.Thursday => schedule.Thursday,
            DayOfWeek.Friday => schedule.Friday,
            DayOfWeek.Saturday => schedule.Saturday,
            _ => schedule.Sunday,
        };
    }

    private static DayOfWeek Yesterday(DayOfWeek day) =>
        day == DayOfWeek.Sunday ? DayOfWeek.Saturday : day - 1;

    private static bool TryParse(string value, out TimeOnly time) =>
        TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time);
}
