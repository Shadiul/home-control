using LedControl.Art;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace LedControl.Controls;

/// <summary>Soft coloured glows and shadows, drawn with gradients (XAML has no blur for elements).</summary>
public static class Glow
{
    /// <summary>A soft round glow: strongest in the middle, fading out to the edge.</summary>
    public static RadialGradientBrush Soft(Color c, double alpha) =>
        Draw.Radial((0, Paint.WithAlpha(c, alpha)), (0.55, Paint.WithAlpha(c, alpha * 0.6)), (1, Paint.WithAlpha(c, 0)));

    /// <summary>
    /// A soft shadow in a colour, for a tile: a box a little larger than the tile and shifted down, put behind it.
    /// </summary>
    public static Border Shadow(double corner = 18) => new()
    {
        CornerRadius = new CornerRadius(corner), Margin = new Thickness(-6, -2, -6, -10), Opacity = 0, IsHitTestVisible = false,
    };
}

/// <summary>
/// The shared colour swatch, used by every colour picker (the Light page, Music sync's Pulse colour and the
/// quick panel): a 40px tile with 12px corners holding a round 24px swatch. The tile is a translucent surface
/// with a subtle 1px stroke. Picked, the tile turns to the card surface with a 2px ring in the swatch's colour,
/// a soft shadow in that colour and a glow on the swatch.
/// </summary>
public sealed partial class SwatchTile : Button
{
    readonly Border shadow, tile, ring, hover;
    readonly Ellipse glow;

    public Color Colour { get; }

    public SwatchTile(string name, Color colour, UIElement? face = null)
    {
        Colour = colour;
        Style = (Style)Application.Current.Resources["BareButton"];
        Height = 40;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;

        shadow = Glow.Shadow();
        shadow.Background = Glow.Soft(colour, 0.33);
        tile = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1) };
        hover = new Border { CornerRadius = new CornerRadius(12), Background = Paint.Brush(0x808080, 0x14), Opacity = 0 };
        ring = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), BorderBrush = new SolidColorBrush(colour), Opacity = 0 };
        glow = new Ellipse { Width = 44, Height = 44, Margin = new Thickness(-10), Opacity = 0, IsHitTestVisible = false, Fill = Glow.Soft(colour, 0.7) };
        var swatch = face ?? new Ellipse
        {
            Width = 24, Height = 24, Fill = new SolidColorBrush(colour), Stroke = Paint.Brush(0, 0x1A), StrokeThickness = 1,
        };
        Content = new Grid { Children = { shadow, tile, hover, ring, glow, swatch } };
        AutomationProperties.SetName(this, name);
        ToolTipService.SetToolTip(this, name);
        PointerEntered += (_, _) => hover.Opacity = 1;
        PointerExited += (_, _) => hover.Opacity = 0;
    }

    /// <summary>surface: the card the swatches sit on (the picked tile takes its colour).</summary>
    public void Show(Palette p, bool picked, Color? surface = null)
    {
        tile.Background = Palette.B(picked ? surface ?? p.Card : p.Tile);
        tile.BorderBrush = Palette.B(picked ? Paint.Hex(0, 0) : p.TileStroke);
        ring.Opacity = shadow.Opacity = glow.Opacity = picked ? 1 : 0;
        AutomationProperties.SetItemStatus(this, picked ? "Selected" : "");
    }

    /// <summary>The Custom tile's face: a small rainbow swatch with a white + in it.</summary>
    public static UIElement RainbowFace()
    {
        // A conic rainbow, as thin pie slices (XAML has no conic gradient).
        uint[] stops = [0xE8453C, 0xF5B12E, 0x9BCB4A, 0x2BB5A8, 0x3E6FE0, 0xA85BD8, 0xE05B9E, 0xE8453C];
        var wheel = new Canvas { Width = 24, Height = 24 };
        const int slices = 72;
        for (int i = 0; i < slices; i++)
        {
            double t = (i + 0.5) / slices, seg = t * (stops.Length - 1);
            int k = Math.Min(stops.Length - 2, (int)seg);
            var c = Paint.Mix(Paint.Hex(stops[k]), Paint.Hex(stops[k + 1]), seg - k);
            // Each slice runs a little past the next one so no seams show.
            double a0 = (i / (double)slices) * 2 * Math.PI - Math.PI / 2, a1 = ((i + 1.6) / slices) * 2 * Math.PI - Math.PI / 2;
            var p0 = new Point(12 + 12 * Math.Cos(a0), 12 + 12 * Math.Sin(a0));
            var p1 = new Point(12 + 12 * Math.Cos(a1), 12 + 12 * Math.Sin(a1));
            var fig = new PathFigure { StartPoint = new Point(12, 12), IsClosed = true, IsFilled = true };
            fig.Segments.Add(new LineSegment { Point = p0 });
            fig.Segments.Add(new ArcSegment { Point = p1, Size = new Size(12, 12), SweepDirection = SweepDirection.Clockwise });
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            wheel.Children.Add(new Path { Data = geo, Fill = new SolidColorBrush(c) });
        }
        wheel.Children.Add(new Ellipse { Width = 24, Height = 24, Stroke = Paint.Brush(0, 0x1A), StrokeThickness = 1 });
        var plus = Draw.Shape("M6 2v8M2 6h8", null, Paint.Brush(0x1B1B1B), 2, round: true);
        var badge = new Grid
        {
            Width = 12, Height = 12,
            Children =
            {
                new Ellipse { Fill = Paint.Brush(0xFFFFFF) },
                new Viewbox { Width = 8, Height = 8, Child = new Canvas { Width = 12, Height = 12, Children = { plus } } },
            },
        };
        return new Grid { Width = 24, Height = 24, Children = { wheel, badge } };
    }
}

