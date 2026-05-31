using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using SukiUI;
using SukiUI.Controls;
using SukiUI.Enums;
using SukiUI.Models;

namespace Peerfluence.Services;

public sealed class ThemeService : IThemeService
{
    private static readonly IReadOnlyDictionary<string, ThemePalette> Palettes = new Dictionary<string, ThemePalette>
    {
        ["Indigo"] = new("#3f51b5", "#283593", "#ff6f00", "#e65100"),
        ["Cobalt"] = new("#1565c0", "#0d47a1", "#00acc1", "#00838f"),
        ["Mint"] = new("#2e7d32", "#1b5e20", "#00c853", "#00a152"),
        ["Emerald"] = new("#059669", "#064e3b", "#34d399", "#059669"),
        ["Rose"] = new("#c2185b", "#880e4f", "#ff5252", "#ff1744"),
        ["Vibrant"] = new("#9333ea", "#581c87", "#f97316", "#c2410c"),
        ["Amber"] = new("#d97706", "#78350f", "#fbbf24", "#d97706"),
        ["Slate"] = new("#455a64", "#263238", "#90a4ae", "#607d8b"),
        ["Solar"] = new("#f9a825", "#f57f17", "#fdd835", "#f9a825")
    };

    public void Apply(ThemeSettings settings)
    {
        ApplyVariant(settings.ThemeVariant);
        ApplyPalette(settings.ColorTheme);
        ApplyBackgroundStyle(settings.BackgroundStyle);
    }

    private void ApplyVariant(string variant)
    {
        if (Application.Current == null)
        {
            return;
        }

        if (variant is "Light" or "Dark")
        {
            var theme = SukiTheme.GetInstance(Application.Current);
            theme.ChangeBaseTheme(variant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark);
            return;
        }

        Application.Current.RequestedThemeVariant = ThemeVariant.Default;
    }

    private void ApplyPalette(string themeName)
    {
        if (Application.Current == null || !Palettes.TryGetValue(themeName, out var palette))
        {
            return;
        }

        var theme = SukiTheme.GetInstance(Application.Current);
        var existingTheme = theme.ColorThemes.FirstOrDefault(candidate => candidate.DisplayName == themeName);
        if (existingTheme == null)
        {
            existingTheme = new SukiColorTheme(themeName, palette.Primary, palette.Accent);
            theme.AddColorTheme(existingTheme);
        }

        theme.ChangeColorTheme(existingTheme);

        var resources = Application.Current.Resources;
        resources["SukiPrimaryDarkColor"] = palette.PrimaryDark;
        resources["SukiAccentDarkColor"] = palette.AccentDark;
    }

    private void ApplyBackgroundStyle(string styleName)
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        if (desktop.MainWindow is not SukiWindow sukiWindow)
        {
            return;
        }

        if (!Enum.TryParse<SukiBackgroundStyle>(styleName, out var style))
        {
            style = SukiBackgroundStyle.GradientSoft;
        }

        sukiWindow.BackgroundStyle = style;
    }

    private sealed record ThemePalette(Color Primary, Color PrimaryDark, Color Accent, Color AccentDark)
    {
        public ThemePalette(string primary, string primaryDark, string accent, string accentDark)
            : this(Color.Parse(primary), Color.Parse(primaryDark), Color.Parse(accent), Color.Parse(accentDark))
        {
        }
    }
}

