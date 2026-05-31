using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Peerfluence.ViewModels;
using Peerfluence.Views;

namespace Peerfluence.Services;

public sealed class AddTorrentDialogService : IAddTorrentDialogService
{
    private static readonly TimeSpan MagnetMetadataPreviewTimeout = TimeSpan.FromSeconds(45);
    private readonly ITorrentService _torrentService;
    private readonly ITopLevelService _topLevelService;
    private readonly IAppSettingsService _settingsService;
    private readonly IMagnetMetadataPreviewService _metadataPreviewService;

    public AddTorrentDialogService(
        ITorrentService torrentService,
        ITopLevelService topLevelService,
        IAppSettingsService settingsService,
        IMagnetMetadataPreviewService metadataPreviewService)
    {
        _torrentService = torrentService;
        _topLevelService = topLevelService;
        _settingsService = settingsService;
        _metadataPreviewService = metadataPreviewService;
    }

    public async Task<bool> ShowTorrentFileAsync(string torrentPath)
    {
        if (!_settingsService.Current.ShowAddTorrentOptions)
        {
            await _torrentService.AddTorrentFileAsync(torrentPath);
            return true;
        }

        var viewModel = await AddTorrentOptionsViewModel.CreateForTorrentFileAsync(
            torrentPath,
            _torrentService,
            _topLevelService,
            _settingsService);

        return await ShowAsync(viewModel);
    }

    public Task<bool> ShowMagnetAsync(string magnetUri)
    {
        if (!_settingsService.Current.ShowAddTorrentOptions)
        {
            return AddMagnetWithoutDialogAsync(magnetUri);
        }

        var viewModel = AddTorrentOptionsViewModel.CreateForMagnet(
            magnetUri,
            _torrentService,
            _topLevelService,
            _settingsService);
        viewModel.StartMetadataPreview(_metadataPreviewService, MagnetMetadataPreviewTimeout);

        return ShowAsync(viewModel);
    }

    private async Task<bool> AddMagnetWithoutDialogAsync(string magnetUri)
    {
        await _torrentService.AddMagnetAsync(magnetUri);
        return true;
    }

    private async Task<bool> ShowAsync(AddTorrentOptionsViewModel viewModel)
    {
        var window = new AddTorrentOptionsWindow
        {
            DataContext = viewModel
        };

        if (_topLevelService.GetTopLevel() is Window owner)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            var closed = new TaskCompletionSource();
            window.Closed += (_, _) => closed.TrySetResult();
            window.Show();
            await closed.Task;
        }

        return viewModel.WasAdded;
    }
}