/// <summary>
/// A tile button with 12px corners: an icon over a label (or beside it), or a number and a short label.
/// It sits on the translucent tile surface and, picked, fills with its own colour.
/// </summary>
public sealed partial class ChipTile : Button
{
    readonly Border shadow, face, hover;
    readonly TextBlock label;
    readonly TextBlock? number;
    readonly Path[] icon = [];
    Color colour;

    public ChipTile(string text, string? iconData, Color colour, bool row = false, double height = 52, double labelSize = 11, string? num = null)
    {
        Style = (Style)Application.Current.Resources["BareButton"];
        Height = height;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        shadow = Glow.Shadow();
        label = new TextBlock
        {
            Text = text, FontSize = labelSize, TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var content = new StackPanel
        {
            Orientation = row ? Orientation.Horizontal : Orientation.Vertical, Spacing = row ? (num != null ? 4 : 8) : 3,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 0, 6, 0),
        };
        if (iconData != null)
        {
            var (box, strokes) = Draw.LineIcon(iconData, 18);
            box.HorizontalAlignment = HorizontalAlignment.Center;
            box.VerticalAlignment = VerticalAlignment.Center;
            icon = strokes;
            content.Children.Add(box);
        }
        if (num != null)
        {
            number = new TextBlock { Text = num, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(number);
        }
        content.Children.Add(label);
        face = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = content };
        hover = new Border { CornerRadius = new CornerRadius(12), Background = Paint.Brush(0x808080, 0x14), Opacity = 0, IsHitTestVisible = false };
        Content = new Grid { Children = { shadow, face, hover } };
        Colour = colour;
        AutomationProperties.SetName(this, text);
        ToolTipService.SetToolTip(this, text);
        PointerEntered += (_, _) => hover.Opacity = 1;
        PointerExited += (_, _) => hover.Opacity = 0;
    }

    /// <summary>The colour the tile fills with when picked.</summary>
    public Color Colour
    {
        get => colour;
        set
        {
            colour = value;
            shadow.Background = Glow.Soft(value, 0.33);
        }
    }

    public string Label
    {
        get => label.Text;
        set => label.Text = value;
    }

    public void Show(Palette p, bool picked, bool enabled = true, Color? pickedText = null)
    {
        var fg = picked ? pickedText ?? Paint.Hex(0xFFFFFF) : p.Text;
        face.Background = Palette.B(picked ? colour : p.Tile);
        face.BorderBrush = Palette.B(picked ? colour : p.TileStroke);
        shadow.Opacity = picked ? 1 : 0;
        label.Foreground = Palette.B(fg);
        if (number != null) number.Foreground = Palette.B(fg);
        Draw.Tint(icon, fg);
        IsEnabled = enabled;
        Opacity = enabled ? 1 : 0.45;
        AutomationProperties.SetItemStatus(this, picked ? "Selected" : "");
    }
}
