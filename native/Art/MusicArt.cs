using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace HomeControl.Art;

/// <summary>Soft shapes for the music art: blurred discs drawn as radial gradients, and breathing animations.</summary>
static class Soft
{
    /// <summary>A disc of radius r blurred by about <paramref name="blur"/>: solid inside, fading out past its edge.</summary>
    public static Ellipse Disc(double cx, double cy, double r, double blur, Color c, double alpha = 1)
    {
        double R = r + 2 * blur, inner = Math.Max(0, (r - blur) / R);
        var e = new Ellipse
        {
            Width = 2 * R, Height = 2 * R, IsHitTestVisible = false,
            Fill = Draw.Radial((0, Paint.WithAlpha(c, alpha)), (inner, Paint.WithAlpha(c, alpha)), (r / R, Paint.WithAlpha(c, alpha * 0.5)), (1, Paint.WithAlpha(c, 0))),
        };
        return Draw.At(e, cx - R, cy - R);
    }

    public static Ellipse Circle(double cx, double cy, double r, Brush? fill, Brush? stroke = null, double width = 0)
    {
        var e = new Ellipse { Width = 2 * r, Height = 2 * r, Fill = fill, Stroke = stroke, StrokeThickness = width, IsHitTestVisible = false };
        return Draw.At(e, cx - r, cy - r);
    }

    /// <summary>A glossy orb: a white highlight at the top left, the colour, and a deeper edge.</summary>
    public static RadialGradientBrush Orb(Color light, Color deep, double lightAt = 0.32, Color? top = null) =>
        Draw.Radial(new(0.36, 0.3), 0.8, 0.8, (0, top ?? Paint.Hex(0xFFFFFF)), (lightAt, light), (1, deep));

    /// <summary>Scales an element about its centre between two sizes and back, forever.</summary>
    public static void Breathe(Storyboard sb, FrameworkElement e, double from, double to, double seconds, double begin = 0)
    {
        var scale = new ScaleTransform();
        e.RenderTransformOrigin = new Point(0.5, 0.5);
        e.RenderTransform = scale;
        foreach (var axis in new[] { "ScaleX", "ScaleY" })
        {
            var a = new DoubleAnimation
            {
                From = from, To = to, Duration = new Duration(TimeSpan.FromSeconds(seconds / 2)), AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(begin),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            };
            Storyboard.SetTarget(a, scale);
            Storyboard.SetTargetProperty(a, axis);
            sb.Children.Add(a);
        }
    }

    public static void Animate(Storyboard sb, DependencyObject target, string property, double from, double to, double seconds,
                               bool reverse = true, double begin = 0)
    {
        var a = new DoubleAnimation
        {
            From = from, To = to, Duration = new Duration(TimeSpan.FromSeconds(reverse ? seconds / 2 : seconds)), AutoReverse = reverse,
            RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(begin),
        };
        if (reverse) a.EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, property);
        sb.Children.Add(a);
    }
}

/// <summary>
/// The Music sync stage, kept minimal: one glossy orb in the style's main colour, breathing inside a blurred
/// halo while music sync runs, a single thin ring, and a frosted play / stop button in its middle. When
/// stopped the orb is a quiet grey disc. Drawn on a 300 × 280 canvas.
/// </summary>
public sealed partial class MusicStage : Canvas
{
    const double Cx = 150, Cy = 140;
    readonly Ellipse halo, orb, shine;
    readonly Storyboard breathing = new();
    readonly Path icon;
    bool running;
    Color main;

    public Button PlayButton { get; }

