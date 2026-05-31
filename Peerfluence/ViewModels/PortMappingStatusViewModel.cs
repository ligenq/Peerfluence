namespace Peerfluence.ViewModels;

public sealed class PortMappingStatusViewModel
{
    public PortMappingStatusViewModel(PortMappingStatus status)
    {
        Protocol = status.Protocol;
        Result = status.Result;
        ExternalPort = status.ExternalPort;
        ErrorMessage = status.ErrorMessage;
    }

    public string Protocol { get; }

    public PortMappingResult Result { get; }

    public int? ExternalPort { get; }

    public string? ErrorMessage { get; }

    public string StatusColor => Result switch
    {
        PortMappingResult.Success => "#22C55E",
        PortMappingResult.Failed => "#EF4444",
        PortMappingResult.Pending => "#F59E0B",
        _ => "#6B7280"
    };

    public string DisplayText => Result switch
    {
        PortMappingResult.Success => ExternalPort.HasValue
            ? string.Format(Properties.Resources.Settings_PortMapping_ExternalPort, ExternalPort.Value)
            : Result.ToString(),
        PortMappingResult.Failed => ErrorMessage ?? Result.ToString(),
        _ => Result.ToString()
    };
}
