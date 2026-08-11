using Peerfluence.Core.Config;

namespace Peerfluence.Core.Messaging;

public sealed class InterfaceModeChangedMessage
{
    public InterfaceModeChangedMessage(InterfaceMode mode)
    {
        Mode = mode;
    }

    public InterfaceMode Mode { get; }
}
