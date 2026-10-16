using Avalonia;
using Avalonia.Controls;

namespace Peerfluence.Theme.Controls;

/// <summary>
/// Shows a <see cref="TextBlock"/>'s full text as its tooltip, but only while trimming is actually
/// hiding part of it.
/// </summary>
/// <remarks>
/// <para>
/// A selector cannot ask whether text was trimmed, so a plain <c>ToolTip.Tip</c> setter in a style
/// attaches a tooltip to every match - including the short ones, where it pops up to repeat text
/// already fully on screen. Enable this instead and the tooltip appears only where it says
/// something the cell does not.
/// </para>
/// <para>
/// Two things can change the answer: the text, and the width it has to fit into. Both are watched.
/// Width arrives as a <see cref="Visual.BoundsProperty"/> change, which is set during arrange -
/// after the measure that rebuilt <see cref="TextBlock.TextLayout"/>, so the layout consulted here
/// is the one that produced those bounds.
/// </para>
/// </remarks>
public static class OverflowTooltip
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, bool>("IsEnabled", typeof(OverflowTooltip));

    static OverflowTooltip()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBlock>(
            (target, args) => OnIsEnabledChanged(target, args.GetNewValue<bool>()));
    }

    public static bool GetIsEnabled(TextBlock target)
    {
        ArgumentNullException.ThrowIfNull(target);
        return target.GetValue(IsEnabledProperty);
    }

    public static void SetIsEnabled(TextBlock target, bool value)
    {
        ArgumentNullException.ThrowIfNull(target);
        target.SetValue(IsEnabledProperty, value);
    }

    private static void OnIsEnabledChanged(TextBlock target, bool enabled)
    {
        // Unsubscribed first either way, so a style re-applying the setter cannot stack handlers.
        target.PropertyChanged -= OnTextBlockPropertyChanged;
        if (enabled)
        {
            target.PropertyChanged += OnTextBlockPropertyChanged;
        }

        Update(target, enabled);
    }

    private static void OnTextBlockPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (sender is TextBlock target
            && (args.Property == Visual.BoundsProperty || args.Property == TextBlock.TextProperty))
        {
            Update(target, GetIsEnabled(target));
        }
    }

    private static void Update(TextBlock target, bool enabled)
    {
        // SetCurrentValue rather than SetValue: the tooltip is this helper's own running state, not
        // a local value that should outrank a style a consumer wrote for the same control.
        target.SetCurrentValue(ToolTip.TipProperty, enabled && IsTrimmed(target) ? target.Text : null);
    }

    private static bool IsTrimmed(TextBlock target)
    {
        foreach (var line in target.TextLayout.TextLines)
        {
            if (line.HasCollapsed)
            {
                return true;
            }
        }

        return false;
    }
}
