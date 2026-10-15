# Peerfluence.Theme

A compact Avalonia desktop theme with light and dark variants and eight accents. MIT licensed.
It builds on Fluent control templates and input handling. The application supplies its font.

```xml
<Application.Styles>
  <FluentTheme />
  <theme:PeerfluenceTheme />
</Application.Styles>
```

Add `xmlns:theme="using:Peerfluence.Theme"`. Register Inter, for example, with
`.WithInterFont()` on the application's `AppBuilder`. Change the application-wide variant with
`Application.Current.RequestedThemeVariant` and the accent with
`theme.UseAccent("Emerald")`. Unknown accent names return `false` without changing the palette.

Accents: Teal, Indigo, Cobalt, Emerald, Amber, Rose, Violet, Slate.
Requires Avalonia 12.1 and .NET 10. The theme library supports trimming and NativeAOT; the gallery
is a separate development tool and is not shipped with Peerfluence.

## Responsibilities

| File | Responsibility |
| --- | --- |
| `Themes/Metrics.axaml` | Spacing, density, radii and typography sizes |
| `Themes/Palette.axaml` | Shared brushes, light/dark surfaces, text and semantic colors |
| `Themes/Accents.axaml` | Accent colors for both variants |
| `Themes/FluentResources.axaml` | Fluent's resource keys mapped to Peerfluence tokens |
| `Themes/Controls.axaml` | Layout and deliberate visual differences, including segmented choices |
| `Themes/Variants.axaml` | Complete presenter states for quiet, destructive, navigation and toolbar variants |
| `Themes/Overlays.axaml`, `Controls/` | Templates and behavior for dialogs, notifications and status bars |

Prefer changing resource mappings to overriding template parts. For example, primary buttons use
Fluent's existing `accent` class and all `AccentButton*` resources, including disabled states.
Quiet, destructive, navigation and toolbar variants still need presenter overrides because Fluent
sets their interaction colors on the presenter. Keep those overrides together in `Variants.axaml`,
with disabled states taking precedence over selection, hover and press.

Use `DynamicResource` for Peerfluence tokens at control call sites. Accent switching preserves the
shared brush objects so existing controls and Fluent resource aliases repaint together. Apply the
variant at application scope: these shared brushes represent one palette for the application.

## Control states

| Family | States to preserve when changing the theme |
| --- | --- |
| Buttons (`accent`, `ghost`, `danger`, `icon`) | Normal, hover, pressed, disabled, visible keyboard focus |
| Toolbar toggle (`toolbar`) | Off, on, mixed; hover, pressed, disabled, keyboard focus |
| Checkboxes | Off, on, mixed; hover, pressed, disabled; readable glyphs |
| Radio buttons and switches | Off/on; hover, pressed, disabled; readable glyphs |
| Segmented radio (`chip`) | Selected/unselected; hover, pressed, disabled, keyboard focus and exclusive choice |
| Text, numeric and combo fields | Normal, hover, focus, disabled; editing and spinning |
| Lists and navigation (`nav`) | Selected/unselected; hover, pressed, disabled, keyboard navigation |
| Tabs and disclosure (`disclosure`) | Selected/expanded and unselected/collapsed; hover, pressed, disabled, keyboard focus |
| DataGrid | Row hover, whole-row selection, Ctrl/Shift selection and keyboard navigation |

DataGrid retains its internal current cell for keyboard navigation and embedded controls. Its cell
outlines are hidden; selection is communicated by the entire row.

## Overlay components

These controls own behavior as well as styling. Changes require interaction verification:

- `DialogHost`: focus enters the dialog, Tab stays within it, Escape and scrim clicks cancel,
  content clicks stay inside, and closing restores the previous focus. Detaching releases the
  manager subscription; reattaching synchronizes the current dialog.
- `ToastHost`: arrival leaves focus in place. Tab reaches the body and separate close button.
  Enter/Space on the body dismisses and invokes its action once; Close/Escape dismisses without
  invoking it. Removal and manager replacement restore the previous focus. Capacity is four.
- `InfoBar`: close is a real keyboard-accessible button. Closing preserves visibility bindings so
  a later status can appear. `IsClosable=false` removes the close action from interaction.

Set `DismissLabel` on `ToastHost` and closable `InfoBar` instances to the application's localized
close label. Managers are UI-thread affine and verify access. No application services or view
models belong in this library.

## Visual gallery

From the repository root, open the interactive control gallery:

```sh
dotnet run --project Peerfluence.Theme.Gallery
```

It displays enabled, disabled, selected and mixed controls, editable fields, a populated grid,
tabs, disclosure, status bars, notifications and dialogs. Use Tab, Shift+Tab, arrow keys,
Enter, Space and Escape. Change the variant/accent while selections are active.

Capture the default accent in both variants without opening a desktop window:

```sh
dotnet run --project Peerfluence.Theme.Gallery -- --capture TestResults/theme-gallery
```

Add `--all-accents` to capture all eight accents. Open the generated `index.html` to compare
captures. Hover, press and keyboard-focus images use actual input/focus changes; the exporter does
not force pseudo-classes. Popup, notification and dialog captures show their complete surfaces.
The gallery uses Skia and the same Fluent/theme pair as the application, but does not load the
application, its settings, downloads or network services.

CI renders and uploads the default-accent gallery on Windows and Linux. Deep Validation captures
all accents. These artifacts support visual review; they are not pixel-difference assertions.
Compare images produced on the same OS, font stack and rendering environment.

## Avalonia upgrade checks

1. Update the aligned Avalonia package versions, including the gallery and tests.
2. Compare the resource keys in `FluentResources.axaml` with the new Fluent templates. Remove
   obsolete aliases and map any newly introduced interaction states.
3. Review the remaining template dependencies: button/toggle/list presenters; the segmented
   radio template; the selected tab pipe; DataGrid row background and cell focus/currency visuals;
   and the disclosure header. Their selectors are compatibility code, not a stable Avalonia API.
4. Run `dotnet test Peerfluence.slnx`. The control-state tests exercise every accent and both
   variants with real pointer/keyboard input; overlay tests cover focus, dismissal and lifecycle.
5. Generate `--all-accents` captures and inspect focus cues, disabled contrast, dropdowns, selected
   rows, wrapping, notification actions and dialog boundaries. Also review the real application's
   screenshots using `Peerfluence.UiTests/ScreenshotGallery.cs` for layout regressions.

Passing behavioral tests alone does not establish visual consistency or accessibility compliance.
The gallery and the real-application checks complement those tests.
