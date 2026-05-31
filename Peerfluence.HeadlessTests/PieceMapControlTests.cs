using Avalonia.Controls;
using Peerfluence.Controls;
using Peerfluence.HeadlessTests.XUnit;

namespace Peerfluence.HeadlessTests;

public class PieceMapControlTests
{
    [AvaloniaFact]
    public void CanCreate()
    {
        var control = new PieceMapControl();
        Assert.NotNull(control);
    }

    [AvaloniaFact]
    public void Properties_DefaultToNull()
    {
        var control = new PieceMapControl();
        Assert.Null(control.Bitfield);
        Assert.Null(control.Availability);
        Assert.Equal(0, control.PieceCount);
    }

    [AvaloniaFact]
    public void Properties_CanBeSet()
    {
        var control = new PieceMapControl();
        var bitfield = new byte[] { 0xFF, 0x00 };
        var availability = new int[] { 3, 0, 1, 5 };

        control.Bitfield = bitfield;
        control.Availability = availability;
        control.PieceCount = 16;

        Assert.Same(bitfield, control.Bitfield);
        Assert.Same(availability, control.Availability);
        Assert.Equal(16, control.PieceCount);
    }

    [AvaloniaFact]
    public void RenderWithData_DoesNotThrow()
    {
        var control = new PieceMapControl
        {
            Bitfield = new byte[] { 0xFF, 0x0F },
            Availability = new int[] { 3, 2, 1, 0, 5, 4, 3, 2, 1, 0, 5, 4, 3, 2, 1, 0 },
            PieceCount = 16,
            Width = 200,
            Height = 50
        };

        var window = new Window { Content = control };
        window.Show();

        // If we get here without exception, rendering succeeded
        Assert.True(true);
    }

    [AvaloniaFact]
    public void RenderWithNullBitfield_DoesNotThrow()
    {
        var control = new PieceMapControl
        {
            PieceCount = 10,
            Width = 200,
            Height = 50
        };

        var window = new Window { Content = control };
        window.Show();

        Assert.True(true);
    }

    [AvaloniaFact]
    public void RenderWithZeroPieceCount_DoesNotThrow()
    {
        var control = new PieceMapControl
        {
            Bitfield = new byte[] { 0xFF },
            PieceCount = 0,
            Width = 200,
            Height = 50
        };

        var window = new Window { Content = control };
        window.Show();

        Assert.True(true);
    }
}
