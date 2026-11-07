using LedControl.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace LedControl.Art;

/// <summary>Pictures for the Climate page's fan and airflow panels, drawn on a 280 × 214 canvas.</summary>
static class Draw
{
    public static Geometry Geo(string data) => (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);

    public static string F(double n) => n.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

    public static Path Shape(string data, Brush? fill = null, Brush? stroke = null, double width = 1, bool round = false)
    {
        var p = new Path { Data = Geo(data), Fill = fill, Stroke = stroke, StrokeThickness = width };
        if (round) p.StrokeStartLineCap = p.StrokeEndLineCap = PenLineCap.Round;
        return p;
    }

    public static T At<T>(T e, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        return e;
    }

    /// <summary>
    /// A new radial gradient. (WinUI doesn't redraw a radial gradient whose stops change after it's first shown,
    /// so changing colours means making a new one.)
    /// </summary>
    public static RadialGradientBrush Radial(Point center, double rx, double ry, params (double Offset, Color Colour)[] stops)
    {
        var b = new RadialGradientBrush { Center = center, GradientOrigin = center, RadiusX = rx, RadiusY = ry };
        foreach (var (o, c) in stops) b.GradientStops.Add(new GradientStop { Offset = o, Color = c });
        return b;
    }

    public static RadialGradientBrush Radial(params (double Offset, Color Colour)[] stops) => Radial(new(0.5, 0.5), 0.5, 0.5, stops);

    public static LinearGradientBrush Vertical(params (double Offset, Color Colour)[] stops) => Gradient(new(0, 0), new(0, 1), stops);

    public static LinearGradientBrush Gradient(Point from, Point to, params (double Offset, Color Colour)[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = from, EndPoint = to };
        foreach (var (o, c) in stops) b.GradientStops.Add(new GradientStop { Offset = o, Color = c });
        return b;
    }

    public static readonly Color Stroke = Paint.Hex(0xCDD2D7), Outlet = Paint.Hex(0x2B3138), Blade = Paint.Hex(0xE4E7EA);

    /// <summary>A 24 × 24 line icon shown at <paramref name="size"/>; colour it with <see cref="Tint"/>.</summary>
    public static (Viewbox Box, Path[] Strokes) LineIcon(string data, double size, double width = 1.7)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        var paths = data.Split('|').Select(d => new Path
        {
            Data = Geo(d), StrokeThickness = width, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        }).ToArray();
        foreach (var p in paths) canvas.Children.Add(p);
        return (new Viewbox { Width = size, Height = size, Child = canvas }, paths);
    }

    public static void Tint(Path[] strokes, Color c)
    {
        foreach (var p in strokes) p.Stroke = new SolidColorBrush(c);
    }
}

