using CommunityToolkit.Mvvm.Messaging;

namespace Peerfluence.Services;

public sealed class AppMessenger : IAppMessenger
{
    public void Publish<TMessage>(TMessage message)
        where TMessage : class
    {
        WeakReferenceMessenger.Default.Send(message);
    }
}
