using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Markup.Xaml;

namespace Peerfluence.Markup;

public class L : MarkupExtension
{
    public string Key { get; }

    public L(string key)
    {
        Key = key;
    }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
            return Key;

        return new LocalizedStringObservable(Key).ToBinding();
    }

    private sealed class LocalizedStringObservable : IObservable<string>
    {
        private readonly string _key;

        public LocalizedStringObservable(string key)
        {
            _key = key;
        }

        public IDisposable Subscribe(IObserver<string> observer)
        {
            observer.OnNext(LocalizationService.GetString(_key));

            var localizationService = LocalizationService.Instance;
            if (localizationService is null)
            {
                return EmptySubscription.Instance;
            }

            void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
            {
                if (e.PropertyName is "Item[]" or null or "")
                {
                    observer.OnNext(LocalizationService.GetString(_key));
                }
            }

            localizationService.PropertyChanged += OnPropertyChanged;
            return new Subscription(() => localizationService.PropertyChanged -= OnPropertyChanged);
        }
    }

    private sealed class EmptySubscription : IDisposable
    {
        public static EmptySubscription Instance { get; } = new();

        public void Dispose()
        {
        }
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _unsubscribe;

        public Subscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe;
        }

        public void Dispose()
        {
            _unsubscribe?.Invoke();
            _unsubscribe = null;
        }
    }
}