/// <summary>
/// Fan speed: a housing with a rotor that spins faster at each step, a soft glow, and streams of air flowing
/// out to both sides, more of them, longer and faster at each step. Still while the unit is off.
/// </summary>
public sealed partial class FanScene : Canvas
{
    readonly Ellipse glow = new();
    readonly Canvas winds = new();
    readonly Ellipse housing = new() { Width = 88, Height = 88, StrokeThickness = 3 };
    readonly Ellipse inner = new() { Width = 76, Height = 76, StrokeThickness = 1, StrokeDashArray = [2, 4] };
    readonly Canvas rotor = new();
    readonly RotateTransform spin = new();
    readonly Path[] blades = new Path[5];
    readonly Ellipse hub = new() { Width = 16, Height = 16, StrokeThickness = 3 };
    readonly TextBlock caption = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Width = 120, TextAlignment = TextAlignment.Center };
    readonly Storyboard spinning = new() { RepeatBehavior = RepeatBehavior.Forever };
    readonly Storyboard flowing = new() { RepeatBehavior = RepeatBehavior.Forever };
    string shownKey = "";

    public FanScene()
    {
        Width = ArcSelector.StageWidth;
        Height = ArcSelector.StageHeight;
        Children.Add(glow);
        Children.Add(winds);
        Children.Add(Draw.At(housing, 96, 78));
        Children.Add(Draw.At(inner, 102, 84));
        // Rotor: five curved blades round the centre (140, 122).
        rotor.RenderTransform = new TransformGroup { Children = { spin, new ScaleTransform { ScaleX = 1.05, ScaleY = 1.05 }, new TranslateTransform { X = 140, Y = 122 } } };
        for (int i = 0; i < 5; i++)
        {
            blades[i] = Draw.Shape("M0 -6C10 -12 22 -24 22 -32C14 -36 4 -30 0 -6Z");
            blades[i].RenderTransform = new RotateTransform { Angle = 72 * i };
            rotor.Children.Add(blades[i]);
        }
        Children.Add(rotor);
        Children.Add(Draw.At(hub, 132, 114));
        Children.Add(Draw.At(caption, 80, 178));

        var turn = new DoubleAnimation { From = 0, To = 360 };
        Storyboard.SetTarget(turn, spin);
        Storyboard.SetTargetProperty(turn, "Angle");
        spinning.Children.Add(turn);
        Unloaded += (_, _) => { spinning.Stop(); flowing.Stop(); shownKey = ""; };
    }

    /// <summary>level 1-5 (the speed set, or remembered while on Auto).</summary>
    public void Show(Palette p, bool on, bool auto, int level)
    {
        int n = auto ? 3 : Math.Clamp(level, 1, 5);
        var blade = on ? (auto ? p.Text2 : p.Accent) : p.Text3;
        foreach (var b in blades) b.Fill = Palette.B(blade);
        housing.Fill = hub.Fill = Palette.B(p.Card);
        housing.Stroke = inner.Stroke = Palette.B(p.Track);
        hub.Stroke = Palette.B(blade);
        caption.Text = auto ? "AUTO" : on ? "" : "Off";
        caption.Foreground = Palette.B(auto ? p.Accent : p.Text3);

        double gr = 52 + n * 7;
        glow.Width = glow.Height = gr * 2;
        Draw.At(glow, 140 - gr, 122 - gr);
        glow.Fill = Draw.Radial((0, Paint.WithAlpha(p.Accent, 0.35)), (1, Paint.WithAlpha(p.Accent, 0)));
        glow.Opacity = on ? Math.Min(1, 0.35 + n * 0.13) : 0;

        // Rebuild the moving parts only when what they show changes.
        string key = $"{on}|{auto}|{n}|{p.Accent}|{p.Dark}";
        if (key == shownKey) return;
        shownKey = key;
        spinning.Stop();
        flowing.Stop();
        flowing.Children.Clear();
        winds.Children.Clear();
        if (!on) return;

        // One full turn takes 2.4 s at Low down to 0.45 s at High; Auto turns at a steady medium pace.
        double turnSeconds = auto ? 1.2 : new[] { 2.4, 1.5, 1.0, 0.7, 0.45 }[n - 1];
        ((DoubleAnimation)spinning.Children[0]).Duration = new Duration(TimeSpan.FromSeconds(turnSeconds));

        double dur = auto ? 1.3 : new[] { 2.2, 1.6, 1.15, 0.85, 0.6 }[n - 1];
        double width = 2 + n * 0.25;
        var wind = Draw.Gradient(new(0, 0), new(1, 0),
            (0, Paint.WithAlpha(p.Accent, 0.2)), (0.5, p.Accent), (1, Paint.WithAlpha(p.Accent, 0.2)));
        for (int k = 0; k < n; k++)
        {
            double off = (k - (n - 1) / 2.0) * 12, reach = 30 + k * 4 + n * 3, y = 122 + off;
            string right = $"M186 {Draw.F(y)}C{Draw.F(200 + reach * 0.3)} {Draw.F(y - 4)} {Draw.F(210 + reach * 0.6)} {Draw.F(y + 10)} {Draw.F(214 + reach * 0.7)} {Draw.F(y + 30 + k * 4)}";
            string left = $"M94 {Draw.F(y)}C{Draw.F(80 - reach * 0.3)} {Draw.F(y - 4)} {Draw.F(70 - reach * 0.6)} {Draw.F(y + 10)} {Draw.F(66 - reach * 0.7)} {Draw.F(y + 30 + k * 4)}";
            foreach (var d in new[] { right, left })
            {
                // Dashes are measured in stroke widths: 10 on, 12 off.
                var path = Draw.Shape(d, null, wind, width, round: true);
                path.StrokeDashArray = [10 / width, 12 / width];
                path.Opacity = 0.45 + 0.1 * n - k * 0.04;
                winds.Children.Add(path);
                var move = new DoubleAnimation
                {
                    From = 0, To = -44 / width, Duration = new Duration(TimeSpan.FromSeconds(dur + k * 0.12)),
                    EnableDependentAnimation = true, RepeatBehavior = RepeatBehavior.Forever,
                };
                Storyboard.SetTarget(move, path);
                Storyboard.SetTargetProperty(move, "StrokeDashOffset");
                flowing.Children.Add(move);
            }
        }
        if (IsLoaded)
        {
            spinning.Begin();
            flowing.Begin();
        }
        else
        {
            Loaded -= StartWhenLoaded;
            Loaded += StartWhenLoaded;
        }
    }

    void StartWhenLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= StartWhenLoaded;
        if (shownKey.StartsWith("True"))
        {
            spinning.Begin();
            flowing.Begin();
        }
    }
}

