using System;
using Avalonia.Controls;

namespace Peerfluence.Services;

public sealed class DialogRegistration
{
    public DialogRegistration(Type viewModelType, Func<Window> windowFactory, Func<object> viewModelFactory)
    {
        ViewModelType = viewModelType;
        WindowFactory = windowFactory;
        ViewModelFactory = viewModelFactory;
    }

    public Type ViewModelType { get; }
    public Func<Window> WindowFactory { get; }
    public Func<object> ViewModelFactory { get; }
}
