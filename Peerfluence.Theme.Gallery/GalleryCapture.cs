using System.Globalization;
using System.Net;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Peerfluence.Theme.Gallery;

internal static class GalleryCapture
{
    public static void Write(string directory, bool allAccents)
    {
        Directory.CreateDirectory(directory);
        var files = new List<string>();
        IEnumerable<string> accents = allAccents ? PeerfluenceTheme.Accents : [PeerfluenceTheme.DefaultAccent];
        foreach (string accent in accents)
        {
            foreach (bool dark in new[] { false, true })
            {
                var window = new GalleryWindow();
                window.SetPalette(accent, dark);
                window.Show();
                try
                {
                    string prefix = $"{(dark ? "dark" : "light")}-{accent.ToLowerInvariant()}";
                    Capture(window, directory, prefix + "-overview", files);
                    foreach (var (name, control) in window.Targets)
                    {
                        // Real input exercises Fluent's state machinery. No pseudo-classes are forced.
                        window.MouseMove(new Point(1170, 970));
                        window.ResetFocus();
                        var target = control is Expander
                            ? control.GetVisualDescendants().OfType<ToggleButton>().First()
                            : control is NumericUpDown
                                ? control.GetVisualDescendants().OfType<TextBox>().Single()
                                : control;
                        var point = target.TranslatePoint(new Point(Math.Min(10, target.Bounds.Width / 2), target.Bounds.Height / 2), window)!.Value;
                        window.MouseMove(point);
                        if (!target.IsPointerOver) throw new InvalidOperationException($"Pointer did not reach {name}.");
                        Capture(window, directory, $"{prefix}-{name}-hover", files);
                        window.MouseDown(point, MouseButton.Left);
                        Capture(window, directory, $"{prefix}-{name}-pressed", files);
                        // Release outside to avoid changing toggle/radio selection or activating buttons.
                        window.MouseMove(new Point(1170, 970));
                        window.MouseUp(new Point(1170, 970), MouseButton.Left);
                        if (control is ComboBox field) field.IsDropDownOpen = false;
                        Dispatcher.UIThread.RunJobs();
                        window.ResetFocus();
                        if (!target.Focus(NavigationMethod.Tab)) throw new InvalidOperationException($"Could not focus {name}.");
                        Capture(window, directory, $"{prefix}-{name}-focus", files);
                    }

                    var combo = (ComboBox)window.Targets["combo"];
                    combo.IsDropDownOpen = true;
                    Capture(window, directory, prefix + "-dropdown", files);
                    combo.IsDropDownOpen = false;
                    var menu = window.Targets["standard"].ContextMenu!;
                    var menuPoint = window.Targets["standard"].TranslatePoint(new Point(10, 10), window)!.Value;
                    window.MouseMove(menuPoint);
                    menu.Open(window.Targets["standard"]);
                    Capture(window, directory, prefix + "-context-menu", files);
                    menu.Close();
                    var disclosure = (Expander)window.Targets["disclosure"];
                    disclosure.IsExpanded = false;
                    Capture(window, directory, prefix + "-disclosure-collapsed", files);
                    disclosure.IsExpanded = true;
                    window.ShowToasts();
                    Capture(window, directory, prefix + "-notifications", files);
                    window.ShowDialog();
                    Capture(window, directory, prefix + "-dialog", files);
                }
                finally { window.Close(); }
            }
        }

        var html = new StringBuilder("<!doctype html><html lang=\"en\"><meta charset=\"utf-8\"><title>Peerfluence control gallery</title><style>body{font:15px system-ui;background:#f6f7f9;color:#15181d;margin:24px}nav{display:flex;flex-wrap:wrap;gap:12px}section{margin-top:32px}img{max-width:100%;border:1px solid #c8cdd6}a{color:#0f6d69}</style><h1>Peerfluence control gallery</h1><p>Actual pointer and keyboard states. Compare captures on the same OS and font stack.</p><details><summary>Browse the image index</summary><nav>");
        foreach (string file in files) html.Append(CultureInfo.InvariantCulture, $"<a href=\"#{WebUtility.HtmlEncode(file)}\">{WebUtility.HtmlEncode(file)}</a>");
        html.Append("</nav></details>");
        foreach (string file in files) html.Append(CultureInfo.InvariantCulture, $"<section id=\"{WebUtility.HtmlEncode(file)}\"><h2>{WebUtility.HtmlEncode(file)}</h2><img loading=\"lazy\" src=\"{WebUtility.HtmlEncode(file)}.png\" alt=\"{WebUtility.HtmlEncode(file)}\"></section>");
        html.Append("</html>");
        File.WriteAllText(Path.Combine(directory, "index.html"), html.ToString());
        Console.WriteLine($"Captured {files.Count} images in {Path.GetFullPath(directory)}");
    }

    private static void Capture(Window window, string directory, string name, List<string> files)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException($"No rendered frame for {name}.");
        frame.Save(Path.Combine(directory, name + ".png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        files.Add(name);
    }
}