/// <summary>Up and down: the unit seen from the side on its wall, with the louvre flap and a beam of air.</summary>
public sealed partial class SideUnitScene : Canvas
{
    static readonly double[] Angles = [0, 22.5, 45, 67.5, 90];
    readonly Rectangle wall = new() { Width = 6, Height = 186, RadiusX = 2, RadiusY = 2 };
    readonly Path beam = new();
    readonly Path flap = new() { Stroke = new SolidColorBrush(Draw.Blade), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

    public SideUnitScene()
    {
        Width = ArcSelector.StageWidth;
        Height = ArcSelector.StageHeight;
        Children.Add(Draw.At(wall, 26, 14));
        Children.Add(beam);
        Children.Add(Draw.Shape("M32 34H96A18 18 0 0 1 114 52V74L97 98H32Z",
            Draw.Gradient(new(0, 0), new(1, 1), (0, Paint.Hex(0xFFFFFF)), (0.65, Paint.Hex(0xEEF0F2)), (1, Paint.Hex(0xD5DADF))),
            new SolidColorBrush(Draw.Stroke)));
        Children.Add(Draw.Shape("M38 40h50M38 45h46", null, new SolidColorBrush(Draw.Stroke), 1.2, round: true));
        Children.Add(Draw.Shape("M110.5 54v16", null, new SolidColorBrush(Paint.Hex(0xFFFFFF)), 2, round: true));
        Children.Add(Draw.Shape("M114 74L97 98H88L105 73.5Z", new SolidColorBrush(Draw.Outlet)));
        Children.Add(flap);
    }

    /// <summary>index 0-4 (straight ahead to straight down) or -1 for automatic.</summary>
    public void Show(Palette p, int index, bool swing)
    {
        wall.Fill = Palette.B(p.Track);
        int vi = swing || index < 0 ? 2 : index;
        double vd = Angles[vi], rad = vd * Math.PI / 180;
        flap.Data = Draw.Geo($"M101 86 L{Draw.F(101 + 14 * Math.Cos(rad))} {Draw.F(86 + 14 * Math.Sin(rad))}");
        double half = swing ? 46 : 11, len = 74, c = swing ? 45 : vd;
        string Q(double deg, double rr) => $"{Draw.F(103 + rr * Math.Cos(deg * Math.PI / 180))} {Draw.F(88 + rr * Math.Sin(deg * Math.PI / 180))}";
        beam.Data = Draw.Geo($"M{Q(c - half, 6)} L{Q(c - half, len)} A{len} {len} 0 0 1 {Q(c + half, len)} L{Q(c + half, 6)} Z");
        beam.Fill = Palette.B(p.Accent);
        beam.Opacity = index < 0 && !swing ? 0.12 : 0.24;
    }
}

/// <summary>Left and right: the unit seen from the front, with its display and a beam of air under it.</summary>
public sealed partial class FrontUnitScene : Canvas
{
    static readonly double[] Angles = [160, 125, 90, 55, 20];
    readonly Path beam = new();
    readonly TextBlock display = new() { FontSize = 7.5, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Width = 26, TextAlignment = TextAlignment.Center, Foreground = new SolidColorBrush(Paint.Hex(0x6FD3FF)) };
    readonly Rectangle blade = new() { Width = 124, RadiusX = 1.5, RadiusY = 1.5, Fill = new SolidColorBrush(Draw.Blade) };

