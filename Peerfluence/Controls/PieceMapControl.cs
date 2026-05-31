using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace Peerfluence.Controls;

public sealed class PieceMapControl : Control
{
    public static readonly DirectProperty<PieceMapControl, byte[]?> BitfieldProperty =
        AvaloniaProperty.RegisterDirect<PieceMapControl, byte[]?>(
            nameof(Bitfield),
            o => o.Bitfield,
            (o, v) => o.Bitfield = v);

    private byte[]? _bitfield;
    public byte[]? Bitfield
    {
        get => _bitfield;
        set
        {
            if (SetAndRaise(BitfieldProperty, ref _bitfield, value))
            {
                InvalidateVisual();
            }
        }
    }

    public static readonly DirectProperty<PieceMapControl, int[]?> AvailabilityProperty =
        AvaloniaProperty.RegisterDirect<PieceMapControl, int[]?>(
            nameof(Availability),
            o => o.Availability,
            (o, v) => o.Availability = v);

    private int[]? _availability;
    public int[]? Availability
    {
        get => _availability;
        set
        {
            if (SetAndRaise(AvailabilityProperty, ref _availability, value))
            {
                InvalidateVisual();
            }
        }
    }

    public static readonly DirectProperty<PieceMapControl, int> PieceCountProperty =
        AvaloniaProperty.RegisterDirect<PieceMapControl, int>(
            nameof(PieceCount),
            o => o.PieceCount,
            (o, v) => o.PieceCount = v);

    private int _pieceCount;
    public int PieceCount
    {
        get => _pieceCount;
        set
        {
            if (SetAndRaise(PieceCountProperty, ref _pieceCount, value))
            {
                InvalidateVisual();
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        context.Custom(new PieceMapCustomDrawOperation(Bounds, Bitfield, Availability, PieceCount));
    }

    private sealed class PieceMapCustomDrawOperation : ICustomDrawOperation
    {
        private readonly byte[]? _bitfield;
        private readonly int[]? _availability;
        private readonly int _pieceCount;

        public PieceMapCustomDrawOperation(Rect bounds, byte[]? bitfield, int[]? availability, int pieceCount)
        {
            Bounds = bounds;
            _bitfield = bitfield;
            _availability = availability;
            _pieceCount = pieceCount;
        }

        public Rect Bounds { get; }

        public void Dispose() { }

        public bool Equals(ICustomDrawOperation? other) => false;

        public bool HitTest(Point p) => false;

        public void Render(ImmediateDrawingContext context)
        {
            var lease = (ISkiaSharpApiLeaseFeature?)context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature));
            if (lease == null) return;

            using var skia = lease.Lease();
            var canvas = skia.SkCanvas;

            if (_pieceCount <= 0) return;

            int cols = (int)Math.Max(1, Math.Sqrt(_pieceCount * (Bounds.Width / Bounds.Height)));
            int rows = (int)Math.Ceiling((double)_pieceCount / cols);

            float w = (float)Bounds.Width / cols;
            float h = (float)Bounds.Height / rows;

            using var paint = new SKPaint { Style = SKPaintStyle.Fill };

            for (int i = 0; i < _pieceCount; i++)
            {
                int r = i / cols;
                int c = i % cols;

                bool hasPiece = false;
                if (_bitfield != null && i / 8 < _bitfield.Length)
                {
                    hasPiece = (_bitfield[i / 8] & (1 << (7 - (i % 8)))) != 0;
                }

                int avail = 0;
                if (_availability != null && i < _availability.Length)
                {
                    avail = _availability[i];
                }

                if (hasPiece)
                {
                    paint.Color = SKColors.Green;
                }
                else if (avail > 5)
                {
                    paint.Color = SKColors.Orange;
                }
                else if (avail > 0)
                {
                    paint.Color = SKColors.LightBlue;
                }
                else
                {
                    paint.Color = SKColors.Gray;
                }

                canvas.DrawRect(new SKRect(c * w, r * h, (c + 1) * w - 1, (r + 1) * h - 1), paint);
            }
        }
    }
}
