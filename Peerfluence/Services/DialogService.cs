using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace Peerfluence.Services;

public sealed class DialogService : IDialogService
{
    private readonly ITopLevelService _topLevelService;
    private readonly IReadOnlyDictionary<Type, DialogRegistration> _registrations;

    public DialogService(ITopLevelService topLevelService, IEnumerable<DialogRegistration> registrations)
    {
        _topLevelService = topLevelService;
        _registrations = registrations.ToDictionary(r => r.ViewModelType);
    }

    public async Task ShowAsync<TViewModel>() where TViewModel : class
    {
        var viewModelType = typeof(TViewModel);
        if (!_registrations.TryGetValue(viewModelType, out var registration))
        {
            throw new InvalidOperationException($"No dialog registered for view model type '{viewModelType.FullName}'.");
        }

        var window = registration.WindowFactory();
        window.DataContext = registration.ViewModelFactory();

        var owner = _topLevelService.GetTopLevel() as Window;
        if (owner != null)
        {
            await window.ShowDialog(owner);
            return;
        }

        window.Show();
    }
}
