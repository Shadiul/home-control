using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Windows.Foundation;
using Windows.System;

namespace HomeControl.Controls;

/// <summary>
/// A thick ring slider: a 270° arc from the bottom left, over the top, to the bottom right, with a tall pill
/// handle. Drag along the ring (or click it) to set the value; arrow keys work when it has focus.
/// Anything in <see cref="Center"/> (the value, − and + buttons) sits in the middle.
/// </summary>
public sealed partial class RadialDial : UserControl
{
    const double StartAngle = 135, Sweep = 270;

    readonly Grid root;
    readonly Canvas canvas;
    readonly Path track, fill;
    readonly Grid knob;
    readonly Border knobBar, knobFace;
    readonly List<Line> ticks = [];
    readonly TextBlock minLabel = new() { FontSize = 12, TextAlignment = TextAlignment.Center, Width = 40 };
    readonly TextBlock maxLabel = new() { FontSize = 12, TextAlignment = TextAlignment.Center, Width = 40 };
    Brush? tickBrush, tickActiveBrush;
    readonly ContentPresenter centre;
    readonly RotateTransform knobRotate = new();
    double diameter = 210, thickness = 14, value, minimum, maximum = 100;
    bool dragging;

    public RadialDial()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        track = new Path { StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        fill = new Path { StrokeThickness = thickness, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        knobBar = new Border { Width = 3, Height = 16, CornerRadius = new CornerRadius(1.5) };
        knobFace = new Border { CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Colors.White),
                                BorderBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x3D, 0, 0, 0)), BorderThickness = new Thickness(1) };
        knob = new Grid
        {
            Width = 16, Height = 34,
            RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = knobRotate,
            Children = { knobFace, knobBar },
        };
        canvas = new Canvas { Children = { track, fill, minLabel, maxLabel, knob } };
        centre = new ContentPresenter
        {
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        root = new Grid { Background = new SolidColorBrush(Colors.Transparent), Children = { canvas, centre } };
        Content = root;

        TrackBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF));
        ArcBrush = new SolidColorBrush(Colors.White);

        root.PointerPressed += OnPointerPressed;
        root.PointerMoved += OnPointerMoved;
        root.PointerReleased += OnPointerReleased;
        root.PointerCanceled += OnPointerReleased;
        root.PointerCaptureLost += (_, _) => dragging = false;
        KeyDown += OnKeyDown;
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.45;
        Layout();
    }

    /// <summary>Raised when the user changes the value (dragging, clicking or keys), not when code sets it.</summary>
    public event Action<double>? ValueChanged;

    public bool IsDragging => dragging;

    public double Minimum { get => minimum; set { minimum = value; Update(); } }
    public double Maximum { get => maximum; set { maximum = value; Update(); } }
    public double Step { get; set; } = 1;
    public double LargeStep { get; set; } = 5;

    public double Value
    {
        get => value;
        set
        {
            double v = Math.Clamp(value, minimum, maximum);
            if (v == this.value) return;
            double old = this.value;
            this.value = v;
            Update();
            (FrameworkElementAutomationPeer.FromElement(this) as RadialDialPeer)?.RaiseValueChanged(old, v);
        }
    }

    /// <summary>Overall size; the ring is drawn inside it and the bottom is trimmed where the gap is.</summary>
    public double Diameter { get => diameter; set { diameter = value; Layout(); } }
    public double Thickness { get => thickness; set { thickness = value; track.StrokeThickness = fill.StrokeThickness = value; Layout(); } }

    public Brush TrackBrush { get => track.Stroke; set => track.Stroke = value; }
    public Brush ArcBrush { get => fill.Stroke; set { fill.Stroke = value; knobBar.Background = value; } }

    public object? Center { get => centre.Content; set => centre.Content = value; }

    /// <summary>Size of the pill handle (width × height); the bar inside grows with it.</summary>
    public Size KnobSize
    {
        get => new(knob.Width, knob.Height);
        set
        {
            knob.Width = value.Width;
            knob.Height = value.Height;
            knobFace.CornerRadius = new CornerRadius(value.Width / 2);
            knobBar.Height = Math.Round(value.Height * 0.45);
            Layout();
        }
    }

    public Brush KnobBrush { get => knobFace.Background; set => knobFace.Background = value; }
    public Brush KnobStrokeBrush { get => knobFace.BorderBrush; set => knobFace.BorderBrush = value; }

    /// <summary>Small marks inside the ring, one per step when set (0 for none). Marks up to the value use the active brush.</summary>
    public int TickCount { get => ticks.Count; set { SetTicks(value); Layout(); } }
    public Brush? TickBrush { get => tickBrush; set { tickBrush = value; Update(); } }
    public Brush? TickActiveBrush { get => tickActiveBrush; set { tickActiveBrush = value; Update(); } }

    /// <summary>Text under the two ends of the ring ("16°" and "30°"); empty for none.</summary>
    public string MinLabel { get => minLabel.Text; set { minLabel.Text = value; Layout(); } }
    public string MaxLabel { get => maxLabel.Text; set { maxLabel.Text = value; Layout(); } }
    public Brush LabelBrush { get => minLabel.Foreground; set => minLabel.Foreground = maxLabel.Foreground = value; }

    void SetTicks(int count)
    {
        foreach (var t in ticks) canvas.Children.Remove(t);
        ticks.Clear();
        for (int i = 0; i < count; i++)
        {
            var line = new Line { StrokeThickness = 1.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            ticks.Add(line);
            canvas.Children.Insert(0, line);
        }
    }

    double Radius => diameter / 2 - Math.Max(thickness / 2, knob.Height / 2) - 4;
    Point Centre => new(diameter / 2, diameter / 2);

    Point PointAt(double angle)
    {
        double a = angle * Math.PI / 180;
        return new Point(Centre.X + Radius * Math.Cos(a), Centre.Y + Radius * Math.Sin(a));
    }

    Geometry Arc(double fromAngle, double sweep)
    {
        sweep = Math.Max(sweep, 0.01);
        var figure = new PathFigure { StartPoint = PointAt(fromAngle), IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = PointAt(fromAngle + sweep), Size = new Size(Radius, Radius),
            SweepDirection = SweepDirection.Clockwise, IsLargeArc = sweep > 180,
        });
        var g = new PathGeometry();
        g.Figures.Add(figure);
        return g;
    }

    void Layout()
    {
        // The ring's lowest points are at 135° and 45°; trim the empty bottom below them.
        double bottom = Centre.Y + Radius * Math.Sin(Math.PI / 4) + 20;
        if (minLabel.Text.Length > 0 || maxLabel.Text.Length > 0) bottom += 18;
        root.Width = canvas.Width = diameter;
        root.Height = canvas.Height = Math.Min(diameter, bottom);
        centre.Width = diameter;
        centre.Height = diameter;
        centre.VerticalAlignment = VerticalAlignment.Top;
        track.Data = Arc(StartAngle, Sweep);
        var start = PointAt(StartAngle);
        var end = PointAt(StartAngle + Sweep);
        Canvas.SetLeft(minLabel, start.X - 20);
        Canvas.SetTop(minLabel, start.Y + 14);
        Canvas.SetLeft(maxLabel, end.X - 20);
        Canvas.SetTop(maxLabel, end.Y + 14);
        double inner = Radius - thickness / 2 - 4;
        for (int i = 0; i < ticks.Count; i++)
        {
            double a = (StartAngle + Sweep * i / Math.Max(1, ticks.Count - 1)) * Math.PI / 180;
            ticks[i].X1 = Centre.X + (inner - 5) * Math.Cos(a);
            ticks[i].Y1 = Centre.Y + (inner - 5) * Math.Sin(a);
            ticks[i].X2 = Centre.X + inner * Math.Cos(a);
            ticks[i].Y2 = Centre.Y + inner * Math.Sin(a);
        }
        Update();
    }

    double Fraction => maximum > minimum ? (value - minimum) / (maximum - minimum) : 0;

    void Update()
    {
        if (fill == null) return;
        double angle = StartAngle + Sweep * Fraction;
        fill.Data = Arc(StartAngle, Sweep * Fraction);
        var p = PointAt(angle);
        Canvas.SetLeft(knob, p.X - knob.Width / 2);
        Canvas.SetTop(knob, p.Y - knob.Height / 2);
        knobRotate.Angle = angle + 90;
        for (int i = 0; i < ticks.Count; i++)
            ticks[i].Stroke = (double)i / Math.Max(1, ticks.Count - 1) <= Fraction + 1e-9 ? tickActiveBrush : tickBrush;
    }

    // ------------------------------------------------------------ input

    /// <summary>The value under a pointer position, or null when it's away from the ring.</summary>
    double? ValueAt(Point p, bool mustBeOnRing)
    {
        double dx = p.X - Centre.X, dy = p.Y - Centre.Y;
        if (mustBeOnRing && Math.Abs(Math.Sqrt(dx * dx + dy * dy) - Radius) > thickness / 2 + 14) return null;
        double rel = (Math.Atan2(dy, dx) * 180 / Math.PI - StartAngle + 720) % 360;
        if (rel > Sweep) rel = rel > Sweep + (360 - Sweep) / 2 ? 0 : Sweep;
        double v = minimum + rel / Sweep * (maximum - minimum);
        return Math.Round(v / Step) * Step;
    }

    void SetFromUser(double v)
    {
        v = Math.Clamp(v, minimum, maximum);
        if (v == value) return;
        Value = v;
        ValueChanged?.Invoke(v);
    }

    void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled) return;
        var v = ValueAt(e.GetCurrentPoint(canvas).Position, mustBeOnRing: true);
        if (v == null) return;
        dragging = root.CapturePointer(e.Pointer);
        Focus(FocusState.Pointer);
        SetFromUser(v.Value);
        e.Handled = true;
    }

    void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!dragging) return;
        if (ValueAt(e.GetCurrentPoint(canvas).Position, mustBeOnRing: false) is { } v) SetFromUser(v);
        e.Handled = true;
    }

    void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        root.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Keys pressed on the − and + buttons in the middle belong to them.
        if (e.OriginalSource is ButtonBase) return;
        double? v = e.Key switch
        {
            VirtualKey.Left or VirtualKey.Down => value - Step,
            VirtualKey.Right or VirtualKey.Up => value + Step,
            VirtualKey.PageDown => value - LargeStep,
            VirtualKey.PageUp => value + LargeStep,
            VirtualKey.Home => minimum,
            VirtualKey.End => maximum,
            _ => null,
        };
        if (v == null) return;
        SetFromUser(v.Value);
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RadialDialPeer(this);

    internal void SetFromAutomation(double v) => SetFromUser(v);

    /// <summary>Lets screen readers treat the dial as a slider.</summary>
    sealed partial class RadialDialPeer(RadialDial owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        protected override string GetClassNameCore() => nameof(RadialDial);
        protected override object GetPatternCore(PatternInterface pattern) =>
            pattern == PatternInterface.RangeValue ? this : base.GetPatternCore(pattern);

        public bool IsReadOnly => !owner.IsEnabled;
        public double LargeChange => owner.LargeStep;
        public double SmallChange => owner.Step;
        public double Maximum => owner.Maximum;
        public double Minimum => owner.Minimum;
        public double Value => owner.Value;
        public void SetValue(double value) => owner.SetFromAutomation(value);

        public void RaiseValueChanged(double old, double now) =>
            RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, old, now);
    }
}
