using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;

namespace LedControl.Art;

/// <summary>
/// A speaker cone seen from the front, sitting on a soft violet glow that breathes while music sync runs
/// (and dims when it stops). Drawn on a 352 × 196 canvas.
/// </summary>
public sealed partial class SpeakerCone : Canvas
{
    const double Cx = 176, Cy = 98;

    readonly Ellipse glow;
    readonly ScaleTransform breath = new() { CenterX = 118, CenterY = 118 };
    readonly Storyboard breathing = new() { RepeatBehavior = RepeatBehavior.Forever };
    bool running;

    public SpeakerCone()
    {
        Width = 352;
        Height = 196;
        // A blurred violet disc (radius about 78), drawn as a radial gradient that fades out past its edge.
        var violet = Paint.Hex(0x8A5BE0);
        glow = new Ellipse
        {
            Width = 236, Height = 236, RenderTransform = breath,
            Fill = Draw.Radial((0, violet), (0.5, violet), (0.66, Paint.WithAlpha(violet, 0.5)), (1, Paint.WithAlpha(violet, 0))),
        };
        Children.Add(Draw.At(glow, Cx - 118, Cy - 118));
        Children.Add(Circle(62, Paint.Brush(0x1B1D21), Paint.Brush(0x3A3E45), 3));
        Children.Add(Circle(54, null, Paint.Brush(0x2C3036), 5));
        Children.Add(Circle(48, Draw.Radial(new(0.45, 0.4), 0.65, 0.65,
            (0, Paint.Hex(0x4A4F57)), (0.7, Paint.Hex(0x23262B)), (1, Paint.Hex(0x121417))), null, 0));
        Children.Add(Circle(16, Draw.Radial(new(0.4, 0.35), 0.7, 0.7, (0, Paint.Hex(0x8A8F98)), (1, Paint.Hex(0x2E3238))), null, 0));
        Children.Add(Draw.At(new Ellipse { Width = 8, Height = 8, Fill = Paint.Brush(0xFFFFFF, 0x40) }, 166, 88));

        // Breathing: the glow swells from about 70 to 86 and back every 2.6 seconds.
        foreach (var axis in new[] { "ScaleX", "ScaleY" })
        {
            var a = new DoubleAnimation
            {
                From = 0.9, To = 1.1, Duration = new Duration(TimeSpan.FromSeconds(1.3)), AutoReverse = true,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(a, breath);
            Storyboard.SetTargetProperty(a, axis);
            breathing.Children.Add(a);
        }
        Unloaded += (_, _) => breathing.Stop();
        Loaded += (_, _) =>
        {
            if (running) breathing.Begin();
        };
        Show(false);
    }

    static Ellipse Circle(double r, Brush? fill, Brush? stroke, double width) =>
        Draw.At(new Ellipse { Width = r * 2, Height = r * 2, Fill = fill, Stroke = stroke, StrokeThickness = width }, Cx - r, Cy - r);

    public void Show(bool running)
    {
        glow.Opacity = running ? 0.65 : 0.15;
        if (running == this.running) return;
        this.running = running;
        if (running) breathing.Begin();
        else
        {
            breathing.Stop();
            breath.ScaleX = breath.ScaleY = 1;
        }
    }
}
