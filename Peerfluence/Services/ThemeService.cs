using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Peerfluence.Theme;

namespace Peerfluence.Services;

public sealed class ThemeService : IThemeService, IDisposable
{
    private IPlatformSettings? _platformSettings;
    private bool _followsSystem;
    private bool _disposed;

    public ThemeService()
    {
    }

    internal ThemeService(IPlatformSettings platformSettings)
    {
        _platformSettings = platformSettings;
    }

    /// <summary>
    /// Accent names that existed before the theme was replaced, and what they became.
    /// </summary>
    /// <remarks>
    /// Settings already on disk name these. Mapping them is three lines; the alternative is every
    /// existing installation silently reverting to the default accent on upgrade, which reads as
    /// the update having lost a preference.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Renamed =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Mint"] = "Emerald",
            ["Vibrant"] = "Violet",
            ["Solar"] = "Amber",
        };

    /// <summary>
    /// Variants that existed before the application settled on the Midnight pair.
    /// </summary>
    /// <remarks>
    /// The neutral variants still ship in the theme package, which other applications use, but
    /// this one no longer offers them. Settings already on disk name them, so they are mapped to
    /// the Midnight of the same brightness rather than silently falling back to the default.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> RetiredVariants =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Light"] = "MidnightLight",
            ["Dark"] = "Midnight",
        };

    public void Apply(ThemeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ObjectDisposedException.ThrowIf(_disposed, this);

        FollowSystem(NormalizeVariant(settings.ThemeVariant) == "System");
        ApplyVariant(Resolve(settings.ThemeVariant, _platformSettings));
        ApplyAccent(settings.ColorTheme);
    }

    // Keep the stored choice separate from its current appearance: System must remain System
    // in the settings screen even though it renders as one of the Midnight variants.
    internal static string NormalizeVariant(string? variant)
    {
        if (variant is not null && RetiredVariants.TryGetValue(variant, out string? migrated))
        {
            return migrated;
        }

        return variant is "Midnight" or "MidnightLight" ? variant : "System";
    }

    /// <summary>
    /// Which variant a stored name means, after migration and after resolving "System".
    /// </summary>
    /// <remarks>
    /// "System" cannot be left to <see cref="ThemeVariant.Default"/> any more. Avalonia resolves
    /// that to its own Light and Dark, which this application no longer offers, so following the
    /// operating system means reading its preference and choosing between the Midnight pair here.
    /// </remarks>
    public static ThemeVariant Resolve(string? variant) =>
        Resolve(variant, Application.Current?.PlatformSettings);

    private static ThemeVariant Resolve(string? variant, IPlatformSettings? platformSettings)
    {
        var named = PeerfluenceTheme.VariantFor(NormalizeVariant(variant));
        if (!Equals(named, ThemeVariant.Default))
        {
            return named;
        }

        return FollowsSystem(platformSettings) ? PeerfluenceTheme.Midnight : PeerfluenceTheme.MidnightLight;
    }

    private static bool FollowsSystem(IPlatformSettings? platformSettings)
    {
        // Best effort: a platform with no colour values to report is read as light, which is what
        // Avalonia's own default does, rather than throwing on a machine that cannot answer.
        try
        {
            return platformSettings?.GetColorValues().ThemeVariant
                == PlatformThemeVariant.Dark;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void FollowSystem(bool follow)
    {
        // Resolved lazily because DI can construct the service before Avalonia initializes.
        _platformSettings ??= Application.Current?.PlatformSettings;
        if (_followsSystem && _platformSettings is not null)
        {
            _platformSettings.ColorValuesChanged -= OnSystemColorsChanged;
        }

        _followsSystem = follow;
        if (follow && _platformSettings is not null)
        {
            _platformSettings.ColorValuesChanged += OnSystemColorsChanged;
        }
    }

    private void OnSystemColorsChanged(object? sender, PlatformColorValues values)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            RefreshSystemVariant();
        }
        else
        {
            Dispatcher.UIThread.Post(RefreshSystemVariant);
        }
    }

    private void RefreshSystemVariant()
    {
        // A queued notification may arrive after an explicit choice or shutdown. Re-read the
        // preference so an older notification cannot undo a more recent system change either.
        if (_followsSystem && !_disposed)
        {
            ApplyVariant(Resolve("System", _platformSettings));
        }
    }

    public void Dispose()
    {
        FollowSystem(false);
        _disposed = true;
    }

    private static void ApplyVariant(ThemeVariant resolved)
    {
        if (Application.Current is not { } application)
        {
            return;
        }

        application.RequestedThemeVariant = resolved;

        // The typeface a variant brings with it is a style the theme adds and removes, not a
        // resource, so it has to be told separately. See PeerfluenceTheme.UseVariant for why.
        application.Styles.OfType<PeerfluenceTheme>().FirstOrDefault()?.UseVariant(resolved);
    }

    private static void ApplyAccent(string? accent)
    {
        if (Application.Current?.Styles.OfType<PeerfluenceTheme>().FirstOrDefault() is not { } theme)
        {
            return;
        }

        if (accent is not null && Renamed.TryGetValue(accent, out string? current))
        {
            accent = current;
        }

        // Falls back rather than throwing: the name arrives from a settings file, so it can be
        // absent, or something a later version wrote. The fallback is the one belonging to the
        // variant now showing, because the variants no longer offer the same set and the stored
        // name may be perfectly valid for a different one.
        if (!theme.UseAccent(accent))
        {
            theme.UseAccent(PeerfluenceTheme.DefaultAccentFor(Application.Current?.ActualThemeVariant));
        }
    }
}
