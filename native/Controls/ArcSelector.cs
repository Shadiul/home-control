using LedControl.Art;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace LedControl.Controls;

/// <summary>
/// A thick arc with five positions and a tall pill handle, drawn over a picture (280 × 214), as on the
/// Climate page's fan speed and airflow panels. Drag along the arc or click a position; each position is
/// also a small button, so it works with the keyboard and screen readers.
/// </summary>
public sealed partial class ArcSelector : UserControl
{
    public const double StageWidth = 280, StageHeight = 214;

    /// <summary>How the arc is lit: from its start up to the position (an amount, like fan speed),
    /// or just around the position (a direction).</summary>
    public enum Look { Amount, Direction }

    readonly double cx, cy, r;
    readonly double[] angles;
    readonly string[] names;
    readonly Look look;
    readonly Canvas stage = new() { Width = StageWidth, Height = StageHeight, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
    readonly Path track, fill;
    readonly Ellipse[] dots;
    readonly Button[] hits;
    readonly TextBlock label1, label2;
    readonly Grid handle;
    readonly Border handleFace, handleBar;
    readonly RotateTransform handleRotate = new();
    bool dragging;
    int lastPicked = -2;

    /// <summary>The user picked a position (0-4) by dragging, clicking or with the keyboard.</summary>
    public event Action<int>? Picked;

    public ArcSelector(UIElement picture, double cx, double cy, double r, double[] angles, string[] names,
                       (string Text, double X, double Y) end1, (string Text, double X, double Y) end2, Look look, string title)
    {
        this.cx = cx;
        this.cy = cy;
        this.r = r;
        this.angles = angles;
        this.names = names;
        this.look = look;
        Width = StageWidth;
        Height = StageHeight;

        stage.Children.Add(picture);
        double lo = angles.Min(), hi = angles.Max();
        track = new Path { Data = Segment(lo, hi), StrokeThickness = 14, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        fill = new Path { StrokeThickness = 14, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        stage.Children.Add(track);
        stage.Children.Add(fill);

        dots = new Ellipse[angles.Length];
        for (int i = 0; i < angles.Length; i++)
        {
            var p = PointAt(angles[i]);
            dots[i] = new Ellipse { Width = 4.8, Height = 4.8 };
            Canvas.SetLeft(dots[i], p.X - 2.4);
            Canvas.SetTop(dots[i], p.Y - 2.4);
            stage.Children.Add(dots[i]);
        }

        label1 = EndLabel(end1);
        label2 = EndLabel(end2);

        handleFace = new Border { CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        handleBar = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5) };
        handle = new Grid
        {
            Width = 16, Height = 34, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = handleRotate,
            IsHitTestVisible = false, Children = { handleFace, handleBar },
        };
        stage.Children.Add(handle);

        // An invisible button on each position, for the keyboard, screen readers and tooltips.
        hits = new Button[angles.Length];
        for (int i = 0; i < angles.Length; i++)
        {
            var p = PointAt(angles[i]);
            int index = i;
            var b = new Button
            {
                Style = (Style)Application.Current.Resources["BareButton"], Width = 28, Height = 28, CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            };
            AutomationProperties.SetName(b, $"{title}: {names[i]}");
            ToolTipService.SetToolTip(b, names[i]);
            b.Click += (_, _) => Picked?.Invoke(index);
            Canvas.SetLeft(b, p.X - 14);
            Canvas.SetTop(b, p.Y - 14);
            hits[i] = b;
            stage.Children.Add(b);
        }

        Content = stage;
        stage.PointerPressed += OnPressed;
        stage.PointerMoved += OnMoved;
        stage.PointerReleased += OnReleased;
        stage.PointerCanceled += OnReleased;
        stage.PointerCaptureLost += (_, _) => dragging = false;
        Show(Palette.For(false), -1, false, false);
    }

    TextBlock EndLabel((string Text, double X, double Y) end)
    {
        var t = new TextBlock { Text = end.Text, FontSize = 11.5, Width = 60, TextAlignment = TextAlignment.Center, IsHitTestVisible = false };
        Canvas.SetLeft(t, end.X - 30);
        Canvas.SetTop(t, end.Y - 12);
        stage.Children.Add(t);
        return t;
    }

    Point PointAt(double deg)
    {
        double a = deg * Math.PI / 180;
        return new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
    }

    /// <summary>The arc between two angles in degrees (smaller first), clockwise.</summary>
    Geometry Segment(double from, double to)
    {
        var figure = new PathFigure { StartPoint = PointAt(from), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = PointAt(Math.Max(to, from + 0.01)), Size = new Size(r, r), SweepDirection = SweepDirection.Clockwise, IsLargeArc = to - from > 180,
        });
        var g = new PathGeometry();
        g.Figures.Add(figure);
        return g;
    }

    /// <summary>
    /// index: the chosen position, or -1 for none (automatic). sweeping: the whole range is in use (swing).
    /// active: the position is set by hand (accent colour) rather than parked (grey).
    /// </summary>
    public void Show(Palette p, int index, bool sweeping, bool active)
    {
        track.Stroke = Palette.B(p.Track);
        label1.Foreground = label2.Foreground = Palette.B(p.Text3);
        handleFace.Background = Palette.B(p.ThumbRing);
        handleFace.BorderBrush = Palette.B(p.CtrlStroke);

        double lo = angles.Min(), hi = angles.Max();
        if (look == Look.Amount)
        {
            int at = index < 0 ? 2 : index;
            fill.Data = Segment(Math.Min(angles[0], angles[at]), Math.Max(angles[0], angles[at]));
            fill.Stroke = Palette.B(active ? p.Accent : p.Text3);
            fill.Opacity = active ? 1 : 0.35;
        }
        else if (sweeping)
        {
            fill.Data = Segment(lo, hi);
            fill.Stroke = Palette.B(p.Accent);
            fill.Opacity = 0.3;
        }
        else if (index >= 0)
        {
            double a = angles[index];
            fill.Data = Segment(Math.Max(lo, a - 9), Math.Min(hi, a + 9));
            fill.Stroke = Palette.B(p.Accent);
            fill.Opacity = 1;
        }
        else fill.Opacity = 0;

        for (int i = 0; i < dots.Length; i++)
        {
            dots[i].Fill = Palette.B(active && i == index ? p.OnAccent : p.Text3);
            AutomationProperties.SetItemStatus(hits[i], active && i == index ? "Selected" : "");
        }

        // Automatic and swing park the handle in the middle.
        int shown = index < 0 || sweeping ? 2 : index;
        var hp = PointAt(angles[shown]);
        Canvas.SetLeft(handle, hp.X - 8);
        Canvas.SetTop(handle, hp.Y - 17);
        handleRotate.Angle = angles[shown] + 90;
        handleBar.Background = Palette.B(active ? p.Accent : p.Text3);
    }

    int NearestAt(Point pt)
    {
        double deg = Math.Atan2(pt.Y - cy, pt.X - cx) * 180 / Math.PI;
        // Arcs that pass over the top use angles above 180°: keep the pointer's angle on the same side.
        if (angles.Max() > 180 && deg < 90) deg += 360;
        int best = 0;
        for (int i = 1; i < angles.Length; i++)
            if (Math.Abs(angles[i] - deg) < Math.Abs(angles[best] - deg)) best = i;
        return best;
    }

    void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(stage).Position;
        if (Math.Abs(Math.Sqrt((pt.X - cx) * (pt.X - cx) + (pt.Y - cy) * (pt.Y - cy)) - r) > 30) return;
        dragging = stage.CapturePointer(e.Pointer);
        lastPicked = NearestAt(pt);
        Picked?.Invoke(lastPicked);
        e.Handled = true;
    }

    void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!dragging) return;
        int i = NearestAt(e.GetCurrentPoint(stage).Position);
        if (i != lastPicked)
        {
            lastPicked = i;
            Picked?.Invoke(i);
        }
        e.Handled = true;
    }

    void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        lastPicked = -2;
        stage.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }
}
