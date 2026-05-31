using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Peerfluence.Properties;
using Peerfluence.ViewModels;
using Peerfluence.Views;

namespace Peerfluence;

public class ViewLocator : IDataTemplate
{
    private static readonly Dictionary<Type, Type> ViewModelToViewMap = new Dictionary<Type, Type>
    {
        [typeof(MainWindowViewModel)] = typeof(MainWindowView),
        [typeof(DownloadsViewModel)] = typeof(DownloadsView),
        [typeof(DetailsViewModel)] = typeof(DetailsView),
        [typeof(SettingsViewModel)] = typeof(SettingsView),
        [typeof(AboutViewModel)] = typeof(AboutView),
        [typeof(CreateTorrentViewModel)] = typeof(CreateTorrentWindow),
    };

    public static IServiceProvider? Services { get; set; }

    public Control? Build(object? data)
    {
        if (data is null)
            return null;

        var vmType = data.GetType();
        Control? control = null;

        if (Services != null && ViewModelToViewMap.TryGetValue(vmType, out var viewType))
        {
            control = Services.GetService(viewType) as Control;
        }

        if (control != null)
        {
            control.DataContext = data;
            return control;
        }

        return new TextBlock { Text = string.Format(Resources.ViewLocator_NotFound, vmType.FullName) };
    }

    public bool Match(object? data)
    {
        return data is ViewModelBase;
    }
}
