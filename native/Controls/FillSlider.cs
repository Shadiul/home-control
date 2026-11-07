using LedControl.Art;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI;

namespace LedControl.Controls;

/// <summary>
/// A slider drawn in code: drag or click anywhere on it, use the arrow keys, Page Up/Down, Home and End, and it
/// reads as a standard slider to screen readers. <see cref="ValueChanged"/> fires only for the user's changes,
/// so setting <see cref="Value"/> to show a device's state never sends anything back.
/// </summary>
public abstract partial class ValueSlider : UserControl
{
    double value;

    public double Minimum { get; set; }
    public double Maximum { get; set; } = 100;
    public double SmallChange { get; set; } = 1;
    public double LargeChange { get; set; } = 10;
    public bool IsDragging { get; private set; }

    /// <summary>What a screen reader says for the value ("35%", "Warm").</summary>
    public string ValueText { get; set; } = "";

    /// <summary>The user moved it (dragging, clicking or keys). Whole steps only.</summary>
    public event Action<double>? ValueChanged;

    protected ValueSlider()
    {
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        PointerPressed += OnPressed;
        PointerMoved += OnMoved;
        PointerReleased += OnReleased;
        PointerCanceled += OnReleased;
        PointerCaptureLost += (_, _) => IsDragging = false;
        KeyDown += OnKey;
        SizeChanged += (_, _) => Layout();
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : 0.45;
    }

    public double Value
    {
        get => value;
        set
        {
            this.value = Math.Clamp(value, Minimum, Math.Max(Minimum, Maximum));
            Layout();
        }
    }

    /// <summary>0-1 along the track.</summary>
    protected double Fraction => Maximum > Minimum ? (value - Minimum) / (Maximum - Minimum) : 0;

    /// <summary>Puts the fill, grip and text where the value says.</summary>
    protected abstract void Layout();

    /// <summary>The part of the control's width the track spans: left edge and width.</summary>
    protected virtual (double Left, double Width) Track => (0, ActualWidth);

    void SetByUser(double v)
    {
        v = Math.Clamp(Math.Round(v / SmallChange) * SmallChange, Minimum, Maximum);
        if (v == value) return;
        Value = v;
        ValueChanged?.Invoke(v);
        if (FrameworkElementAutomationPeer.FromElement(this) is { } peer)
            peer.RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, v, v);
    }

    double ValueAt(double x)
    {
        var (left, width) = Track;
        double f = width > 0 ? Math.Clamp((x - left) / width, 0, 1) : 0;
        return Minimum + f * (Maximum - Minimum);
    }

    void OnPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled) return;
        Focus(FocusState.Pointer);
        IsDragging = CapturePointer(e.Pointer);
        SetByUser(ValueAt(e.GetCurrentPoint(this).Position.X));
        e.Handled = true;
    }

    void OnMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!IsDragging) return;
        SetByUser(ValueAt(e.GetCurrentPoint(this).Position.X));
        e.Handled = true;
    }

    void OnReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!IsDragging) return;
        IsDragging = false;
        ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    void OnKey(object sender, KeyRoutedEventArgs e)
    {
        double? to = e.Key switch
        {
            VirtualKey.Left or VirtualKey.Down => value - SmallChange,
            VirtualKey.Right or VirtualKey.Up => value + SmallChange,
            VirtualKey.PageDown => value - LargeChange,
            VirtualKey.PageUp => value + LargeChange,
            VirtualKey.Home => Minimum,
            VirtualKey.End => Maximum,
            _ => null,
        };
        if (to is { } v && IsEnabled)
        {
            SetByUser(v);
            e.Handled = true;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

    sealed partial class Peer(ValueSlider owner) : FrameworkElementAutomationPeer(owner), IRangeValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
        protected override string GetClassNameCore() => "Slider";
        protected override object GetPatternCore(PatternInterface pattern) =>
            pattern == PatternInterface.RangeValue ? this : base.GetPatternCore(pattern);
        protected override string GetItemStatusCore() => owner.ValueText;

        public bool IsReadOnly => !owner.IsEnabled;
        public double LargeChange => owner.LargeChange;
        public double Maximum => owner.Maximum;
        public double Minimum => owner.Minimum;
        public double SmallChange => owner.SmallChange;
        public double Value => owner.Value;
        public void SetValue(double value) => owner.SetByUser(value);
    }
}

/// <summary>
/// A fill slider: the whole rounded pill is the slider. It fills from the left in the device's colour with a
/// white grip at the edge of the fill, and can show an icon and name on the left and the value on the right.
/// </summary>
public sealed partial class FillSlider : ValueSlider
{
    readonly Border track, fill, grip, glow;
    readonly Grid labels;
    readonly TextBlock name, valueLabel;
    readonly Microsoft.UI.Xaml.Shapes.Path[] icon = [];

