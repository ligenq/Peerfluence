using System.ComponentModel;
using System.Globalization;
using Avalonia;
using CommunityToolkit.Mvvm.Messaging;
using Peerfluence.Core.Messaging;
using SukiUI;

namespace Peerfluence.Services;

public sealed class LocalizationService : ILocalizationService
{
    private const string DefaultLanguage = "en-US";
    public event PropertyChangedEventHandler? PropertyChanged;

    public string CurrentLanguage { get; private set; } = DefaultLanguage;

    public static LocalizationService? Instance { get; private set; }

    public LocalizationService()
    {
        Instance = this;
    }

    public void Apply(string? language)
    {
        var resolvedLanguage = string.IsNullOrWhiteSpace(language) ? DefaultLanguage : language;
        CurrentLanguage = resolvedLanguage;

        var culture = new CultureInfo(resolvedLanguage);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        Properties.Resources.Culture = culture;

        UpdateSukiLocale(culture);

        WeakReferenceMessenger.Default.Send(new LanguageChangedMessage(resolvedLanguage));

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    }

    public static string GetString(string key)
    {
        var result = Properties.Resources.ResourceManager.GetString(key, Properties.Resources.Culture);
        return result ?? key;
    }

    public string this[string key] => GetString(key);

    private static void UpdateSukiLocale(CultureInfo culture)
    {
        if (Application.Current == null)
        {
            return;
        }

        var theme = SukiTheme.GetInstance(Application.Current);
        theme.Locale = culture.Name;
    }
}
