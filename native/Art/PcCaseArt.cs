using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace LedControl.Art;

/// <summary>
/// The PC, drawn like a glass-sided case with a white base: two front fan rings (NZXT), a rear fan ring and the
/// cooler's pump head (Corsair), and an LED strip along the top inside. Everything lit glows in the PC lighting's
/// colour; brightness sets how strong the glow is. Drawn on a 240 × 220 canvas.
/// </summary>
public sealed partial class PcCaseArt : Canvas
{
    readonly Ellipse frontGlow1, frontGlow2, rearGlow, pumpGlow, insideGlow;
    readonly Ellipse frontRing1, frontRing2, rearRing;
    readonly Path pumpRing, strip, stripGlow;

    public PcCaseArt()
    {
        Width = 240;
        Height = 220;
        var dark = new SolidColorBrush(Paint.Hex(0x15161A));
        var frame = new SolidColorBrush(Paint.Hex(0x0C0C0E));

        // Case: tinted glass box on a white base.
        Children.Add(Draw.Shape("M8 6h224a4 4 0 0 1 4 4v158H4V10a4 4 0 0 1 4-4z", frame));
        Children.Add(Draw.Shape("M10 12h146v152H10z", dark));                 // side glass
        Children.Add(Draw.Shape("M162 12h68v152h-68z", dark));                // front glass
        Children.Add(Draw.Shape("M4 168h232v40a6 6 0 0 1-6 6H10a6 6 0 0 1-6-6z",
            Draw.Vertical((0, Paint.Hex(0xF4F4F4)), (1, Paint.Hex(0xD9D9D9)))));
        Children.Add(Draw.Shape("M18 174h120v2H18z", new SolidColorBrush(Paint.Hex(0xC9C9C9))));

        // Light pooling inside the side window.
        insideGlow = Glowing(83, 88, 80);
        Children.Add(insideGlow);

        // Inside: motherboard tray, GPU and cooler tubes.
        Children.Add(Draw.Shape("M20 20h126v134H20z", new SolidColorBrush(Paint.Hex(0x1C1D22))));
        Children.Add(Draw.Shape("M24 112h116v16a3 3 0 0 1-3 3H27a3 3 0 0 1-3-3z", new SolidColorBrush(Paint.Hex(0x2A2C32))));
        Children.Add(Draw.Shape("M30 118h70", null, new SolidColorBrush(Paint.Hex(0x3A3D44)), 2, round: true));
        Children.Add(Draw.Shape("M104 72c18 0 30 8 40-30M104 80c22 0 36 6 44-32", null, new SolidColorBrush(Paint.Hex(0x2E3036)), 3, round: true));

        // LED strip along the top inside.
        stripGlow = Draw.Shape("M22 18h122", null, null, 9, round: true);
        strip = Draw.Shape("M22 18h122", null, null, 2.5, round: true);
        Children.Add(stripGlow);
        Children.Add(strip);

        // Rear fan (Corsair) and the pump head (Corsair).
        rearGlow = Glowing(48, 58, 34);
        Children.Add(rearGlow);
        Children.Add(Draw.At(new Ellipse { Width = 48, Height = 48, Fill = new SolidColorBrush(Paint.Hex(0x101114)) }, 24, 34));
        rearRing = Ring(48, 58, 22, 3.5);
        Children.Add(rearRing);
        Children.Add(Draw.At(new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(Paint.Hex(0x2A2C32)) }, 42, 52));

        pumpGlow = Glowing(96, 76, 26);
        Children.Add(pumpGlow);
        Children.Add(Draw.Shape("M84 64h24a4 4 0 0 1 4 4v16a4 4 0 0 1-4 4H84a4 4 0 0 1-4-4V68a4 4 0 0 1 4-4z", new SolidColorBrush(Paint.Hex(0x101114))));
        pumpRing = Draw.Shape("M86 67h20a3 3 0 0 1 3 3v12a3 3 0 0 1-3 3H86a3 3 0 0 1-3-3V70a3 3 0 0 1 3-3z", null, null, 2, round: true);
        Children.Add(pumpRing);

        // Front fans (NZXT).
        frontGlow1 = Glowing(196, 52, 46);
        frontGlow2 = Glowing(196, 124, 46);
        Children.Add(frontGlow1);
        Children.Add(frontGlow2);
        frontRing1 = Ring(196, 52, 28, 5);
        frontRing2 = Ring(196, 124, 28, 5);
        Children.Add(frontRing1);
        Children.Add(frontRing2);
        foreach (var cy in new[] { 52, 124 })
            Children.Add(Draw.At(new Ellipse { Width = 10, Height = 10, Fill = new SolidColorBrush(Paint.Hex(0x22242A)) }, 191, cy - 5));

        // Glass reflections.
        Children.Add(Draw.Shape("M14 16l40 0-34 60z", new SolidColorBrush(Paint.Hex(0xFFFFFF, 0x0D))));
        Children.Add(Draw.Shape("M166 16h22l-22 40z", new SolidColorBrush(Paint.Hex(0xFFFFFF, 0x0D))));
        Show(false, Paint.Hex(0xFFC47E), 0);
    }

    static Ellipse Glowing(double cx, double cy, double r) =>
        Draw.At(new Ellipse { Width = r * 2, Height = r * 2, IsHitTestVisible = false }, cx - r, cy - r);

    static Ellipse Ring(double cx, double cy, double r, double width) =>
        Draw.At(new Ellipse { Width = r * 2, Height = r * 2, StrokeThickness = width }, cx - r, cy - r);

    static RadialGradientBrush Soft(Color c, double alpha) =>
        Draw.Radial((0, Paint.WithAlpha(c, alpha)), (0.5, Paint.WithAlpha(c, alpha * 0.45)), (1, Paint.WithAlpha(c, 0)));

    /// <summary>colour: the PC lighting's colour; level 0-1: its brightness.</summary>
    public void Show(bool on, Color colour, double level)
    {
        level = Math.Clamp(level, 0, 1);
        var off = new SolidColorBrush(Paint.Hex(0x3A3C41));
        if (!on || level <= 0)
        {
            foreach (var g in new[] { frontGlow1, frontGlow2, rearGlow, pumpGlow, insideGlow }) g.Fill = null;
            frontRing1.Stroke = frontRing2.Stroke = rearRing.Stroke = pumpRing.Stroke = strip.Stroke = off;
            stripGlow.Stroke = null;
            return;
        }
        // Dim colours stay visible: blend towards the colour's full strength rather than black.
        var lit = Paint.Mix(Paint.Mix(colour, Paint.Hex(0x3A3C41), 0.65), colour, 0.35 + 0.65 * level);
        var ring = new SolidColorBrush(Paint.Mix(lit, Paint.Hex(0xFFFFFF), 0.25));
        frontRing1.Stroke = frontRing2.Stroke = rearRing.Stroke = pumpRing.Stroke = strip.Stroke = ring;
        double a = 0.25 + 0.5 * level;
        frontGlow1.Fill = Soft(colour, a);
        frontGlow2.Fill = Soft(colour, a);
        rearGlow.Fill = Soft(colour, a);
        pumpGlow.Fill = Soft(colour, a * 0.9);
        insideGlow.Fill = Soft(colour, 0.1 + 0.25 * level);
        stripGlow.Stroke = new SolidColorBrush(Paint.WithAlpha(colour, 0.25 + 0.3 * level));
    }
}