    public FillSlider(string label, string? iconData = null, bool showText = true, double height = 48)
    {
        Height = height;
        CornerRadius = new CornerRadius(14);
        AutomationProperties.SetName(this, label);
        track = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1) };
        glow = new Border { HorizontalAlignment = HorizontalAlignment.Left, Opacity = 0, IsHitTestVisible = false };
        fill = new Border { HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(14, 0, 0, 14) };
        grip = new Border
        {
            Width = 5, Height = 24, CornerRadius = new CornerRadius(2.5), Background = Paint.Brush(0xFFFFFF),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = Paint.Brush(0, 0x26), BorderThickness = new Thickness(0.5),
        };
        name = new TextBlock { Text = label, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        valueLabel = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        if (iconData != null)
        {
            var (box, strokes) = Draw.LineIcon(iconData, 18, 1.8);
            icon = strokes;
            left.Children.Add(box);
        }
        left.Children.Add(name);
        labels = new Grid
        {
            Padding = new Thickness(18, 0, 14, 0), IsHitTestVisible = false, Visibility = showText ? Visibility.Visible : Visibility.Collapsed,
            Children = { left, valueLabel },
        };
        // The fill and grip are clipped to the pill's rounded corners.
        var clip = new Grid { CornerRadius = new CornerRadius(14), Children = { fill, grip } };
        Content = new Grid { Background = Paint.Brush(0, 0), Children = { glow, track, clip, labels } };
    }

    /// <summary>Colours: the track (null: the tile surface), the fill, the text, and an optional glow round the fill.</summary>
    public void SetColours(Palette p, Brush? trackBrush, Brush? fillBrush, Color text, Color? glowColour = null)
    {
        track.Background = trackBrush ?? Palette.B(p.Tile);
        track.BorderBrush = Palette.B(trackBrush == null ? p.TileStroke : Paint.Hex(0xFFFFFF, 0x24));
        fill.Background = fillBrush;
        name.Foreground = valueLabel.Foreground = Palette.B(text);
        Draw.Tint(icon, text);
        if (glowColour is { } g)
        {
            glow.Background = Draw.Vertical((0, Paint.WithAlpha(g, 0)), (0.5, Paint.WithAlpha(g, 0.4)), (1, Paint.WithAlpha(g, 0)));
            glow.Opacity = 1;
        }
        else glow.Opacity = 0;
    }

    /// <summary>A fill that runs from a faint to a stronger version of a colour.</summary>
    public static Brush FillOf(Color c, double from = 0x55 / 255.0, double to = 0xAA / 255.0) =>
        Draw.Gradient(new(0, 0), new(1, 0), (0, Paint.WithAlpha(c, from)), (1, Paint.WithAlpha(c, to)));

    public string Text
    {
        set
        {
            valueLabel.Text = value;
            ValueText = value;
        }
    }

    protected override void Layout()
    {
        double w = ActualWidth, x = w * Fraction;
        fill.Width = Math.Max(0, x);
        glow.Width = Math.Max(0, x + 10);
        glow.Margin = new Thickness(-4, -8, 0, -8);
        grip.Margin = new Thickness(Math.Max(6, x - 10), 0, 0, 0);
    }
}

/// <summary>
/// The Light page's thick slider: a 16px rounded track that fills in a colour (with a glow), and a tall white
/// handle with a coloured outline and bar. With no fill, the track itself can carry a gradient (warmth).
/// </summary>
public sealed partial class ThickSlider : ValueSlider
{
    const double Thumb = 20;
    readonly Border track, fill, glow, thumb, thumbBar, thumbShadow;
    readonly Grid thumbHost;

    public ThickSlider(string label)
    {
        Height = 44;
        AutomationProperties.SetName(this, label);
        track = new Border { Height = 16, CornerRadius = new CornerRadius(8), VerticalAlignment = VerticalAlignment.Center };
        glow = new Border { Height = 36, CornerRadius = new CornerRadius(18), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        fill = new Border { Height = 16, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        thumbShadow = new Border
        {
            Width = 34, Height = 50, CornerRadius = new CornerRadius(17), Margin = new Thickness(-7, -5, -7, -9),
            Background = Draw.Radial((0, Paint.Hex(0, 0x40)), (0.6, Paint.Hex(0, 0x18)), (1, Paint.Hex(0, 0))), IsHitTestVisible = false,
        };
        thumbBar = new Border { Width = 4, Height = 14, CornerRadius = new CornerRadius(2) };
        thumb = new Border
        {
            Width = Thumb, Height = 36, CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(2),
            Background = Paint.Brush(0xFFFFFF), Child = thumbBar,
        };
        thumbHost = new Grid
        {
            Width = Thumb, Height = 36, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false, Children = { thumbShadow, thumb },
        };
        Content = new Grid { Background = Paint.Brush(0, 0), Children = { track, glow, fill, thumbHost } };
    }

    /// <summary>trackBrush: the empty track (or the whole gradient); fillBrush null for none; ring: the handle's colour.</summary>
    public void SetColours(Brush trackBrush, Brush? fillBrush, Color? glowColour, Color ring)
    {
        track.Background = trackBrush;
        fill.Background = fillBrush;
        fill.Visibility = fillBrush == null ? Visibility.Collapsed : Visibility.Visible;
        glow.Background = glowColour is { } g
            ? Draw.Vertical((0, Paint.WithAlpha(g, 0)), (0.5, Paint.WithAlpha(g, 0.45)), (1, Paint.WithAlpha(g, 0)))
            : null;
        thumb.BorderBrush = thumbBar.Background = new SolidColorBrush(ring);
    }

    protected override void Layout()
    {
        double x = ActualWidth * Fraction;
        fill.Width = Math.Max(16, x);
        glow.Width = Math.Max(0, x + 8);
        glow.Margin = new Thickness(-4, 0, 0, 0);
        thumbHost.Margin = new Thickness(Math.Clamp(x - Thumb / 2, -Thumb / 2, ActualWidth - Thumb / 2), 0, 0, 0);
    }
}
