namespace Peerfluence.Core.Services;

public interface IAppMessenger
{
    void Publish<TMessage>(TMessage message)
        where TMessage : class;
}
