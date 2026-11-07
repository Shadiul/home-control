using LedControl.Art;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LedControl.Controls;

/// <summary>
/// Windows 11 colours for drawings and controls built in code, for the element's current theme
/// (the app can be set to light or dark separately from Windows). The accent follows the Windows accent colour.
/// </summary>
public sealed record Palette(
    bool Dark, Color Card, Color CardStroke, Color Ctrl, Color CtrlStroke, Color Text, Color Text2, Color Text3,
    Color Accent, Color OnAccent, Color Track, Color Divider, Color ThumbRing, Color SwitchOff, Color Well)
{
    public static Palette For(FrameworkElement element) => For(element.ActualTheme == ElementTheme.Dark);

    public static Palette For(bool dark)
    {
        var accent = SystemColour(dark ? "SystemAccentColorLight2" : "SystemAccentColorDark1", dark ? 0x60CDFFu : 0x005FB8u);
        return dark
            ? new(true, Paint.Hex(0x2B2B2B), Paint.Hex(0x383838), Paint.Hex(0x2D2D2D), Paint.Hex(0x454545),
                  Paint.Hex(0xFFFFFF), Paint.Hex(0xC8C8C8), Paint.Hex(0xA8A8A8), accent, Paint.Hex(0x000000),
                  Paint.Hex(0x454545), Paint.Hex(0x363636), Paint.Hex(0x454545), Paint.Hex(0xC8C8C8), Paint.Hex(0xFFFFFF, 0x0A))
            : new(false, Paint.Hex(0xFBFBFB), Paint.Hex(0xE5E5E5), Paint.Hex(0xFFFFFF), Paint.Hex(0xD6D6D6),
                  Paint.Hex(0x1B1B1B), Paint.Hex(0x5D5D5D), Paint.Hex(0x666666), accent, Paint.Hex(0xFFFFFF),
                  Paint.Hex(0xDCDCDC), Paint.Hex(0xEAEAEA), Paint.Hex(0xFFFFFF), Paint.Hex(0x5D5D5D), Paint.Hex(0x000000, 0x09));
    }

    static Color SystemColour(string key, uint fallback) =>
        Application.Current.Resources.TryGetValue(key, out var v) && v is Color c ? c : Paint.Hex(fallback);

    public static SolidColorBrush B(Color c) => new(c);
}
