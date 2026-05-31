using Avalonia;
using Avalonia.Headless;
using Material.Icons.Avalonia;
using Peerfluence.Converters;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Services;
using SukiUI;

[assembly: AvaloniaTestApplication(typeof(Peerfluence.HeadlessTests.TestAppBuilder))]
[assembly: AvaloniaTestFramework]

namespace Peerfluence.HeadlessTests;

public class TestApp : Application
{
    public override void Initialize()
    {
        // Initialize localization singleton (required by {m:L} markup extension)
        _ = new LocalizationService();

        Styles.Add(new SukiTheme());
        Styles.Add(new MaterialIconStyles(null));

        Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://Peerfluence/")) { Source = new Uri("avares://Peerfluence/Resources/_Colors.axaml") });
        Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://Peerfluence/")) { Source = new Uri("avares://Peerfluence/Resources/_Thickness.axaml") });
        Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://Peerfluence/")) { Source = new Uri("avares://Peerfluence/Resources/_Spacings.axaml") });
        Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://Peerfluence/")) { Source = new Uri("avares://Peerfluence/Resources/_FontSizes.axaml") });
        Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://Peerfluence/")) { Source = new Uri("avares://Peerfluence/Resources/_IconSizes.axaml") });
        Resources.MergedDictionaries.Add(new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://Peerfluence/")) { Source = new Uri("avares://Peerfluence/Resources/_CornerRadii.axaml") });

        Resources["ByteSizeConverter"] = new ByteSizeConverter();
        Resources["SpeedConverter"] = new SpeedConverter();
        Resources["NullToBoolConverter"] = new NullToBoolConverter();

        DataTemplates.Add(new ViewLocator());
    }
}

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions())
            .WithInterFont()
;
}
