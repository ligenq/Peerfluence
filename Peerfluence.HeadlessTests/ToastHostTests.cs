using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Theme.Controls;

namespace Peerfluence.HeadlessTests;

public sealed class ToastHostTests
{
    [AvaloniaTheory]
    [InlineData(PhysicalKey.Enter, true)]
    [InlineData(PhysicalKey.Space, true)]
    [InlineData(PhysicalKey.Enter, false)]
    [InlineData(PhysicalKey.Space, false)]
    [InlineData(PhysicalKey.Escape, false)]
    public void Keyboard_ActivatesOrDismissesAndRestoresFocus(PhysicalKey key, bool activate)
    {
        var manager = new ToastManager();
        var host = new ToastHost { Manager = manager, DismissLabel = "Close notification" };
        var field = new TextBox { Text = "Working" };
        var window = new Window { Content = new Panel { Children = { field, host } }, Width = 600, Height = 400 };
        window.Show();
        try
        {
            field.Focus();
            int activations = 0;
            var toast = new Toast { Title = "Finished", Message = "Open download", Clicked = () => activations++ };
            manager.Show(toast);
            Layout(window);
            Assert.True(field.IsFocused);
            var body = host.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_ActivateToast");
            var close = host.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "PART_DismissToast");
            Assert.Equal(toast.Title, AutomationProperties.GetName(body));
            Assert.Equal("Close notification", AutomationProperties.GetName(close));
            window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
            Assert.True(body.IsFocused);
            if (!activate)
            {
                window.KeyPressQwerty(PhysicalKey.Tab, RawInputModifiers.None);
                Assert.True(close.IsFocused);
            }

            window.KeyPressQwerty(key, RawInputModifiers.None);
            window.KeyReleaseQwerty(key, RawInputModifiers.None);
            Layout(window);
            Assert.Empty(manager.Toasts);
            Assert.Equal(activate ? 1 : 0, activations);
            Assert.True(field.IsFocused);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pointer_ActivatesBodyOrDismissesWithoutActivating(bool activate)
    {
        var manager = new ToastManager();
        var host = new ToastHost { Manager = manager };
        var window = new Window { Content = host, Width = 600, Height = 400 };
        window.Show();
        try
        {
            int activations = 0;
            manager.Show(new Toast { Title = "Finished", Clicked = () => activations++ });
            Layout(window);
            var button = host.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == (activate ? "PART_ActivateToast" : "PART_DismissToast"));
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Layout(window);
            Assert.Empty(manager.Toasts);
            Assert.Equal(activate ? 1 : 0, activations);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void RemovingFocusedToast_RestoresFocusWithoutRunningItsAction()
    {
        var manager = new ToastManager();
        var host = new ToastHost { Manager = manager };
        var field = new TextBox();
        var window = new Window { Content = new Panel { Children = { field, host } }, Width = 600, Height = 400 };
        window.Show();
        try
        {
            field.Focus();
            int activations = 0;
            var toast = new Toast { Title = "Finished", Clicked = () => activations++ };
            manager.Show(toast);
            Layout(window);
            host.GetVisualDescendants().OfType<Button>().First().Focus(NavigationMethod.Tab);
            // Automatic expiry and capacity eviction use the same manager removal path.
            manager.Dismiss(toast);
            Layout(window);
            Assert.True(field.IsFocused);
            Assert.Equal(0, activations);
        }
        finally { window.Close(); }
    }

    private static void Layout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
