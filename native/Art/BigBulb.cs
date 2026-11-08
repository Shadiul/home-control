using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace HomeControl.Art;

/// <summary>
/// The Light page's bulb: glass tinted with the light's colour, an LED filament on two wires, a collar and a
/// threaded metal base. It glows in its colour; brightness sets the size and strength of the glow.
/// Drawn on a 120 × 177 canvas.
/// </summary>
public sealed partial class BigBulb : Canvas
{
    const string FilamentData = "M53 88c0-14-7-22-4-34M67 88c0-14 7-22 4-34M49 54c3.5-6 18.5-6 22 0";

    readonly Ellipse halo = new();
    readonly Path glass;
    readonly Path wires, filament, core;

    public BigBulb()
    {
        Width = 120;
        Height = 177;
        Children.Add(halo);
        glass = Draw.Shape("M60 6C30.5 6 12 28.5 12 55c0 19 10.5 31 20.5 42.5C38.5 104.5 41 111 41 120h38c0-9 2.5-15.5 8.5-22.5C97.5 86 108 74 108 55 108 28.5 89.5 6 60 6z");
        Children.Add(glass);
        wires = Draw.Shape("M53 120V88M67 120V88", null, null, 1.2, round: true);
        filament = Draw.Shape(FilamentData, null, null, 2.6, round: true);
        core = Draw.Shape(FilamentData, null, new SolidColorBrush(Paint.Hex(0xFFFFFF)), 1, round: true);
        Children.Add(wires);
        Children.Add(filament);
        Children.Add(core);

        // highlights on the glass
        Children.Add(Highlight(38, 38, 8, 17, -28, 0.55));
        Children.Add(Highlight(86, 70, 3, 8, 20, 0.25));

        // collar and threaded base
        Children.Add(Draw.At(new Rectangle
        {
            Width = 41, Height = 9, RadiusX = 2.5, RadiusY = 2.5,
            Fill = Draw.Gradient(new(0, 0), new(1, 0), (0, Paint.Hex(0xD5D5D5)), (0.4, Paint.Hex(0xFAFAFA)), (1, Paint.Hex(0xC2C2C2))),
        }, 39.5, 118));
        var metal = Draw.Gradient(new(0, 0), new(1, 0), (0, Paint.Hex(0x7E7E80)), (0.35, Paint.Hex(0xE9E9EA)), (0.6, Paint.Hex(0xB9B9BB)), (1, Paint.Hex(0x6E6E70)));
        Children.Add(Draw.At(new Rectangle { Width = 38, Height = 8, RadiusX = 4, RadiusY = 4, Fill = metal }, 41, 128));
        Children.Add(Draw.At(new Rectangle { Width = 37, Height = 8, RadiusX = 4, RadiusY = 4, Fill = metal }, 41.5, 137));
        Children.Add(Draw.At(new Rectangle { Width = 36, Height = 8, RadiusX = 4, RadiusY = 4, Fill = metal }, 42, 146));
        Children.Add(Draw.Shape("M44 155h32l-4 8.5c-.6 1.3-1.9 2-3.3 2H51.3c-1.4 0-2.7-.7-3.3-2z", new SolidColorBrush(Paint.Hex(0x4A4A4C))));
        Children.Add(Draw.At(new Rectangle { Width = 10, Height = 5, RadiusX = 2, RadiusY = 2, Fill = new SolidColorBrush(Paint.Hex(0x9A9A9C)) }, 55, 165));
        Show(false, Paint.Hex(0xFFC47E), 0);
    }

    static Ellipse Highlight(double cx, double cy, double rx, double ry, double angle, double opacity) =>
        Draw.At(new Ellipse
        {
            Width = rx * 2, Height = ry * 2, Fill = new SolidColorBrush(Paint.Hex(0xFFFFFF)), Opacity = opacity,
            RenderTransformOrigin = new(0.5, 0.5), RenderTransform = new RotateTransform { Angle = angle },
        }, cx - rx, cy - ry);

    static RadialGradientBrush Glass(Color a, Color b, Color c) => Draw.Radial(new(0.42, 0.36), 0.7, 0.7, (0, a), (0.5, b), (1, c));

    /// <summary>colour: the light's colour; level 0-1: its brightness.</summary>
    public void Show(bool on, Color colour, double level)
    {
        var white = Paint.Hex(0xFFFFFF);
        if (on)
        {
            level = Math.Clamp(level, 0, 1);
            double r = 52 + 18 * level;
            halo.Width = halo.Height = r * 2;
            Draw.At(halo, 60 - r, 56 - r);
            halo.Fill = Draw.Radial((0, Paint.WithAlpha(colour, 0.45 + 0.5 * level)), (0.45, Paint.WithAlpha(colour, 0.15 + 0.3 * level)),
                                    (1, Paint.WithAlpha(colour, 0)));
            glass.Fill = Glass(white, Paint.Mix(colour, white, 0.55), colour);
            glass.Stroke = new SolidColorBrush(Paint.Mix(colour, white, 0.3));
            wires.Stroke = new SolidColorBrush(Paint.Hex(0xE9D9B8));
            filament.Stroke = new SolidColorBrush(Paint.Mix(colour, Paint.Hex(0xFFF4D6), 0.6));
            core.Opacity = 0.9;
        }
        else
        {
            halo.Fill = null;
            glass.Fill = Glass(white, Paint.Hex(0xECECEC), Paint.Hex(0xC9C9C9));
            glass.Stroke = new SolidColorBrush(Paint.Hex(0xB5B5B5));
            wires.Stroke = new SolidColorBrush(Paint.Hex(0xA8A8A8));
            filament.Stroke = new SolidColorBrush(Paint.Hex(0xB0A58F));
            core.Opacity = 0;
        }
    }
}
