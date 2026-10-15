using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Styling;
using Peerfluence.Theme.Controls;

namespace Peerfluence.Theme.Gallery;

/// <summary>Exercises the public theme without loading Peerfluence, settings, or services.</summary>
public sealed class GalleryWindow : Window
{
    private static readonly string[] DownloadStates = ["Downloads", "Completed", "Seeding"];
    private static readonly string[] UnavailableStates = ["Unavailable"];
    private readonly ToastManager _toasts = new();
    private readonly DialogManager _dialogs = new();
    private readonly ComboBox _variant;
    private readonly ComboBox _accent;
    private readonly TextBlock _result = Label("Actions appear here. Tab moves through the real controls.");

    public Dictionary<string, Control> Targets { get; } = new(StringComparer.Ordinal);

    public GalleryWindow()
    {
        Title = "Peerfluence theme gallery";
        Width = 1180;
        Height = 980;
        MinWidth = 950;
        MinHeight = 700;
        _variant = new ComboBox { ItemsSource = new[] { "Light", "Dark" }, SelectedIndex = 0, Width = 100 };
        _accent = new ComboBox { ItemsSource = PeerfluenceTheme.Accents, SelectedIndex = 0, Width = 120 };
        AutomationProperties.SetName(_variant, "Theme variant");
        AutomationProperties.SetName(_accent, "Accent");
        _variant.SelectionChanged += (_, _) => Application.Current!.RequestedThemeVariant =
            _variant.SelectedIndex == 0 ? ThemeVariant.Light : ThemeVariant.Dark;
        _accent.SelectionChanged += (_, _) => CurrentTheme.UseAccent(_accent.SelectedItem as string);

        var title = Label("Control gallery");
        title.Classes.Add("title");
        var header = Row(title, new Separator { Width = 16, Opacity = 0 }, Label("Variant"), _variant, Label("Accent"), _accent);
        var left = Column(
            Section("Actions · enabled / disabled", ActionSamples()),
            Section("Fields · enabled / disabled", FieldSamples()),
            Section("Selection and navigation", NavigationSamples()));
        var right = Column(
            Section("Choices · off / on / mixed / disabled", ChoiceSamples()),
            Section("Tabs and disclosure", DisclosureSamples()),
            Section("Status and overlays", OverlaySamples()));
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 24 };
        columns.Children.Add(left);
        Grid.SetColumn(right, 1);
        columns.Children.Add(right);
        var body = Column(header, Label("Hover, press, use Tab and arrow keys. Change the palette while a control is selected."), columns, _result);
        body.Margin = new Thickness(24);
        Content = new Panel
        {
            Children =
            {
                new ScrollViewer { Content = body },
                new ToastHost { Manager = _toasts },
                new DialogHost { Manager = _dialogs },
            },
        };
    }

    private static PeerfluenceTheme CurrentTheme => Application.Current!.Styles.OfType<PeerfluenceTheme>().Single();

    public void SetPalette(string accent, bool dark)
    {
        _accent.SelectedItem = accent;
        _variant.SelectedIndex = dark ? 1 : 0;
        Application.Current!.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        CurrentTheme.UseAccent(accent);
    }

    public void ResetFocus() => _variant.Focus();

    public void ShowToasts()
    {
        foreach (var severity in Enum.GetValues<ToastSeverity>())
        {
            _toasts.Show(new Toast
            {
                Title = $"{severity}: download notification",
                Message = "A longer message wraps inside the card. Activate it to open the result, or close it separately.",
                Severity = severity,
                Clicked = () => _result.Text = "Notification activated.",
            });
        }
    }

    public void ShowDialog()
    {
        var field = new TextBox { PlaceholderText = "Enter a name", Text = "Sample torrent" };
        AutomationProperties.SetName(field, "Torrent name");
        var accept = Action("Save", "accent");
        var cancel = Action("Cancel");
        accept.IsDefault = true;
        cancel.IsCancel = true;
        accept.Click += (_, _) => { _result.Text = $"Saved: {field.Text}"; _dialogs.Close(); };
        cancel.Click += (_, _) => _dialogs.Close();
        _dialogs.Show(Column(Label("Rename download"), field, Row(accept, cancel)));
    }

    private Control ActionSamples()
    {
        var rows = Column();
        foreach (var (name, classes) in new[] { ("Standard", ""), ("Primary", "accent"), ("Quiet", "ghost"), ("Destructive", "danger") })
        {
            var button = Track(name.ToLowerInvariant(), Action(name, classes));
            if (classes.Length == 0)
            {
                button.ContextMenu = new ContextMenu
                {
                    Items = { new MenuItem { Header = "Open download" }, new MenuItem { Header = "Unavailable action", IsEnabled = false } },
                };
            }
            var disabled = Action(name, classes);
            disabled.IsEnabled = false;
            rows.Children.Add(Row(button, disabled));
        }
        return rows;
    }

    private Control FieldSamples()
    {
        var field = Track("text", new TextBox { Text = "Editable text", Width = 220 });
        var number = Track("number", new NumericUpDown { Value = 42, Minimum = 0, Maximum = 100, Width = 220 });
        var combo = Track("combo", new ComboBox { ItemsSource = DownloadStates, SelectedIndex = 0, Width = 220 });
        return Column(
            Row(field, new TextBox { Text = "Unavailable", IsEnabled = false, Width = 220 }),
            Row(number, new NumericUpDown { Value = 42, IsEnabled = false, Width = 220 }),
            Row(combo, new ComboBox { ItemsSource = UnavailableStates, SelectedIndex = 0, IsEnabled = false, Width = 220 }));
    }

    private Control ChoiceSamples()
    {
        var check = Track("check", new CheckBox { Content = "Checked", IsChecked = true });
        var radio = Track("radio", new RadioButton { Content = "Selected", IsChecked = true, GroupName = "radio" });
        var toggle = Track("switch", new ToggleSwitch { IsChecked = true });
        var chip = Track("chip", new RadioButton { Content = "Selected", IsChecked = true, GroupName = "chip", Classes = { "chip" } });
        var toolbar = Track("toolbar", new ToggleButton { Content = "Details", IsChecked = true, Classes = { "toolbar" } });
        return Column(
            Row(new CheckBox { Content = "Off" }, check, new CheckBox { Content = "Mixed", IsThreeState = true, IsChecked = null }, new CheckBox { Content = "Disabled", IsChecked = true, IsEnabled = false }),
            Row(new RadioButton { Content = "Off", GroupName = "radio" }, radio, new RadioButton { Content = "Disabled", IsChecked = true, IsEnabled = false, GroupName = "disabled-radio" }),
            Row(new ToggleSwitch(), toggle, new ToggleSwitch { IsChecked = true, IsEnabled = false }),
            Row(new RadioButton { Content = "Other", GroupName = "chip", Classes = { "chip" } }, chip,
                new RadioButton { Content = "Disabled", IsChecked = true, IsEnabled = false, Classes = { "chip" } }),
            Row(new ToggleButton { Content = "Details", Classes = { "toolbar" } }, toolbar,
                new ToggleButton { Content = "Details", IsChecked = true, IsEnabled = false, Classes = { "toolbar" } }));
    }

    private Control NavigationSamples()
    {
        var nav = new ListBox
        {
            Classes = { "nav" },
            Items = { new ListBoxItem { Content = "Downloads" }, Track("list", new ListBoxItem { Content = "Selected destination" }), new ListBoxItem { Content = "Unavailable", IsEnabled = false } },
            SelectedIndex = 1,
        };
        var about = Track("navigation", Action("About", "ghost nav selected"));
        about.HorizontalAlignment = HorizontalAlignment.Stretch;
        about.HorizontalContentAlignment = HorizontalAlignment.Left;
        var grid = new DataGrid { AutoGenerateColumns = false, IsReadOnly = true, Height = 130, ItemsSource = new[] { new SampleRow("Ubuntu.iso", "Downloading"), new SampleRow("Archive.zip", "Complete") }, SelectionMode = DataGridSelectionMode.Extended };
        grid.Columns.Add(new DataGridTextColumn { Header = "Name", Binding = new Binding(nameof(SampleRow.Name)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
        grid.Columns.Add(new DataGridTextColumn { Header = "State", Binding = new Binding(nameof(SampleRow.State)) });
        grid.SelectedIndex = 0;
        return Column(nav, about, grid, new ProgressBar { Value = 65 });
    }

    private Control DisclosureSamples()
    {
        var tabs = new TabControl { Items = { Track("tab", new TabItem { Header = "Selected", Content = Label("Use the arrow keys to change tabs.") }), new TabItem { Header = "Other" }, new TabItem { Header = "Disabled", IsEnabled = false } }, SelectedIndex = 0 };
        var expander = Track("disclosure", new Expander { Header = "Legal notice", Content = Label("Expanded content uses the same surface, border and typography."), IsExpanded = true, Classes = { "disclosure" } });
        return Column(tabs, expander);
    }

    private Control OverlaySamples()
    {
        var toast = Action("Show notifications");
        toast.Click += (_, _) => ShowToasts();
        var dialog = Action("Show dialog");
        dialog.Click += (_, _) => ShowDialog();
        return Column(
            new InfoBar { Message = "Information: status belongs to this screen." },
            new InfoBar { Message = "Warning: check the download directory.", Severity = ToastSeverity.Warning },
            new InfoBar { Message = "Error: the tracker could not be reached.", Severity = ToastSeverity.Error },
            Row(toast, dialog));
    }

    private T Track<T>(string name, T control) where T : Control
    {
        Targets.Add(name, control);
        AutomationProperties.SetName(control, name);
        return control;
    }

    private Button Action(string text, string classes = "")
    {
        var button = new Button { Content = new TextBlock { Text = text } };
        foreach (string value in classes.Split(' ', StringSplitOptions.RemoveEmptyEntries)) button.Classes.Add(value);
        AutomationProperties.SetName(button, text);
        button.Click += (_, _) => _result.Text = $"Activated {text}.";
        return button;
    }

    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private static StackPanel Row(params Control[] children) => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }.WithChildren(children);
    private static StackPanel Column(params Control[] children) => new StackPanel { Spacing = 12 }.WithChildren(children);
    private static Control Section(string title, Control content) => Column(new TextBlock { Text = title, FontWeight = Avalonia.Media.FontWeight.SemiBold }, content);
    private sealed record SampleRow(string Name, string State);
}

internal static class PanelExtensions
{
    public static T WithChildren<T>(this T panel, IEnumerable<Control> children) where T : Panel
    {
        panel.Children.AddRange(children);
        return panel;
    }
}
