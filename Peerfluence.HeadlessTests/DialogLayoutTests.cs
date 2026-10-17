using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Peerfluence.Core.Config;
using Peerfluence.Core.Services;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Services;
using Peerfluence.Theme;
using Peerfluence.Theme.Controls;
using Peerfluence.Views;

namespace Peerfluence.HeadlessTests;

/// <summary>
/// Measures the gap a person sees, including prompts constructed in C# that AXAML rules cannot read.
/// </summary>
public sealed class DialogLayoutTests
{
    [AvaloniaTheory]
    [InlineData("Midnight", 420)]
    [InlineData("Midnight", 520)]
    [InlineData("MidnightLight", 420)]
    [InlineData("MidnightLight", 520)]
    public async Task LongValidationErrors_WrapOnceAndKeepActionsInsideNarrowDialogs(string variant, int width)
    {
        string errorMessage = "Failed to add magnet. Enter a valid magnet link containing a torrent info hash. "
            + "The value you entered could not be read; check the link and try again. "
            + "The supplied link was magnet:?xt=urn:btih:" + new string('a', 180);
        var app = Application.Current!;
        var previous = app.RequestedThemeVariant;
        var theme = app.Styles.OfType<PeerfluenceTheme>().Single();
        using var themeService = new ThemeService();
        themeService.Apply(new ThemeSettings { ThemeVariant = variant });
        var manager = new DialogManager();
        var window = new Window { Content = new DialogHost { Manager = manager }, Width = width, Height = 560 };
        var dialogs = new DialogService(Substitute.For<ITopLevelService>(), [], manager);
        try
        {
            window.Show();
            var answer = dialogs.PromptForTextAsync(new TextPrompt("Add magnet", "Add",
                InitialText: "not a magnet", Validate: _ => errorMessage));
            Layout();
            window.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.Enter, RawInputModifiers.None);
            Layout();
            Assert.False(answer.IsCompleted);
            var input = window.GetVisualDescendants().OfType<TextBox>()
                .Single(box => AutomationProperties.GetAutomationId(box) == DialogService.PromptTextBoxId);
            Assert.Equal("not a magnet", input.Text);
            Assert.True(input.IsFocused);
            AssertInsideWindow(input);

            var message = Assert.Single(window.GetVisualDescendants().OfType<TextBlock>(),
                text => text.IsEffectivelyVisible && text.Text == errorMessage);
            Assert.True(message.TextLayout.TextLines.Count > 1, "The error must wrap in a narrow dialog.");
            Assert.All(message.TextLayout.TextLines, line =>
            {
                Assert.False(line.HasCollapsed, "Validation errors must remain readable in full.");
                Assert.True(line.WidthIncludingTrailingWhitespace <= message.Bounds.Width + 0.5,
                    "A long word in the validation error extends beyond the message bounds.");
            });
            AssertInsideWindow(message);

            var actions = window.GetVisualDescendants().OfType<Button>()
                .Where(button => button.IsDefault || button.IsCancel).ToArray();
            Assert.Equal(2, actions.Length);
            foreach (var button in actions)
            {
                Assert.True(button.IsEffectivelyEnabled && button.IsEffectivelyVisible);
                AssertInsideWindow(button);
            }

            var cancel = actions.Single(button => button.IsCancel);
            var point = cancel.TranslatePoint(new Point(cancel.Bounds.Width / 2, cancel.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Layout();
            Assert.True(answer.IsCompleted, "Cancel must remain clickable after a long error is shown.");
            Assert.Null(await answer);
        }
        finally
        {
            manager.Close();
            window.Close();
            theme.UseVariant(previous);
            theme.UseAccent(PeerfluenceTheme.DefaultAccent);
            app.RequestedThemeVariant = previous;
        }

        void Layout()
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
        }

        void AssertInsideWindow(Control control)
        {
            var top = control.TranslatePoint(default, window)!.Value;
            var bottom = control.TranslatePoint(new Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
            Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
            Assert.InRange(top.X, -0.5, window.ClientSize.Width);
            Assert.InRange(top.Y, -0.5, window.ClientSize.Height);
            Assert.InRange(bottom.X, 0, window.ClientSize.Width + 0.5);
            Assert.InRange(bottom.Y, 0, window.ClientSize.Height + 0.5);
        }
    }

    [AvaloniaTheory]
    [InlineData("Midnight")]
    [InlineData("MidnightLight")]
    public async Task AllDialogActions_FollowTheSameThemeSpacing(string variant)
    {
        var app = Application.Current!;
        var previousVariant = app.RequestedThemeVariant;
        var theme = app.Styles.OfType<PeerfluenceTheme>().Single();
        bool hadSpacingOverride = app.Resources.TryGetValue("PfSpaceXl", out var previousSpacing);
        using var themeService = new ThemeService();
        themeService.Apply(new ThemeSettings { ThemeVariant = variant });

        var create = new CreateTorrentWindow { DataContext = TestHelpers.CreateCreateTorrentViewModel() };
        var add = new AddTorrentOptionsWindow();
        var manager = new DialogManager();
        var host = new Window { Content = new DialogHost { Manager = manager }, Width = 650, Height = 500 };
        var dialogs = new DialogService(Substitute.For<ITopLevelService>(), [], manager);
        try
        {
            create.Show();
            add.Show();
            host.Show();
            double themeSpacing = Assert.IsType<double>(create.FindResource("PfSpaceXl"));
            Assert.True(themeSpacing > 0, "Dialog actions need a positive gap.");

            // A local constant could accidentally equal today's token and pass. Changing the
            // token proves every row actually follows the shared theme, including existing rows.
            foreach (double spacing in new[] { themeSpacing, themeSpacing + 8 })
            {
                app.Resources["PfSpaceXl"] = spacing;
                AssertActionGap(create, "Create torrent", spacing);
                AssertActionGap(add, "Add torrent", spacing);

                foreach (string prompt in new[] { "Add magnet", "Confirmation", "Remove torrent" })
                {
                    Task answer = prompt switch
                    {
                        "Add magnet" => dialogs.PromptForTextAsync(new TextPrompt("Add magnet", "Add")),
                        "Confirmation" => dialogs.ConfirmAsync(new ConfirmPrompt("Confirm", "Continue?", "Continue", "Cancel")),
                        _ => dialogs.PromptForRemoveOptionsAsync(new RemoveTorrentPrompt(
                            "Remove torrent", "Remove this torrent?", "Remove", "Cancel",
                            RemoveTorrentAction.RemoveOnly,
                            new Dictionary<RemoveTorrentAction, string>
                            {
                                [RemoveTorrentAction.RemoveOnly] = "Keep files",
                                [RemoveTorrentAction.DeleteFiles] = "Delete files",
                            }, "Remember my choice")),
                    };

                    AssertActionGap(host, prompt, spacing);
                    manager.Close();
                    Assert.True(answer.IsCompleted, $"{prompt} did not complete when dismissed.");
                    await answer;
                }
            }
        }
        finally
        {
            manager.Close();
            host.Close();
            add.Close();
            create.Close();
            if (hadSpacingOverride)
            {
                app.Resources["PfSpaceXl"] = previousSpacing!;
            }
            else
            {
                app.Resources.Remove("PfSpaceXl");
            }

            theme.UseVariant(previousVariant);
            theme.UseAccent(PeerfluenceTheme.DefaultAccent);
            app.RequestedThemeVariant = previousVariant;
        }
    }

    private static void AssertActionGap(Window window, string dialog, double expected)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var buttons = window.GetVisualDescendants().OfType<Button>().ToArray();
        var primary = Assert.Single(buttons, button => button.IsDefault);
        var cancel = Assert.Single(buttons, button => button.IsCancel);
        Assert.Same(primary.Parent, cancel.Parent);
        Assert.Contains("dialogActions", Assert.IsType<StackPanel>(primary.Parent).Classes);
        Assert.True(primary.Bounds.Width > 0 && cancel.Bounds.Width > 0,
            $"{dialog}: the actions were not laid out.");

        var primaryRight = primary.TranslatePoint(new Point(primary.Bounds.Width, 0), window)!.Value;
        var cancelLeft = cancel.TranslatePoint(default, window)!.Value;
        double gap = cancelLeft.X - primaryRight.X;
        Assert.True(Math.Abs(gap - expected) <= 0.5,
            $"{dialog}: the action/Cancel gap is {gap:F2}px; the shared theme requires {expected:F2}px.");
        Assert.True(Math.Abs(primaryRight.Y - cancelLeft.Y) <= 0.5,
            $"{dialog}: the actions are not on the same row.");
    }
}
