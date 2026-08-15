using Peerfluence.Core.Config;
using Peerfluence.Core.Messaging;
using PeerSharp.Core;

namespace Peerfluence.Core.Services;

public sealed class TorrentCategoryService : ITorrentCategoryService
{
    private readonly IAppSettingsService _settingsService;
    private readonly IAppMessenger _messenger;

    public TorrentCategoryService(IAppSettingsService settingsService, IAppMessenger messenger)
    {
        _settingsService = settingsService;
        _messenger = messenger;
    }

    private CategorySettings Settings => _settingsService.Current.Categories;

    public IReadOnlyList<TorrentCategory> Categories => Settings.Categories;

    public string? GetCategory(InfoHash hash)
    {
        if (Key(hash) is not { } key)
        {
            return null;
        }

        // A name that no longer exists reads as no category rather than as a category: removal
        // unfiles everything, but a hand-edited settings file can still say otherwise.
        return Settings.Assignments.TryGetValue(key, out var name) && Exists(name) ? name : null;
    }

    public async Task AssignAsync(InfoHash hash, string? categoryName, CancellationToken cancellationToken = default)
    {
        if (Key(hash) is not { } key)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(categoryName) || !Exists(categoryName))
        {
            Settings.Assignments.Remove(key);
        }
        else
        {
            Settings.Assignments[key] = categoryName;
        }

        await SaveAndAnnounceAsync(cancellationToken).ConfigureAwait(false);
    }

    public string? ResolveSavePath(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return null;
        }

        var category = Find(categoryName);
        return category is { HasSavePath: true } ? category.SavePath : null;
    }

    public async Task AddAsync(string name, string savePath, CancellationToken cancellationToken = default)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || Exists(trimmed))
        {
            // Nothing to add, and nothing worth complaining about: the category the user asked for
            // is already there under that name.
            return;
        }

        Settings.Categories.Add(new TorrentCategory(trimmed, savePath?.Trim() ?? string.Empty));
        await SaveAndAnnounceAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        if (Find(name) is not { } category)
        {
            return;
        }

        Settings.Categories.Remove(category);

        // Unfiled rather than left pointing at nothing.
        foreach (var key in Settings.Assignments
            .Where(pair => string.Equals(pair.Value, category.Name, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .ToList())
        {
            Settings.Assignments.Remove(key);
        }

        await SaveAndAnnounceAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ForgetMissingAsync(IEnumerable<InfoHash> present, CancellationToken cancellationToken = default)
    {
        var alive = present
            .Select(Key)
            .Where(key => key != null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase)!;

        var stale = Settings.Assignments.Keys.Where(key => !alive.Contains(key)).ToList();
        if (stale.Count == 0)
        {
            return;
        }

        foreach (var key in stale)
        {
            Settings.Assignments.Remove(key);
        }

        await SaveAndAnnounceAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task SaveAndAnnounceAsync(CancellationToken cancellationToken)
    {
        await _settingsService.SaveAsync(cancellationToken).ConfigureAwait(false);
        _messenger.Publish(new CategoriesChangedMessage());
    }

    private bool Exists(string? name) => Find(name) != null;

    private TorrentCategory? Find(string? name)
    {
        return name == null
            ? null
            : Settings.Categories.FirstOrDefault(
                category => string.Equals(category.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The key an assignment is stored under. Null for a torrent with no hash yet - a magnet whose
    /// metadata has not arrived - because filing something that cannot be identified would file it
    /// under the empty string and hand that category to the next one.
    /// </summary>
    private static string? Key(InfoHash hash)
    {
        return hash.IsEmpty ? null : hash.ToHexStringUpper();
    }
}