    public MusicStage()
    {
        Width = 300;
        Height = 280;
        halo = Soft.Disc(Cx, Cy, 80, 24, Paint.Hex(0x8A5BE0));
        Children.Add(halo);
        Children.Add(Soft.Circle(Cx, Cy, 96, null, Paint.Brush(0xFFFFFF, 0x1F), 1.5));
        orb = Soft.Circle(Cx, Cy, 58, null);
        Children.Add(orb);
        shine = Soft.Disc(132, 118, 12, 4, Paint.Hex(0xFFFFFF));
        shine.Height *= 0.65;
        Canvas.SetTop(shine, 118 - shine.Height / 2);
        Children.Add(shine);

        // Breathing: the halo swells from about 70 to 92 and the orb from 56 to 62, every 2.6 seconds.
        Soft.Breathe(breathing, halo, 70 / 80.0, 92 / 80.0, 2.6);
        Soft.Breathe(breathing, orb, 56 / 58.0, 62 / 58.0, 2.6);

        icon = new Path { Fill = Paint.Brush(0xFFFFFF), Stretch = Stretch.Uniform, Width = 22, Height = 22 };
        PlayButton = new Button
        {
            Style = (Style)Application.Current.Resources["BareButton"], Width = 64, Height = 64, CornerRadius = new CornerRadius(32),
            Content = new Grid
            {
                Children =
                {
                    Soft.Disc(32, 38, 30, 9, Paint.Hex(0), 0.25),
                    new Border
                    {
                        CornerRadius = new CornerRadius(32), Background = Paint.Brush(0xFFFFFF, 0x2E), BorderBrush = Paint.Brush(0xFFFFFF, 0x59),
                        BorderThickness = new Thickness(1), Child = new Viewbox { Width = 22, Height = 22, Child = icon },
                    },
                },
            },
        };
        ((Ellipse)((Grid)PlayButton.Content).Children[0]).Margin = new Thickness(-16, -12, -16, -20);
        Children.Add(Draw.At(PlayButton, Cx - 32, Cy - 32));

        Loaded += (_, _) =>
        {
            if (running) breathing.Begin();
        };
        Unloaded += (_, _) => breathing.Stop();
        Show(false, Paint.Hex(0x8A5BE0));
    }

    public void Show(bool running, Color main)
    {
        bool changed = running != this.running || main != this.main;
        this.running = running;
        this.main = main;
        icon.Data = Draw.Geo(running ? "M6 5h4v14H6zM14 5h4v14h-4z" : "M7 4.5v15l12.5-7.5z");
        string label = running ? "Stop music sync" : "Start music sync";
        AutomationProperties.SetName(PlayButton, label);
        ToolTipService.SetToolTip(PlayButton, label);
        if (!changed && orb.Fill != null) return;

        halo.Fill = ((Ellipse)Soft.Disc(0, 0, 80, 24, main)).Fill;
        halo.Opacity = running ? 0.55 : 0;
        orb.Fill = running
            ? Soft.Orb(Paint.Mix(main, Paint.Hex(0xFFFFFF), 0.45), Paint.Mix(main, Paint.Hex(0), 0.35), 0.35, Paint.Hex(0xFFFFFF, 0xE6))
            : Paint.Brush(0x3A3C41);
        orb.Opacity = running ? 0.95 : 1;
        shine.Opacity = running ? 0.55 : 0.08;
        if (running && IsLoaded) breathing.Begin();
        else if (!running) breathing.Stop();
    }
}

/// <summary>
/// The small animated illustrations on the Music sync style cards, drawn on a 180 × 96 canvas: soft and
/// rounded, nothing pointed. Pulse: a breathing orb with rings. Spectrum: capsule bars with gradients and a
/// reflection. Beat: an amber-to-pink orb sending out ripples. Party: blurred colour blobs circling a pearl.
/// </summary>
public sealed partial class StyleArt : Canvas
{
    const double Cx = 90, Cy = 46;
    readonly string style;
    readonly Storyboard motion = new();

    public StyleArt(string style, Color pulse)
    {
        this.style = style;
        Width = 180;
        Height = 96;
        IsHitTestVisible = false;
        Build(pulse);
        Loaded += (_, _) => motion.Begin();
        Unloaded += (_, _) => motion.Stop();
    }

    /// <summary>The Pulse card follows the chosen pulse colour.</summary>
    public void SetPulse(Color pulse)
    {
        if (style != "pulse") return;
        motion.Stop();
        motion.Children.Clear();
        Children.Clear();
        Build(pulse);
        if (IsLoaded) motion.Begin();
    }

