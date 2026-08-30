using Peerfluence.ViewModels;

namespace Peerfluence.Tests.ViewModels;

/// <summary>
/// One day of the week, as a box that can be ticked.
/// </summary>
public sealed class ScheduleDayViewModelTests
{
    [Fact]
    public void ItKeepsTheDayAndTheNameItWasGiven()
    {
        // The name comes from the culture rather than the resource files, so it arrives as a string
        // and is not this type's business to produce.
        var sut = new ScheduleDayViewModel(DayOfWeek.Thursday, "Thu", isSelected: true);

        Assert.Equal(DayOfWeek.Thursday, sut.Day);
        Assert.Equal("Thu", sut.Name);
        Assert.True(sut.IsSelected);
    }

    [Fact]
    public void TickingIt_AnnouncesTheChange()
    {
        var sut = new ScheduleDayViewModel(DayOfWeek.Sunday, "Sun", isSelected: false);
        var announced = new List<string?>();
        sut.PropertyChanged += (_, e) => announced.Add(e.PropertyName);

        sut.IsSelected = true;

        Assert.True(sut.IsSelected);
        Assert.Contains(nameof(ScheduleDayViewModel.IsSelected), announced);
    }
}
