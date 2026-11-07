using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace LedControl.Art;

public sealed partial class SpeakerArt : UserControl
{
    static readonly double[] BaseOpacity = [0.78, 0.56, 0.34];

    public SpeakerArt()
    {
        InitializeComponent();
        Show(false);
    }

    public void Show(bool running)
    {
        Waves.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        Led.Fill = new SolidColorBrush(running ? Paint.Hex(0x3DD68C) : Paint.Hex(0x5A6068));
        if (!running) Pulse(0, 0);
    }

    /// <summary>Follows the music: level and bass 0-1. The waves brighten with the level and the woofer pushes out on bass.</summary>
    public void Pulse(double level, double bass)
    {
        double k = 0.55 + 0.45 * Math.Clamp(level, 0, 1);
        Path[] right = [WaveR1, WaveR2, WaveR3], left = [WaveL1, WaveL2, WaveL3];
        for (int i = 0; i < 3; i++) right[i].Opacity = left[i].Opacity = BaseOpacity[i] * k;
        WooferScale.ScaleX = WooferScale.ScaleY = 1 + 0.08 * Math.Clamp(bass, 0, 1);
    }
}
