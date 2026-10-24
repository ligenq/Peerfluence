using System;
using System.Threading;
using System.Threading.Tasks;
using Peerfluence.ViewModels;
using Peerfluence.Views;

namespace Peerfluence.Services;

public sealed class AddTorrentDialogService : IAddTorrentDialogService
{
    private readonly ITorrentService _torrentService;
    private readonly ITopLevelService _topLevelService;
    private readonly ITorrentCategoryService _categoryService;
    private readonly IAppSettingsService _settingsService;
    private readonly IMagnetMetadataPreviewService _metadataPreviewService;

    public AddTorrentDialogService(
        ITorrentService torrentService,
        ITopLevelService topLevelService,
        IAppSettingsService settingsService,
        IMagnetMetadataPreviewService metadataPreviewService,
        ITorrentCategoryService categoryService)
    {
        _torrentService = torrentService;
        _topLevelService = topLevelService;
        _categoryService = categoryService;
        _settingsService = settingsService;
        _metadataPreviewService = metadataPreviewService;
    }

    public async Task<bool> ShowTorrentFileAsync(string torrentPath)
    {
        var torrentFile = await TorrentFile.LoadAsync(torrentPath);
        if (!_settingsService.Current.ShowAddTorrentOptions && !torrentFile.IsSsl)
        {
            await _torrentService.AddTorrentAsync(torrentFile);
            return true;
        }

        var viewModel = AddTorrentOptionsViewModel.CreateForTorrentFile(
            torrentFile, torrentPath,
            _torrentService,
            _topLevelService,
            _settingsService,
            _categoryService);

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
            _settingsService,
            _categoryService);
        var timeoutSeconds = Math.Clamp(_settingsService.Current.Streaming.MetadataWaitTimeoutSeconds,
            0, Peerfluence.Core.Config.StreamingSettings.MaxWaitTimeoutSeconds);
        viewModel.StartMetadataPreview(_metadataPreviewService,
            timeoutSeconds == 0 ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(timeoutSeconds));

        return ShowAsync(viewModel);
    }

    public Task<bool> ShowSslCertificateAsync(PeerSharp.Interfaces.ITorrent torrent)
    {
        var viewModel = AddTorrentOptionsViewModel.CreateForSslCertificate(
            torrent, _torrentService, _topLevelService, _settingsService);
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

        await _topLevelService.ShowDialogAsync(window);

        return viewModel.WasAdded;
    }
}
