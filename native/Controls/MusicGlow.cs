using LedControl.Art;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace LedControl.Controls;

/// <summary>
/// The Home page's music sync control: a round play / stop button sitting on a soft violet glow that
/// breathes while music sync runs (no visualizer). Drawn on a 210 × 196 canvas.
/// </summary>
public sealed partial class MusicGlow : UserControl
{
    const double Cx = 105, Cy = 100;
    readonly Ellipse glow;
    readonly Border face;
    readonly Path icon;
    readonly Storyboard breathing = new();
    bool running;

    public Button PlayButton { get; }

    public MusicGlow()
    {
        var canvas = new Canvas { Width = 210, Height = 196 };
        // A blurred violet disc (radius about 56) that swells from 48 to 62 and back every 2.6 seconds.
        glow = Soft.Disc(Cx, Cy, 56, 18, Paint.Hex(0x8A5BE0));
        Soft.Breathe(breathing, glow, 48 / 56.0, 62 / 56.0, 2.6);
        canvas.Children.Add(glow);

        icon = new Path { Fill = Paint.Brush(0xFFFFFF), Width = 24, Height = 24 };
        face = new Border { CornerRadius = new CornerRadius(36), Child = new Viewbox { Width = 26, Height = 26, Child = icon } };
        var shadow = Soft.Disc(36, 42, 34, 10, Paint.Hex(0), 0.22);
        shadow.Margin = new Thickness(-20, -14, -20, -26);
        PlayButton = new Button
        {
            Style = (Style)Application.Current.Resources["BareButton"], Width = 72, Height = 72, CornerRadius = new CornerRadius(36),
            Content = new Grid { Children = { shadow, face } },
        };
        canvas.Children.Add(Draw.At(PlayButton, Cx - 36, Cy - 36));
        Content = canvas;
        Width = 210;
        Height = 196;
        Loaded += (_, _) =>
        {
            if (running) breathing.Begin();
        };
        Unloaded += (_, _) => breathing.Stop();
        Show(false);
    }

    public void Show(bool running)
    {
        icon.Data = Draw.Geo(running ? "M6 5h4v14H6zM14 5h4v14h-4z" : "M8 5v14l11-7z");
        face.Background = running ? Paint.Brush(0xFFFFFF, 0x29) : Paint.Brush(0x8A5BE0);
        glow.Opacity = running ? 0.6 : 0.18;
        string label = running ? "Stop music sync" : "Start music sync";
        AutomationProperties.SetName(PlayButton, label);
        ToolTipService.SetToolTip(PlayButton, label);
        if (running == this.running) return;
        this.running = running;
        if (running && IsLoaded) breathing.Begin();
        else if (!running) breathing.Stop();
    }
}
