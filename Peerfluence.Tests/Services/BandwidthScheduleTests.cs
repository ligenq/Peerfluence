using Peerfluence.Core.Config;
using Peerfluence.Core.Services;

namespace Peerfluence.Tests.Services;

/// <summary>
/// Which speed limits are in force at a given moment.
/// </summary>
public sealed class BandwidthScheduleTests
{
    private static readonly string[] DayNames = ["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"];

    private static AppSettings Settings(
        bool enabled = true,
        string from = "08:00",
        string to = "18:00",
        long scheduledDown = 100,
        long ordinaryDown = 900)
    {
        var settings = new AppSettings();
        settings.Network.MaxDownloadSpeedBytesPerSecond = ordinaryDown;
        settings.Network.MaxUploadSpeedBytesPerSecond = ordinaryDown;
        settings.Schedule.Enabled = enabled;
        settings.Schedule.From = from;
        settings.Schedule.To = to;
        settings.Schedule.DownloadLimitBytesPerSecond = scheduledDown;
        settings.Schedule.UploadLimitBytesPerSecond = scheduledDown;
        return settings;
    }

    private static DateTimeOffset At(string day, int hour, int minute = 0)
    {
        // 2026-08-31 is a Monday, so the offsets below name the day they say.
        var monday = new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero);
        var index = Array.IndexOf(DayNames, day);
        return monday.AddDays(index).AddHours(hour).AddMinutes(minute);
    }

    [Fact]
    public void InsideTheWindow_TheScheduledLimitsApply()
    {
        var (download, upload) = BandwidthSchedule.LimitsFor(Settings(), At("Tue", 9));

        Assert.Equal(100, download);
        Assert.Equal(100, upload);
    }

    [Fact]
    public void OutsideTheWindow_TheOrdinaryLimitsApply()
    {
        var (download, _) = BandwidthSchedule.LimitsFor(Settings(), At("Tue", 20));

        Assert.Equal(900, download);
    }

    [Fact]
    public void TheWindowIncludesItsStartAndExcludesItsEnd()
    {
        // So two windows that meet at six o'clock do not both claim it.
        Assert.True(BandwidthSchedule.IsInWindow(Settings().Schedule, At("Tue", 8, 0)));
        Assert.False(BandwidthSchedule.IsInWindow(Settings().Schedule, At("Tue", 18, 0)));
    }

    [Fact]
    public void TurnedOff_TheWindowIsNeverOpen()
    {
        Assert.False(BandwidthSchedule.IsInWindow(Settings(enabled: false).Schedule, At("Tue", 9)));
    }

    [Fact]
    public void AWindowThatCrossesMidnight_CoversBothSidesOfIt()
    {
        var schedule = Settings(from: "23:00", to: "02:00").Schedule;

        Assert.True(BandwidthSchedule.IsInWindow(schedule, At("Tue", 23, 30)));
        Assert.True(BandwidthSchedule.IsInWindow(schedule, At("Wed", 1, 0)));
        Assert.False(BandwidthSchedule.IsInWindow(schedule, At("Wed", 3, 0)));
    }

    [Fact]
    public void AWindowThatCrossesMidnight_BelongsToTheDayItOpenedOn()
    {
        // A Friday window running to two in the morning covers Saturday morning without Saturday
        // being selected: it is still Friday's window.
        var settings = Settings(from: "23:00", to: "02:00");
        settings.Schedule.Saturday = false;

        Assert.True(BandwidthSchedule.IsInWindow(settings.Schedule, At("Fri", 23, 30)));
        Assert.True(BandwidthSchedule.IsInWindow(settings.Schedule, At("Sat", 1, 0)));
    }

    [Fact]
    public void ADayThatIsNotSelected_HasNoWindow()
    {
        var settings = Settings();
        settings.Schedule.Wednesday = false;

        Assert.False(BandwidthSchedule.IsInWindow(settings.Schedule, At("Wed", 9)));
        Assert.True(BandwidthSchedule.IsInWindow(settings.Schedule, At("Thu", 9)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("half eight")]
    [InlineData("25:00")]
    [InlineData("8:00")]
    public void ATimeThatWillNotParse_LeavesTheWindowShut(string from)
    {
        // Rather than throttling at some invented hour. A settings file can be hand edited.
        Assert.False(BandwidthSchedule.IsInWindow(Settings(from: from).Schedule, At("Tue", 9)));
    }

    [Fact]
    public void AWindowOfNoLength_IsShutRatherThanAlwaysOpen()
    {
        // Far likelier to be a mistake than a request to throttle around the clock, which selecting
        // every day already does.
        Assert.False(BandwidthSchedule.IsInWindow(Settings(from: "08:00", to: "08:00").Schedule, At("Tue", 8)));
    }

    [Fact]
    public void IsSelected_AnswersForEveryDay()
    {
        var schedule = new ScheduleSettings { Sunday = false };

        Assert.True(BandwidthSchedule.IsSelected(schedule, DayOfWeek.Monday));
        Assert.True(BandwidthSchedule.IsSelected(schedule, DayOfWeek.Tuesday));
        Assert.True(BandwidthSchedule.IsSelected(schedule, DayOfWeek.Wednesday));
        Assert.True(BandwidthSchedule.IsSelected(schedule, DayOfWeek.Thursday));
        Assert.True(BandwidthSchedule.IsSelected(schedule, DayOfWeek.Friday));
        Assert.True(BandwidthSchedule.IsSelected(schedule, DayOfWeek.Saturday));
        Assert.False(BandwidthSchedule.IsSelected(schedule, DayOfWeek.Sunday));
    }
}
