using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Peerfluence.ViewModels;

public sealed class AboutViewModel : ViewModelBase
{
    private const string RepositoryUrl = "https://github.com/ligenq/Peerfluence";

    private readonly ILogger<AboutViewModel> _logger;

    public AboutViewModel(ILogger<AboutViewModel> logger)
    {
        _logger = logger;
        OpenGitHubCommand = new RelayCommand(OpenGitHub);

        Packages = new List<string>
        {
            "Avalonia (MIT)",
            "CommunityToolkit.Mvvm (MIT)",
            "Lamar (Apache 2.0)",
            "Material.Icons.Avalonia (MIT)",
            "Microsoft.Extensions.Hosting (MIT)",
            "ModelContextProtocol (MIT)",
            "SukiUI (MIT)"
        };
    }

    public List<string> Packages { get; }

    public string GitHubUrl => RepositoryUrl;

    public IRelayCommand OpenGitHubCommand { get; }

    private void OpenGitHub()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = RepositoryUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open GitHub URL");
        }
    }
}
