using System;

namespace Peerfluence.ViewModels;

public static class PriorityOptions
{
    public static Priority[] All { get; } = Enum.GetValues<Priority>();
}
