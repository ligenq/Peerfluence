namespace Peerfluence.Core.Services;

public interface IDialogService
{
    Task ShowAsync<TViewModel>() where TViewModel : class;
}
