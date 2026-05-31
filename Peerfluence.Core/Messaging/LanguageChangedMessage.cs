namespace Peerfluence.Core.Messaging;

public sealed class LanguageChangedMessage
{
    public LanguageChangedMessage(string language)
    {
        Language = language;
    }

    public string Language { get; }
}
