using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Peerfluence.ViewModels;

/// <summary>
/// One day of the week, as a box that can be ticked.
/// </summary>
/// <remarks>
/// The name comes from the culture rather than from the resource files. A day of the week is
/// something every language already knows how to say, and asking .NET for it is nine translations
/// nobody has to write or keep.
/// </remarks>
public sealed partial class ScheduleDayViewModel : ObservableObject
{
    public ScheduleDayViewModel(DayOfWeek day, string name, bool isSelected)
    {
        Day = day;
        Name = name;
        IsSelected = isSelected;
    }

    public DayOfWeek Day { get; }

    public string Name { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
