using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LedControl.Art;

public sealed partial class HouseArt : UserControl
{
    public HouseArt() => InitializeComponent();

    public void Show(bool night, bool lightOn, Color lightColour, double level, bool acOn)
    {
        Moon.Visibility = Stars.Visibility = night ? Visibility.Visible : Visibility.Collapsed;
        Sun.Visibility = night ? Visibility.Collapsed : Visibility.Visible;
        Glow.Fill = lightOn ? Draw.Radial((0, Paint.WithAlpha(lightColour, 0.25 + 0.5 * level)), (1, Paint.WithAlpha(lightColour, 0))) : null;
        Window.Fill = new SolidColorBrush(lightOn ? lightColour : Paint.Hex(0x22375E));
        AcAir.Visibility = acOn ? Visibility.Visible : Visibility.Collapsed;
    }
}
