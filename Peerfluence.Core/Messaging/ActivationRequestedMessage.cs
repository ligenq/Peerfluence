namespace Peerfluence.Core.Messaging;

public sealed class ActivationRequestedMessage
{
    public ActivationRequestedMessage()
        : this(Array.Empty<string>())
    {
    }

    public ActivationRequestedMessage(IReadOnlyList<string> arguments)
    {
        Arguments = arguments;
    }

    public IReadOnlyList<string> Arguments { get; }
}
