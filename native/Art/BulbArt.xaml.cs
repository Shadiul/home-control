using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LedControl.Art;

public sealed partial class BulbArt : UserControl
{
    public BulbArt()
    {
        InitializeComponent();
        Show(false, Paint.Off, 0);
    }

    /// <summary>Lights the bulb: colour is the light's colour, level 0-1 its brightness.</summary>
    public void Show(bool on, Color colour, double level)
    {
        if (!on)
        {
            HaloInner.Color = HaloOuter.Color = Color.FromArgb(0, 0, 0, 0);
            Glass2.Color = Paint.Hex(0xE6E6E6);
            Glass3.Color = Paint.Hex(0xBDBDBD);
            Glass.Stroke = new SolidColorBrush(Paint.Hex(0x9A9A9A));
            Filament.Stroke = new SolidColorBrush(Paint.Hex(0xA89C86));
            return;
        }
        level = Math.Clamp(level, 0, 1);
        double r = 44 + 22 * level;
        Halo.Width = Halo.Height = r * 2;
        Microsoft.UI.Xaml.Controls.Canvas.SetLeft(Halo, 150 - r);
        Microsoft.UI.Xaml.Controls.Canvas.SetTop(Halo, 52 - r);
        HaloInner.Color = Paint.WithAlpha(colour, 0.45 + 0.5 * level);
        HaloOuter.Color = Paint.WithAlpha(colour, 0);
        Glass2.Color = Paint.Mix(Microsoft.UI.Colors.White, colour, 0.45);
        Glass3.Color = colour;
        Glass.Stroke = new SolidColorBrush(Paint.Mix(colour, Microsoft.UI.Colors.White, 0.4));
        Filament.Stroke = new SolidColorBrush(Paint.Mix(Microsoft.UI.Colors.White, colour, 0.3));
    }
}

/// <summary>Small colour helpers for the illustrations.</summary>
public static class Paint
{
    public static readonly Color Off = Color.FromArgb(255, 0, 0, 0);

    public static Color Hex(uint rgb, byte a = 255) => Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Round(Math.Clamp(alpha, 0, 1) * 255), c.R, c.G, c.B);

    /// <summary>From a (t = 0) to b (t = 1).</summary>
    public static Color Mix(Color a, Color b, double t) => Color.FromArgb(
        (byte)Math.Round(a.A + (b.A - a.A) * t), (byte)Math.Round(a.R + (b.R - a.R) * t),
        (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));

    public static SolidColorBrush Brush(uint rgb, byte a = 255) => new(Hex(rgb, a));
}
