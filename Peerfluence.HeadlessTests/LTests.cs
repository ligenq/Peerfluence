using Avalonia.Controls;
using Avalonia.Data;
using Peerfluence.HeadlessTests.XUnit;
using Peerfluence.Markup;
using Peerfluence.Services;

namespace Peerfluence.HeadlessTests;

/// <summary>
/// The <c>{m:L Key}</c> markup extension every label in the application is written with.
/// </summary>
/// <remarks>
/// <para>
/// This is the mechanism by which the interface changes language without being rebuilt. It is not
/// required by the architecture test - <c>ProvideValue</c> is an override the XAML runtime calls,
/// so no call graph rooted in the tests can reach it - but it is worth pinning down anyway, because
/// nothing else answers the question it decides. <c>LocalizationTests</c> checks that the strings
/// exist in every resx; this is what makes a running window pick a new one up.
/// </para>
/// <para>
/// Both halves can fail quietly. Without the push on subscribe every label is blank until the
/// language is changed once; without the push on change, switching language does nothing until the
/// window is reopened.
/// </para>
/// </remarks>
public class LTests
{
    [AvaloniaFact]
    public void ABoundLabel_ShowsItsStringImmediately()
    {
        var localization = new LocalizationService();
        localization.Apply("en-US");

        var block = Bind("App_Title");

        Assert.Equal(LocalizationService.GetString("App_Title"), block.Text);
        Assert.False(string.IsNullOrWhiteSpace(block.Text));
    }

    [AvaloniaFact]
    public void ChangingLanguage_ChangesWhatABoundLabelShows()
    {
        var localization = new LocalizationService();
        localization.Apply("en-US");

        var block = Bind("Nav_Downloads");
        var english = block.Text;

        try
        {
            localization.Apply("sv-SE");

            Assert.Equal(LocalizationService.GetString("Nav_Downloads"), block.Text);
            Assert.NotEqual(english, block.Text);
        }
        finally
        {
            localization.Apply("en-US");
        }
    }

    [AvaloniaFact]
    public void AnEmptyKey_BindsToNothingRatherThanFailing()
    {
        // A blank key is a mistake in the XAML, and the label it belongs to should come up empty
        // rather than taking the window down with it.
        var value = new L(string.Empty).ProvideValue(null!);

        Assert.Equal(string.Empty, value);
    }

    [AvaloniaFact]
    public void AKeyNobodyWrote_ShowsTheKeyItself()
    {
        var block = Bind("NoSuchKey_AtAll");

        Assert.Equal("NoSuchKey_AtAll", block.Text);
    }

    private static TextBlock Bind(string key)
    {
        var binding = Assert.IsAssignableFrom<BindingBase>(new L(key).ProvideValue(null!));

        var block = new TextBlock();
        block.Bind(TextBlock.TextProperty, binding);

        var window = new Window { Content = block };
        window.Show();

        return block;
    }
}
