using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace LedControl.Art;

/// <summary>
/// A speaker cone seen from the front, ringed by bars that follow the music's spectrum while music sync runs
/// (bass at the top, mirrored left and right). Drawn on a 352 × 196 canvas.
/// </summary>
public sealed partial class SpeakerCone : Canvas
{
    const int Rays = 48;
    const double Cx = 176, Cy = 98, R0 = 70;
    static readonly uint[] RayColours = [0x8A5BE0, 0xB48CFF, 0x5CB8FF];

    readonly Line[] rays = new Line[Rays];
    readonly SolidColorBrush[] colours = RayColours.Select(c => Paint.Brush(c)).ToArray();
    readonly SolidColorBrush idle = Paint.Brush(0x4A4D53);
    readonly ScaleTransform pump = new() { CenterX = Cx, CenterY = Cy };
    bool running;

    public SpeakerCone()
    {
        Width = 352;
        Height = 196;
        for (int k = 0; k < Rays; k++)
        {
            rays[k] = new Line { StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            Children.Add(rays[k]);
        }
        var cone = new Canvas { RenderTransform = pump };
        cone.Children.Add(Circle(62, Paint.Brush(0x1B1D21), Paint.Brush(0x3A3E45), 3));
        cone.Children.Add(Circle(54, null, Paint.Brush(0x2C3036), 5));
        cone.Children.Add(Circle(48, Draw.Radial(new(0.45, 0.4), 0.65, 0.65,
            (0, Paint.Hex(0x4A4F57)), (0.7, Paint.Hex(0x23262B)), (1, Paint.Hex(0x121417))), null, 0));
        cone.Children.Add(Circle(16, Draw.Radial(new(0.4, 0.35), 0.7, 0.7, (0, Paint.Hex(0x8A8F98)), (1, Paint.Hex(0x2E3238))), null, 0));
        cone.Children.Add(Draw.At(new Ellipse { Width = 8, Height = 8, Fill = Paint.Brush(0xFFFFFF, 0x40) }, 166, 88));
        Children.Add(cone);
        Show(false);
    }

    static Ellipse Circle(double r, Brush? fill, Brush? stroke, double width) =>
        Draw.At(new Ellipse { Width = r * 2, Height = r * 2, Fill = fill, Stroke = stroke, StrokeThickness = width }, Cx - r, Cy - r);

    public void Show(bool running)
    {
        this.running = running;
        if (!running) Pulse(null, 0);
    }

    /// <summary>bars 0-1 from the analyser (low to high); bass 0-1 pushes the cone out a little.</summary>
    public void Pulse(float[]? bars, double bass)
    {
        for (int k = 0; k < Rays; k++)
        {
            double len = 3;
            if (running && bars is { Length: > 0 })
            {
                int half = Rays / 2, i = k < half ? k : Rays - 1 - k;
                len = 4 + 18 * Math.Clamp(bars[Math.Min(bars.Length - 1, i * bars.Length / half)], 0, 1);
            }
            double a = (double)k / Rays * Math.PI * 2 - Math.PI / 2;
            rays[k].X1 = Cx + R0 * Math.Cos(a);
            rays[k].Y1 = Cy + R0 * Math.Sin(a);
            rays[k].X2 = Cx + (R0 + len) * Math.Cos(a);
            rays[k].Y2 = Cy + (R0 + len) * Math.Sin(a);
            rays[k].Stroke = running ? colours[k % colours.Length] : idle;
        }
        pump.ScaleX = pump.ScaleY = 1 + 0.05 * Math.Clamp(running ? bass : 0, 0, 1);
    }
}