    void Build(Color pulse)
    {
        var white = Paint.Hex(0xFFFFFF);
        switch (style)
        {
            case "pulse":
            {
                var light = Paint.Mix(pulse, white, 0.45);
                var glow = Soft.Disc(Cx, Cy, 30, 8, pulse, 0.5);
                Soft.Breathe(motion, glow, 24 / 30.0, 34 / 30.0, 2.4);
                var wave = Soft.Circle(Cx, Cy, 36, null, new SolidColorBrush(light), 2);
                Soft.Breathe(motion, wave, 26 / 36.0, 42 / 36.0, 2.4);
                Soft.Animate(motion, wave, "Opacity", 0.7, 0.1, 2.4);
                var orb = Soft.Circle(Cx, Cy, 19, Soft.Orb(light, Paint.Mix(pulse, Paint.Hex(0), 0.35)));
                Soft.Breathe(motion, orb, 1, 20.5 / 19, 2.4);
                Add(glow, wave, Soft.Circle(Cx, Cy, 29, null, new SolidColorBrush(Paint.WithAlpha(light, 0.35)), 1.2), orb, Shine(84, 39, 6, 4, 0.6));
                break;
            }
            case "spectrum":
            {
                int[] heights = [22, 34, 44, 38, 30, 40, 48, 36, 28, 32, 22];
                for (int k = 0; k < heights.Length; k++)
                {
                    double h = heights[k], x = 17 + k * 14;
                    var (top, bottom, glow) = k < 4 ? (0xFFB199u, 0xE8603Cu, 0xE8603Cu) : k < 7 ? (0xA7EBC0u, 0x2F9A5Bu, 0x3DAA67u) : (0xA9CCFFu, 0x3A84E8u, 0x3A84E8u);
                    // a soft glow behind, the capsule bar, and its reflection under the baseline
                    var halo = new Rectangle { Width = 24, Height = h + 16, RadiusX = 12, RadiusY = 12, Opacity = 0.4,
                                               Fill = Draw.Radial((0, Paint.Hex(glow, 0xB0)), (0.6, Paint.Hex(glow, 0x40)), (1, Paint.Hex(glow, 0))) };
                    var bar = new Rectangle { Width = 8, Height = h, RadiusX = 4, RadiusY = 4, Fill = Draw.Vertical((0, Paint.Hex(top)), (1, Paint.Hex(bottom))),
                                              RenderTransformOrigin = new Point(0.5, 1), RenderTransform = new ScaleTransform() };
                    Soft.Animate(motion, bar.RenderTransform, "ScaleY", 1, 0.6, 1 + (k % 4) * 0.22);
                    var mirror = new Rectangle { Width = 8, Height = Math.Round(h * 0.25), RadiusX = 4, RadiusY = 4, Fill = Paint.Brush(glow), Opacity = 0.18 };
                    Add(Draw.At(halo, x - 8, 72 - h - 8), Draw.At(bar, x, 72 - h), Draw.At(mirror, x, 76));
                }
                break;
            }
            case "beat":
            {
                var glow = Soft.Disc(Cx, Cy, 30, 8, Paint.Hex(0xE8A23C), 0.4);
                Add(glow);
                // Ripples: drawn at their full size and grown from small, fading as they go.
                foreach (var (colour, width, begin) in new[] { (0xF5B24Au, 2.5, 0.0), (0xE879B4u, 2.0, 0.6) })
                {
                    var ripple = Soft.Circle(Cx, Cy, 46, null, Paint.Brush(colour), width);
                    var scale = new ScaleTransform();
                    ripple.RenderTransformOrigin = new Point(0.5, 0.5);
                    ripple.RenderTransform = scale;
                    ripple.Opacity = 0;
                    Soft.Animate(motion, scale, "ScaleX", 18 / 46.0, 1, 1.2, reverse: false, begin: begin);
                    Soft.Animate(motion, scale, "ScaleY", 18 / 46.0, 1, 1.2, reverse: false, begin: begin);
                    Soft.Animate(motion, ripple, "Opacity", colour == 0xF5B24A ? 0.8 : 0.7, 0, 1.2, reverse: false, begin: begin);
                    Add(ripple);
                }
                var orb = Soft.Circle(Cx, Cy, 17, Soft.Orb(Paint.Hex(0xF5B24A), Paint.Hex(0xD9579E), 0.4, Paint.Hex(0xFFF6E0)));
                var bounce = new ScaleTransform();
                orb.RenderTransformOrigin = new Point(0.5, 0.5);
                orb.RenderTransform = bounce;
                foreach (var axis in new[] { "ScaleX", "ScaleY" })
                {
                    var k = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = new Duration(TimeSpan.FromSeconds(1.2)) };
                    foreach (var (t, v) in new[] { (0.0, 1.0), (0.144, 19 / 17.0), (0.48, 16 / 17.0), (1.2, 1.0) })
                        k.KeyFrames.Add(new EasingDoubleKeyFrame { KeyTime = TimeSpan.FromSeconds(t), Value = v, EasingFunction = new SineEase() });
                    Storyboard.SetTarget(k, bounce);
                    Storyboard.SetTargetProperty(k, axis);
                    motion.Children.Add(k);
                }
                Add(orb, Shine(85, 40, 5, 3.5, 0.65),
                    Soft.Circle(42, 30, 3, Paint.Brush(0xF5B24A, 0xB3)), Soft.Circle(136, 62, 3.5, Paint.Brush(0xE879B4, 0xB3)),
                    Soft.Circle(140, 24, 2, Paint.Brush(0xFFFFFF, 0x99)), Soft.Circle(38, 66, 2, Paint.Brush(0xFFFFFF, 0x80)));
                break;
            }
            default: // party
            {
                var wheel = new Canvas { Width = 180, Height = 96 };
                uint[] colours = [0xE8603C, 0xE8A23C, 0x3DAA67, 0x2BB5A8, 0x3A84E8, 0xB07BFF];
                for (int k = 0; k < colours.Length; k++)
                {
                    double a = k * Math.PI / 3;
                    wheel.Children.Add(Soft.Disc(Cx + 26 * Math.Cos(a), Cy + 26 * Math.Sin(a), 15, 5, Paint.Hex(colours[k]), 0.85));
                }
                var turn = new RotateTransform { CenterX = Cx, CenterY = Cy };
                wheel.RenderTransform = turn;
                Soft.Animate(motion, turn, "Angle", 0, 360, 9, reverse: false);
                Add(wheel, Soft.Circle(Cx, Cy, 16, Soft.Orb(Paint.Hex(0xE4D8FF), Paint.Hex(0x8E7BD8), 0.45)), Shine(85, 40, 5, 3.5, 0.8),
                    Soft.Circle(132, 22, 2.4, Paint.Brush(0xFFFFFF, 0xBF)), Soft.Circle(46, 70, 2, Paint.Brush(0xFFFFFF, 0x99)),
                    Soft.Circle(44, 20, 1.6, Paint.Brush(0xFFFFFF, 0x80)));
                break;
            }
        }
    }

    void Add(params UIElement[] items)
    {
        foreach (var i in items) Children.Add(i);
    }

    /// <summary>A small soft highlight on an orb.</summary>
    static Ellipse Shine(double cx, double cy, double rx, double ry, double alpha)
    {
        var e = new Ellipse
        {
            Width = 2 * rx + 3, Height = 2 * ry + 3, IsHitTestVisible = false,
            Fill = Draw.Radial((0, Paint.Hex(0xFFFFFF, (byte)(alpha * 255))), (0.6, Paint.Hex(0xFFFFFF, (byte)(alpha * 200))), (1, Paint.Hex(0xFFFFFF, 0))),
        };
        return Draw.At(e, cx - rx - 1.5, cy - ry - 1.5);
    }
}
