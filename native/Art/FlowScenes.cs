using HomeControl.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace HomeControl.Art;

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
/// Fan speed: a round fan in a bezel, centred inside its 270° ring slider so the ring keeps an even distance
/// from it. A recessed well, seven swept blades with a light edge, a front grille of two thin rings and twelve
/// spokes, a metal hub with a highlight, and a faint blur disc that strengthens with speed. Rings of air circle
/// between the fan and the slider, turning the same way as the rotor: one more ring, and faster, at each step.
/// The rotor speeds up from Low to High and stands still while the unit is off.
/// </summary>
public sealed partial class FanScene : Canvas
{
    const double Cx = 140, Cy = 104;
    const string BladeData = "M0 -9C9 -13 17 -24 15.5 -39.5C8.5 -42.5 -1.5 -40 -5 -32C-6.5 -23 -4 -14 0 -9Z";

    readonly Ellipse glow = new();
    readonly Canvas winds = new();
    readonly Ellipse bezel = new() { Width = 102, Height = 102, StrokeThickness = 1 };
    readonly Ellipse well = new() { Width = 90, Height = 90, StrokeThickness = 1.5 };
    readonly Ellipse blur = new() { Width = 82, Height = 82 };
    readonly Canvas rotor = new();
    readonly RotateTransform spin = new();
    readonly Path[] blades = new Path[7];
    readonly Ellipse ring1 = new() { Width = 48, Height = 48, StrokeThickness = 1 };
    readonly Ellipse ring2 = new() { Width = 68, Height = 68, StrokeThickness = 1 };
    readonly Path spokes;
    readonly Ellipse hub = new() { Width = 22, Height = 22, StrokeThickness = 2.5 };
    readonly TextBlock caption = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Width = 80, TextAlignment = TextAlignment.Center };
    readonly Storyboard spinning = new() { RepeatBehavior = RepeatBehavior.Forever };
    readonly Storyboard flowing = new() { RepeatBehavior = RepeatBehavior.Forever };
    string shownKey = "";

    public FanScene()
    {
        Width = ArcSelector.StageWidth;
        Height = ArcSelector.StageHeight;
        Children.Add(glow);
        Children.Add(winds);
        Children.Add(Draw.At(bezel, Cx - 51, Cy - 51));
        Children.Add(Draw.At(well, Cx - 45, Cy - 45));
        Children.Add(Draw.At(blur, Cx - 41, Cy - 41));
        rotor.RenderTransform = new TransformGroup { Children = { spin, new TranslateTransform { X = Cx, Y = Cy } } };
        for (int i = 0; i < blades.Length; i++)
        {
            blades[i] = Draw.Shape(BladeData, null, new SolidColorBrush(Paint.Hex(0xFFFFFF, 0x59)), 1);
            blades[i].RenderTransform = new RotateTransform { Angle = 360.0 / blades.Length * i };
            rotor.Children.Add(blades[i]);
        }
        Children.Add(rotor);
        Children.Add(Draw.At(ring1, Cx - 24, Cy - 24));
        Children.Add(Draw.At(ring2, Cx - 34, Cy - 34));
        var grille = string.Concat(Enumerable.Range(0, 12).Select(k =>
        {
            double a = k * Math.PI / 6, c = Math.Cos(a), sn = Math.Sin(a);
            return $"M{Draw.F(Cx + 13 * c)} {Draw.F(Cy + 13 * sn)}L{Draw.F(Cx + 44 * c)} {Draw.F(Cy + 44 * sn)}";
        }));
        spokes = Draw.Shape(grille, null, null, 1);
        Children.Add(spokes);
        Children.Add(Draw.At(hub, Cx - 11, Cy - 11));
        hub.Fill = Draw.Radial(new(0.38, 0.32), 0.75, 0.75, (0, Paint.Hex(0xFFFFFF)), (0.55, Paint.Hex(0xD9DDE2)), (1, Paint.Hex(0x9AA1AA)));
        Children.Add(Draw.At(new Ellipse { Width = 5.2, Height = 5.2, Fill = Paint.Brush(0xFFFFFF, 0xB3) }, 136.5 - 2.6, 100.5 - 2.6));
        Children.Add(Draw.At(caption, Cx - 40, 182));

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
        foreach (var b in blades) b.Fill = new SolidColorBrush(Paint.WithAlpha(blade, 0.92));
        bezel.Fill = Draw.Vertical((0, p.Card), (1, p.Track));
        bezel.Stroke = Palette.B(p.CtrlStroke);
        var deep = Paint.Mix(p.Card, p.Dark ? Paint.Hex(0xFFFFFF) : Paint.Hex(0), p.Dark ? 0.04 : 0.035);
        well.Fill = Draw.Radial(new(0.5, 0.42), 0.6, 0.6, (0, p.Card), (1, deep));
        well.Stroke = Palette.B(p.Track);
        blur.Fill = Palette.B(blade);
        blur.Opacity = on ? 0.05 + n * 0.035 : 0;
        var grilleBrush = Palette.B(Paint.WithAlpha(p.Text3, 0.22));
        ring1.Stroke = ring2.Stroke = spokes.Stroke = grilleBrush;
        hub.Stroke = Palette.B(blade);
        caption.Text = auto ? "AUTO" : on ? "" : "Off";
        caption.Foreground = Palette.B(auto ? p.Accent : p.Text3);

        double gr = 58 + n * 5;
        glow.Width = glow.Height = gr * 2;
        Draw.At(glow, Cx - gr, Cy - gr);
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

        // Rings of air: a dashed circle each, its dashes running clockwise like the rotor.
        double dur = auto ? 1.6 : new[] { 3, 2.2, 1.6, 1.15, 0.8 }[n - 1];
        for (int k = 0; k < n; k++)
        {
            double r = 56 + k * 3.6, dash = 10 + k * 3, width = 2.2 - k * 0.2;
            string d = $"M{Draw.F(Cx - r)} {Draw.F(Cy)}a{Draw.F(r)} {Draw.F(r)} 0 1 1 {Draw.F(2 * r)} 0a{Draw.F(r)} {Draw.F(r)} 0 1 1 {Draw.F(-2 * r)} 0";
            var ring = Draw.Shape(d, null, Palette.B(p.Accent), width, round: true);
            ring.StrokeDashArray = [dash / width, (40 - dash) / width]; // dashes are measured in stroke widths
            ring.Opacity = 0.62 - k * 0.08;
            winds.Children.Add(ring);
            var move = new DoubleAnimation
            {
                From = 0, To = -80 / width, Duration = new Duration(TimeSpan.FromSeconds(dur * (1 + k * 0.15))),
                EnableDependentAnimation = true, RepeatBehavior = RepeatBehavior.Forever,
            };
            Storyboard.SetTarget(move, ring);
            Storyboard.SetTargetProperty(move, "StrokeDashOffset");
            flowing.Children.Add(move);
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
    readonly Rectangle wall = new() { Width = 6, Height = 168, RadiusX = 2, RadiusY = 2 };
    readonly Path beam = new();
    readonly Path flap = new() { Stroke = new SolidColorBrush(Draw.Blade), StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };

    public SideUnitScene()
    {
        Width = ArcSelector.StageWidth;
        Height = ArcSelector.StageHeight;
        // Drawn 14px higher, so the unit, its arc and labels span the same height as the other two panels.
        RenderTransform = new TranslateTransform { Y = -14 };
        Children.Add(Draw.At(wall, 26, 24));
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
