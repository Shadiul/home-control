using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace HomeControl.Art;

public sealed partial class AcFrontArt : UserControl
{
    bool flowing;

    public AcFrontArt()
    {
        InitializeComponent();
        Show(false, null, false);
        Unloaded += (_, _) => { Flow.Stop(); flowing = false; };
        Loaded += (_, _) => { if (Air.Visibility == Visibility.Visible) StartFlow(); };
    }

    /// <summary>on: running. target: shown on the display. warm: heating (warm air instead of cool).</summary>
    public void Show(bool on, int? target, bool warm)
    {
        Display.Text = on && target is { } t ? $"{t}°" : "--";
        Display.Foreground = new SolidColorBrush(on ? (warm ? Paint.Hex(0xFFB27A) : Paint.Hex(0x6FD3FF)) : Paint.Hex(0x55606B));
        Led.Fill = new SolidColorBrush(on ? Paint.Hex(0x3DD68C) : Paint.Hex(0x5A6068));
        Canvas.SetTop(Blade, on ? 64 : 66);
        Blade.Height = on ? 7 : 3;
        var air = warm ? Paint.Hex(0xFFD2B0) : Paint.Hex(0xCFEAFF);
        AirBrush.GradientStops[0].Color = Paint.WithAlpha(air, 0.75);
        AirBrush.GradientStops[1].Color = Paint.WithAlpha(air, 0);
        Air.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (on) StartFlow();
        else if (flowing)
        {
            Flow.Stop();
            flowing = false;
        }
    }

    void StartFlow()
    {
        if (flowing || !IsLoaded) return;
        Flow.Begin();
        flowing = true;
    }
}
