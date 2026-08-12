using Peerfluence.Core.Config;
using Peerfluence.Core.Messaging;

namespace Peerfluence.Core.Services;

public sealed class InterfaceModeService : IInterfaceModeService
{
    private const string SimpleValue = "Simple";
    private const string AdvancedValue = "Advanced";

    private readonly IAppSettingsService _settingsService;
    private readonly IAppMessenger _messenger;

    public InterfaceModeService(IAppSettingsService settingsService, IAppMessenger messenger)
    {
        _settingsService = settingsService;
        _messenger = messenger;
    }

    public InterfaceMode Current => Parse(_settingsService.Current.InterfaceMode);

    public bool IsSimple => Current == InterfaceMode.Simple;

    public bool HasChosen => IsKnown(_settingsService.Current.InterfaceMode);

    public async Task SetAsync(InterfaceMode mode, CancellationToken cancellationToken = default)
    {
        var settings = _settingsService.Current;
        var changed = !HasChosen || Current != mode;

        settings.InterfaceMode = Format(mode);
        await _settingsService.SaveAsync(cancellationToken).ConfigureAwait(false);

        if (changed)
        {
            _messenger.Publish(new InterfaceModeChangedMessage(mode));
        }
    }

    /// <summary>
    /// Anything but an exact "Simple" reads as Advanced, including the empty value that means the
    /// user has not chosen: an unknown mode should show more than the user expects, never less.
    /// </summary>
    private static InterfaceMode Parse(string? value)
    {
        return string.Equals(value, SimpleValue, StringComparison.OrdinalIgnoreCase)
            ? InterfaceMode.Simple
            : InterfaceMode.Advanced;
    }

    private static bool IsKnown(string? value)
    {
        return string.Equals(value, SimpleValue, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, AdvancedValue, StringComparison.OrdinalIgnoreCase);
    }

    private static string Format(InterfaceMode mode)
    {
        return mode == InterfaceMode.Simple ? SimpleValue : AdvancedValue;
    }
}
