using System;
using System.Collections.Generic;
using System.Linq;

namespace Peerfluence.ViewModels;

public static class PriorityOptions
{
    private static readonly Priority[] PriorityValues = Enum.GetValues<Priority>();
    private static readonly DownloadStrategy[] DownloadStrategyValues = Enum.GetValues<DownloadStrategy>();

    public static Priority[] All { get; } = PriorityValues;

    public static IReadOnlyList<EnumDisplayOption<Priority>> Localized =>
        PriorityValues.Select(priority => new EnumDisplayOption<Priority>(priority, GetPriorityDisplayName(priority))).ToArray();

    public static IReadOnlyList<EnumDisplayOption<DownloadStrategy>> DownloadStrategies =>
        DownloadStrategyValues.Select(strategy => new EnumDisplayOption<DownloadStrategy>(strategy, GetDownloadStrategyDisplayName(strategy))).ToArray();

    public static string GetTrackerStatusDisplayName(TrackerStatusType status)
    {
        return GetResourceString(status switch
        {
            TrackerStatusType.Working => "TrackerStatus_Working",
            TrackerStatusType.NotWorking => "TrackerStatus_NotWorking",
            TrackerStatusType.CircuitOpen => "TrackerStatus_CircuitOpen",
            TrackerStatusType.Unknown => "TrackerStatus_Unknown",
            _ => status.ToString()
        });
    }

    public static string GetPortMappingResultDisplayName(PortMappingResult result)
    {
        return GetResourceString(result switch
        {
            PortMappingResult.NotAttempted => "PortMappingResult_NotAttempted",
            PortMappingResult.Pending => "PortMappingResult_Pending",
            PortMappingResult.Success => "PortMappingResult_Success",
            PortMappingResult.Failed => "PortMappingResult_Failed",
            _ => result.ToString()
        });
    }

    private static string GetPriorityDisplayName(Priority priority)
    {
        return GetResourceString(priority switch
        {
            Priority.DoNotDownload => "Priority_DoNotDownload",
            Priority.Low => "Priority_Low",
            Priority.Normal => "Priority_Normal",
            Priority.High => "Priority_High",
            _ => priority.ToString()
        });
    }

    private static string GetDownloadStrategyDisplayName(DownloadStrategy strategy)
    {
        return GetResourceString(strategy switch
        {
            DownloadStrategy.RarestFirst => "DownloadStrategy_RarestFirst",
            DownloadStrategy.Sequential => "DownloadStrategy_Sequential",
            DownloadStrategy.Streaming => "DownloadStrategy_Streaming",
            _ => strategy.ToString()
        });
    }

    private static string GetResourceString(string key)
    {
        return Properties.Resources.ResourceManager.GetString(key, Properties.Resources.Culture) ?? key;
    }
}

public sealed record EnumDisplayOption<TValue>(TValue Value, string DisplayName);
