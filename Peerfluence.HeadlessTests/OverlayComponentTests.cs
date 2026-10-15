using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Theme.Controls;

namespace Peerfluence.HeadlessTests;

public sealed class OverlayComponentTests
{
    [AvaloniaFact]
    public void InfoBar_ClosesFromTheKeyboardWithoutLosingItsVisibilityBinding()
    {
        var state = new StatusState();
        var bar = new InfoBar { Message = "First status", DismissLabel = "Dismiss status", DataContext = state };
        bar.Bind(Visual.IsVisibleProperty, new Binding(nameof(StatusState.IsVisible)));
        var window = new Window { Content = bar, Width = 600, Height = 400 };
        window.Show();
        try
        {
            Layout(window);
            var close = bar.GetVisualDescendants().OfType<Button>().Single();
            Assert.Equal("Dismiss status", AutomationProperties.GetName(close));
            close.Focus(NavigationMethod.Tab);
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Assert.False(bar.IsVisible);
            state.IsVisible = false;
            state.IsVisible = true;
            bar.Message = "New status";
            Layout(window);
            Assert.True(bar.IsVisible);
            Assert.Contains(bar.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "New status");
            bar.IsClosable = false;
            Layout(window);
            Assert.False(close.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Dialog_CanReattachAndSwitchManagersWithoutLosingFocusContainment()
    {
        var first = new DialogManager();
        var second = new DialogManager();
        var host = new DialogHost { Manager = first };
        var background = new TextBox { Text = "Working" };
        var panel = new Panel { Children = { background, host } };
        var window = new Window { Content = panel, Width = 700, Height = 500 };
        window.Show();
        try
        {
            background.Focus();
            var field = new TextBox { Text = "First dialog" };
            first.Show(field);
            Layout(window);
            Assert.True(field.IsFocused);
            panel.Children.Remove(host);
            Assert.True(background.IsFocused);
            first.Close();
            var replacementField = new TextBox { Text = "Second dialog" };
            second.Show(replacementField);
            host.Manager = second;
            panel.Children.Add(host);
            Layout(window);
            Assert.True(host.IsOpen);
            Assert.True(replacementField.IsFocused);
            background.Focus();
            Assert.True(replacementField.IsFocused);
            // Notifications/changes from the previous manager cannot replace the current dialog.
            first.Show(new TextBox());
            Layout(window);
            Assert.Same(replacementField, host.DialogContent);
            window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Assert.False(second.IsOpen);
            Assert.True(first.IsOpen);
            Assert.True(background.IsFocused);
        }
        finally { first.Close(); second.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public void Toasts_EvictOldestAtCapacityAndChangingManagersRestoresFocus()
    {
        var first = new ToastManager();
        var second = new ToastManager();
        var host = new ToastHost { Manager = first };
        var background = new TextBox();
        var window = new Window { Content = new Panel { Children = { background, host } }, Width = 600, Height = 500 };
        window.Show();
        try
        {
            background.Focus();
            var oldest = new Toast { Title = "Oldest" };
            first.Show(oldest);
            for (int i = 0; i < 4; i++) first.Show(new Toast { Title = $"Notification {i}" });
            Layout(window);
            Assert.Equal(4, first.Toasts.Count);
            Assert.DoesNotContain(oldest, first.Toasts);
            Assert.Equal(4, host.GetVisualDescendants().OfType<Button>().Count(button => button.Name == "PART_ActivateToast"));
            host.GetVisualDescendants().OfType<Button>().First().Focus(NavigationMethod.Tab);
            second.Show(new Toast { Title = "Replacement" });
            host.Manager = second;
            Layout(window);
            Assert.True(background.IsFocused);
            Assert.Single(host.GetVisualDescendants().OfType<Button>(), button => button.Name == "PART_ActivateToast");
            Assert.Contains(host.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Replacement");
        }
        finally { window.Close(); }
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private sealed class StatusState : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool IsVisible
        {
            get;
            set { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible))); }
        } = true;
    }
}