    public FrontUnitScene()
    {
        Width = ArcSelector.StageWidth;
        Height = ArcSelector.StageHeight;
        Children.Add(beam);
        Children.Add(Draw.At(new Rectangle
        {
            Width = 148, Height = 50, RadiusX = 14, RadiusY = 14, Stroke = new SolidColorBrush(Draw.Stroke),
            Fill = Draw.Vertical((0, Paint.Hex(0xFFFFFF)), (0.7, Paint.Hex(0xF1F3F5)), (1, Paint.Hex(0xDDE1E5))),
        }, 66, 22));
        Children.Add(Draw.Shape("M80 30h120M80 34h120", null, new SolidColorBrush(Paint.Hex(0xC9CED4)), 1.2, round: true));
        Children.Add(Draw.At(new Rectangle { Width = 26, Height = 11, RadiusX = 3, RadiusY = 3, Fill = new SolidColorBrush(Paint.Hex(0x1E2730)) }, 176, 40));
        Children.Add(Draw.At(display, 176, 40.5));
        Children.Add(Draw.Shape("M72 58H208V63A9 9 0 0 1 199 72H81A9 9 0 0 1 72 63Z", new SolidColorBrush(Draw.Outlet)));
        Children.Add(Draw.At(blade, 78, 64));
    }

    /// <summary>index 0-4 (far left to far right) or -1 for automatic; vertical 0-4 is how far down the air points.</summary>
    public void Show(Palette p, int index, bool swing, int vertical, bool on, int target)
    {
        double vFrac = vertical / 4.0;
        double hd = swing || index < 0 ? 90 : Angles[index], a = hd * Math.PI / 180;
        double len = 70 + 50 * vFrac, w = (swing ? 70 : 26) + 16 * vFrac, bx = 140, by = 70;
        double ex = bx + Math.Cos(a) * len, ey = by + Math.Sin(a) * len, nx = -Math.Sin(a), ny = Math.Cos(a);
        beam.Data = Draw.Geo($"M{Draw.F(bx - 60)} {Draw.F(by)} L{Draw.F(ex + nx * w)} {Draw.F(ey + ny * w)} L{Draw.F(ex - nx * w)} {Draw.F(ey - ny * w)} L{Draw.F(bx + 60)} {Draw.F(by)} Z");
        beam.Fill = Draw.Vertical((0, Paint.WithAlpha(p.Accent, 0.4)), (1, Paint.WithAlpha(p.Accent, 0)));
        beam.Opacity = index < 0 && !swing ? 0.55 : 1;
        display.Text = on ? $"{target}°" : "--";
        Canvas.SetTop(blade, 64 - vertical * 0.6);
        blade.Height = 2 + vertical * 1.3;
    }
}
