using System.ComponentModel;

namespace Peerfluence.Core.Services;

public interface ILocalizationService : INotifyPropertyChanged
{
    string CurrentLanguage { get; }

    void Apply(string? language);

    string this[string key] { get; }
}
