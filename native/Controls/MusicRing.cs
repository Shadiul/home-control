using LedControl.Art;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace LedControl.Controls;

/// <summary>
/// A ring of bars around a round play/stop button. While music sync runs the bars follow the music's spectrum
/// (bass at the top, mirrored left and right); when stopped they sit short and grey.
/// </summary>
public sealed partial class MusicRing : UserControl
{
    const int Rays = 64;
    const double Cx = 105, Cy = 100, R0 = 58;
    static readonly uint[] RayColours = [0xB48CFF, 0xD2B8FF, 0x7FD6FF];

    readonly Line[] rays = new Line[Rays];
    readonly SolidColorBrush[] colourBrushes = RayColours.Select(c => Paint.Brush(c)).ToArray();
    readonly SolidColorBrush idle = Paint.Brush(0xFFFFFF, 0x2E);
    readonly FontIcon playIcon;
    bool running;

    public Button PlayButton { get; }

    public MusicRing()
    {
        var canvas = new Canvas { Width = 210, Height = 196 };
        canvas.Children.Add(new Ellipse
        {
            Width = 104, Height = 104, Stroke = Paint.Brush(0xFFFFFF, 0x2E), StrokeThickness = 2,
            Margin = new Thickness(Cx - 52, Cy - 52, 0, 0),
        });
        for (int k = 0; k < Rays; k++)
        {
            rays[k] = new Line { StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            canvas.Children.Add(rays[k]);
        }
        playIcon = new FontIcon { Glyph = "", FontSize = 24 };
        PlayButton = new Button
        {
            Width = 72, Height = 72, CornerRadius = new CornerRadius(36), Padding = new Thickness(0),
            BorderThickness = new Thickness(0), Foreground = Paint.Brush(0xFFFFFF), Content = playIcon,
            Margin = new Thickness(Cx - 36, Cy - 36, 0, 0),
        };
        canvas.Children.Add(PlayButton);
        Content = canvas;
        Width = 210;
        Height = 196;
        Show(false);
    }

    public void Show(bool running)
    {
        this.running = running;
        playIcon.Glyph = running ? "" : ""; // stop : play
        PlayButton.Background = running ? Paint.Brush(0xFFFFFF, 0x29) : Paint.Brush(0x8A5BE0);
        AutomationProperties.SetName(PlayButton, running ? "Stop music sync" : "Start music sync");
        ToolTipService.SetToolTip(PlayButton, running ? "Stop music sync" : "Start music sync");
        if (!running) Draw(null);
    }

    /// <summary>Bars 0-1 from the music analyser (low to high frequencies).</summary>
    public void Draw(float[]? bars)
    {
        for (int k = 0; k < Rays; k++)
        {
            double len = 4;
            if (running && bars is { Length: > 0 })
            {
                // Mirror: bass at the top, highest notes at the bottom, the same on both sides.
                int half = Rays / 2, i = k < half ? k : Rays - 1 - k;
                int bar = Math.Min(bars.Length - 1, i * bars.Length / half);
                len = 4 + 22 * Math.Clamp(bars[bar], 0, 1);
            }
            double a = (double)k / Rays * Math.PI * 2 - Math.PI / 2;
            var line = rays[k];
            line.X1 = Cx + R0 * Math.Cos(a);
            line.Y1 = Cy + R0 * Math.Sin(a);
            line.X2 = Cx + (R0 + len) * Math.Cos(a);
            line.Y2 = Cy + (R0 + len) * Math.Sin(a);
            line.Stroke = running ? colourBrushes[k % colourBrushes.Length] : idle;
        }
    }
}
