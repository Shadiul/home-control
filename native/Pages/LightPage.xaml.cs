using LedControl.Art;
using LedControl.Controls;
using LedControl.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace LedControl.Pages;

/// <summary>
/// Light: a realistic bulb that glows in the light's colour (click it to turn the light on or off), mode,
/// brightness and warmth or colour in one grouped card, and illustrated scene cards.
/// </summary>
public sealed partial class LightPage : Page
{
    // Colour swatches: name and the colour as shown on screen (hue and saturation are sent to the light).
    static readonly (string Name, uint Rgb)[] SwatchColours =
    [
        ("Red", 0xE8453C), ("Orange", 0xF2802E), ("Amber", 0xF5B12E), ("Yellow", 0xF2D33C),
        ("Lime", 0x9BCB4A), ("Green", 0x3DAA67), ("Teal", 0x2BB5A8), ("Sky", 0x3A9EE8),
        ("Blue", 0x3E6FE0), ("Violet", 0x7A5BE0), ("Purple", 0xA85BD8), ("Pink", 0xE05B9E),
    ];

    // Scene illustrations on an 80 × 60 grid: the pool of light in the scene's colour, faint room details,
    // the main objects, cords and lamp arms, and small lit details. Plus the scene's colour and the room's.
    static readonly Dictionary<string, (uint Colour, uint Room, string Light, string Soft, string Fill, string Line, string Accent)> SceneArt = new()
    {
        ["Bright"] = (0xF4F7FF, 0xC9D3E6, "M31 17 L10 55 H70 L49 17 Z", "M4 55h72v2H4z M28 46h24v2H28z M31 48h2v7h-2z M47 48h2v7h-2z",
            "M29 17 Q40 5 51 17 Z M22 41h8v5h-8z M50 41h8v5h-8z", "M40 0V9", "M37 18a3 3 0 1 0 6 0a3 3 0 1 0-6 0z"),
        ["Reading"] = (0xFFE6BF, 0xB89A74, "M57 25 L18 50 H54 Z", "M4 55h72v2H4z",
            "M10 47 Q20 42 31 47 V54 Q20 49 10 54Z M31 47 Q42 42 52 47 V54 Q42 49 31 54Z M60 53h12v2H60z M56 15l11 5-3 6-11-5z",
            "M66 53 L59 36 L63 24", "M54.5 22.5a2.5 2.5 0 1 0 5 0a2.5 2.5 0 1 0-5 0z"),
        ["Relax"] = (0xFFC27A, 0x7A5634, "M57 21 L44 56 H80 L71 21 Z", "M2 56h76v2H2z M16 46h22v4H16z",
            "M14 38a6 6 0 0 1 6-6h14a6 6 0 0 1 6 6v8H14z M9 40h6v13H9z M39 40h6v13h-6z M14 46h26v7H14z M57 12h14l3 9H54z M60 55h8v2h-8z",
            "M64 21V55 M12 53v3 M42 53v3", "M62 21h4v2h-4z"),
        ["Night"] = (0xF2802E, 0x2E1C12, "M64 38 L56 52 H74 Z", "M6 44h56v8H6z M6 34h8v18H6z M18 38h14v6H18z",
            "M54 4a10 10 0 1 0 10 14a8 8 0 1 1-10-14z M22 7l1.4 3 3 1.4-3 1.4-1.4 3-1.4-3-3-1.4 3-1.4z M36 17l1 2 2 1-2 1-1 2-1-2-2-1 2-1z M8 52h3v4H8z M57 52h3v4h-3z",
            "M4 56h72", "M61 33a3 3 0 1 0 6 0a3 3 0 1 0-6 0z"),
        ["Movie"] = (0x3E6FE0, 0x141B33, "M17 8h46v27H17z", "M14 5h52v33H14z",
            "M37 15v13l11-6.5z M36 38h8v3h-8z M29 41h22v2H29z M8 50a6 6 0 0 1 6-6h52a6 6 0 0 1 6 6v8H8z", "", ""),
        ["Party"] = (0xA85BD8, 0x3A1F52, "M40 18 L4 57 H17 Z M40 18 L33 57 H47 Z M40 18 L63 57 H76 Z", "M30 18a10 10 0 1 0 20 0a10 10 0 1 0-20 0z",
            "M12 10h3v3h-3z M66 8h3v3h-3z M61 30h2v4h-2z M17 31h3v2h-3z M8 22h2v3H8z M71 21h3v2h-3z",
            "M40 0V8 M30.5 15h19 M30.5 21h19 M35 8.8v18.4 M45 8.8v18.4", "M26 40h3v3h-3z M52 44h3v3h-3z M40 48h3v3h-3z"),
    };

    readonly BigBulb bulb = new();
    readonly List<(Button Button, Border Ring, int H, int S)> swatchButtons = [];
    readonly List<(Button Button, Border Ring, Scene Scene)> sceneButtons = [];
    readonly SolidColorBrush warmthThumb = new(Paint.Hex(0xF2A541));
    bool syncing;
    int lastH = 30, lastS = 100;

    static LightSession Session => App.Instance!.Session;

    public LightPage()
    {
        syncing = true; // controls raise change events while the XAML loads
        InitializeComponent();
        BulbBox.Child = bulb;
        StyleWarmthSlider();
        foreach (var (name, rgb) in SwatchColours) SwatchGrid.Children.Add(SwatchButton(name, rgb));
        foreach (var scene in LightSession.Scenes) SceneGrid.Children.Add(SceneButton(scene));
        syncing = false;

        Session.Changed += Sync;
        Session.MusicChanged += Sync;
        ActualThemeChanged += (_, _) => Sync();
        Loaded += (_, _) => Sync();
    }

    public static Color ToColor((byte R, byte G, byte B) c) => Color.FromArgb(255, c.R, c.G, c.B);

    // ------------------------------------------------------------ building

    /// <summary>The warmth slider's track runs from warm to cool white, and its thumb shows the colour chosen.</summary>
    void StyleWarmthSlider()
    {
        var gradient = Draw.Gradient(new(0, 0), new(1, 0), (0, Paint.Hex(0xF2A541)), (0.55, Paint.Hex(0xFCEBD2)), (1, Paint.Hex(0xC9DDFB)));
        var none = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var state in new[] { "", "PointerOver", "Pressed", "Disabled" })
        {
            TempSlider.Resources["SliderTrackFill" + state] = gradient;
            TempSlider.Resources["SliderTrackValueFill" + state] = none;
        }
        foreach (var state in new[] { "", "PointerOver", "Pressed" }) TempSlider.Resources["SliderThumbBackground" + state] = warmthThumb;
        TempSlider.Background = gradient;
        TempSlider.Foreground = none;
    }

    UIElement SwatchButton(string name, uint rgb)
    {
        var c = Paint.Hex(rgb);
        var (h, s, _) = ColorMath.RgbToHsv(c.R, c.G, c.B);
        var ring = new Border
        {
            CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(2), Padding = new Thickness(3),
            Child = new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(c) },
        };
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["BareButton"], Height = 40, CornerRadius = new CornerRadius(4),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch, Content = ring,
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, name);
        int hue = (int)Math.Round(h) % 360, sat = (int)Math.Round(s * 100);
        button.Click += (_, _) =>
        {
            lastH = hue;
            lastS = sat;
            Session.Submit(LightCommand.Colour(hue, sat, CurrentLevel()));
        };
        swatchButtons.Add((button, ring, hue, sat));
        return button;
    }

    UIElement SceneButton(Scene scene)
    {
        var (colourHex, roomHex, light, soft, fill, line, accent) = SceneArt.TryGetValue(scene.Name, out var a)
            ? a : (0xFFFFFFu, 0x333333u, "", "", "", "", "");
        var c = Paint.Hex(colourHex);
        var art = new Canvas { Width = 80, Height = 60 };
        void Add(string data, Brush? f, Brush? stroke = null)
        {
            if (string.IsNullOrWhiteSpace(data)) return;
            var p = Draw.Shape(data, f, stroke, 2, round: true);
            p.StrokeLineJoin = PenLineJoin.Round;
            art.Children.Add(p);
        }
        Add(light, new SolidColorBrush(Paint.WithAlpha(c, 0.55)));
        Add(soft, Palette.B(Paint.Hex(0xFFFFFF, 0x52)));
        Add(fill, Palette.B(Paint.Hex(0xFFFFFF, 0xEB)));
        Add(line, null, Palette.B(Paint.Hex(0xFFFFFF, 0xD9)));
        Add(accent, new SolidColorBrush(c));

        var c2 = scene.Command;
        string detail = c2.Kind == CommandKind.White
            ? $"{WhiteName(c2.Temp)} · {c2.Brightness}%"
            : $"{ColorMath.HueName(c2.H, c2.S)} · {c2.V}%";
        var hover = new Border { Background = Palette.B(Paint.Hex(0xFFFFFF, 0x14)), Opacity = 0 };
        var face = new Grid
        {
            Height = 144, CornerRadius = new CornerRadius(10),
            Children =
            {
                new Border { Background = Draw.Vertical((0, Paint.Hex(roomHex)), (1, Paint.Hex(0x121214))) },
                new Border { Background = new RadialGradientBrush
                {
                    Center = new(0.5, 0.32), GradientOrigin = new(0.5, 0.32), RadiusX = 0.9, RadiusY = 0.75,
                    GradientStops =
                    {
                        new GradientStop { Offset = 0, Color = Paint.WithAlpha(c, 0.8) },
                        new GradientStop { Offset = 0.35, Color = Paint.WithAlpha(c, 0.33) },
                        new GradientStop { Offset = 0.7, Color = Paint.WithAlpha(c, 0) },
                    },
                } },
                hover,
                new Viewbox { Width = 104, Height = 78, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0), Child = art },
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(12, 22, 12, 10),
                    Background = Draw.Vertical((0, Paint.Hex(0, 0)), (1, Paint.Hex(0, 0x99))),
                    Children =
                    {
                        new TextBlock { Text = scene.Name, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Palette.B(Paint.Hex(0xFFFFFF)) },
                        new TextBlock { Text = detail, FontSize = 12, Foreground = Palette.B(Paint.Hex(0xFFFFFF, 0xD9)), TextTrimming = TextTrimming.CharacterEllipsis },
                    },
                },
            },
        };
        var ring = new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(2), Padding = new Thickness(2), Child = face };
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["BareButton"], CornerRadius = new CornerRadius(12),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = ring,
        };
        AutomationProperties.SetName(button, $"{scene.Name}: {detail}");
        button.PointerEntered += (_, _) => hover.Opacity = 1;
        button.PointerExited += (_, _) => hover.Opacity = 0;
        button.Click += (_, _) => Session.Submit(scene.Command);
        sceneButtons.Add((button, ring, scene));
        return button;
    }

    static string WhiteName(int temp) => temp < 35 ? "Warm white" : temp < 70 ? "Neutral white" : "Cool white";

    // ------------------------------------------------------------ showing the light

    int CurrentLevel() => (int)BrightnessSlider.Value;

    void Sync()
    {
        syncing = true;
        try
        {
            var p = Palette.For(this);
            var cfg = App.Instance!.Config;
            string name = string.IsNullOrWhiteSpace(cfg.Device.Name) ? "Light" : cfg.Device.Name;
            string kind = Session.StatusKind;
            var st = Session.Shown;
            StatusDot.Fill = Palette.B(kind switch
            {
                "ok" => p.Dark ? Paint.Hex(0x6CCB5F) : Paint.Hex(0x0F7B0F),
                "error" => p.Dark ? Paint.Hex(0xFF99A4) : Paint.Hex(0xC42B1C),
                "busy" => p.Dark ? Paint.Hex(0xFCE100) : Paint.Hex(0x9D5D00),
                _ => p.Text3,
            });
            StatusText.Text = kind == "ok" && st != null
                ? $"{name} · {(st.Via == "cloud" ? "Tuya Cloud" : "Home network")}"
                : kind == "error" ? $"{name} · not connected" : Session.StatusText;
            ErrorBar.IsOpen = kind == "error";
            ErrorBar.Message = Session.StatusText;
            ErrorBar.Visibility = ErrorBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            SetupBar.IsOpen = kind == "setup";
            SetupBar.Visibility = SetupBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;

            bool known = st != null, on = st?.On == true, colour = st?.IsColour == true;
            PowerSwitch.IsEnabled = known;
            PowerSwitch.IsOn = on;
            VisualStateManager.GoToState(PowerSwitch, on ? "On" : "Off", false); // after a theme change it can keep showing the old state
            PowerText.Text = !known ? "" : on ? "On" : "Off";
            foreach (var c in new Control[] { WhiteButton, ColourButton, BrightnessSlider, TempSlider, PreviewButton }) c.IsEnabled = known;

            int level = st == null ? 0 : colour ? st.V : st.Brightness;
            var lightColour = st != null ? ToColor(st.Rgb) : Paint.Hex(0xFFC47E);
            bulb.Show(on, lightColour, level / 100.0);
            string shade = st == null ? "" : colour ? ColorMath.HueName(st.H, st.S) : WhiteName(st.Temp);
            Headline.Text = st == null ? (kind == "busy" ? "Connecting…" : "Unknown")
                : Session.MusicRunning ? "Music sync is running"
                : on ? $"{shade} · {level}%" : "Off";
            AutomationProperties.SetName(bulb, on ? $"Light bulb glowing {shade.ToLowerInvariant()} at {level}%" : "Light bulb, off");

            // Dim backdrop so the glow reads; when off, a plain neutral surface.
            Stage.Background = Palette.B(on ? (p.Dark ? Paint.Hex(0x1A1A1C) : Paint.Hex(0x2A2A2D)) : (p.Dark ? Paint.Hex(0x323232) : Paint.Hex(0xE9E9E9)));
            StageGlow.Background = on
                ? Draw.Radial(new(0.5, 0.38), 0.7, 0.7, (0, Paint.WithAlpha(lightColour, 0.33)), (0.38, Paint.WithAlpha(lightColour, 0.09)), (0.7, Paint.WithAlpha(lightColour, 0)))
                : null;

            // Mode, brightness, warmth or colour
            WhiteButton.IsChecked = known && !colour;
            ColourButton.IsChecked = known && colour;
            ColourButton.Visibility = st?.HasColour == false ? Visibility.Collapsed : Visibility.Visible;
            WhitePanel.Visibility = colour || st?.HasTemp == false ? Visibility.Collapsed : Visibility.Visible;
            ColourPanel.Visibility = colour ? Visibility.Visible : Visibility.Collapsed;
            if (st != null)
            {
                BrightnessSlider.Value = Math.Max(1, level);
                TempSlider.Value = st.Temp;
                if (colour)
                {
                    lastH = st.H;
                    lastS = st.S;
                }
            }
            BrightnessText.Text = known ? $"{Math.Max(1, level)}%" : "";
            int temp = st?.Temp ?? 0;
            WarmthText.Text = temp < 35 ? "Warm" : temp < 70 ? "Neutral" : "Cool";
            warmthThumb.Color = Paint.Mix(Paint.Hex(0xF2A541), Paint.Hex(0x9DBFF5), temp / 100.0);

            foreach (var (button, ring, h, s) in swatchButtons)
            {
                bool picked = colour && Math.Min(Math.Abs(h - st!.H), 360 - Math.Abs(h - st.H)) <= 4 && Math.Abs(s - st.S) <= 6;
                ring.BorderBrush = Palette.B(picked ? p.Accent : Paint.Hex(0, 0));
                AutomationProperties.SetItemStatus(button, picked ? "Selected" : "");
            }

            // Scenes: the one the light matches gets a ring.
            foreach (var (button, ring, scene) in sceneButtons)
            {
                var c = scene.Command;
                bool active = on && st != null && (c.Kind == CommandKind.White
                    ? !colour && st.Brightness == c.Brightness && st.Temp == c.Temp
                    : colour && st.H == c.H && st.S == c.S && st.V == c.V);
                ring.BorderBrush = Palette.B(active ? p.Accent : Paint.Hex(0, 0));
                AutomationProperties.SetItemStatus(button, active ? "Active" : "");
            }
        }
        finally
        {
            syncing = false;
        }
    }

    // ------------------------------------------------------------ changes

    void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (Session.Shown != null) Session.TogglePower();
    }

    void Power_Toggled(object sender, RoutedEventArgs e)
    {
        // Only when it differs from the light: the switch can report a change made by Sync a moment later.
        if (!syncing && Session.Shown is { } st && PowerSwitch.IsOn != st.On) Session.Submit(LightCommand.Power(PowerSwitch.IsOn));
    }

    void White_Click(object sender, RoutedEventArgs e) =>
        Session.Submit(LightCommand.White(CurrentLevel(), (int)TempSlider.Value));

    void Colour_Click(object sender, RoutedEventArgs e) =>
        Session.Submit(LightCommand.Colour(lastH, lastS, CurrentLevel()));

    void Brightness_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (syncing) return;
        var st = Session.Shown;
        Session.Submit(st?.IsColour == true
            ? LightCommand.Colour(lastH, lastS, CurrentLevel())
            : LightCommand.White(CurrentLevel(), (int)TempSlider.Value));
    }

    void Temp_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!syncing) Session.Submit(LightCommand.White(CurrentLevel(), (int)TempSlider.Value));
    }

    void PickerFlyout_Opening(object sender, object e)
    {
        syncing = true;
        Picker.Color = ToColor(ColorMath.HsvToRgb(lastH, lastS / 100.0, 1));
        syncing = false;
    }

    void Picker_ColorChanged(ColorPicker sender, ColorChangedEventArgs args)
    {
        if (syncing) return;
        var (h, s, _) = ColorMath.RgbToHsv(args.NewColor.R, args.NewColor.G, args.NewColor.B);
        lastH = (int)Math.Round(h) % 360;
        lastS = (int)Math.Round(s * 100);
        Session.Submit(LightCommand.Colour(lastH, lastS, CurrentLevel()));
    }

    void Retry_Click(object sender, RoutedEventArgs e)
    {
        App.Instance!.Controller.Reset();
        _ = Session.RefreshAsync();
    }

    void OpenSettings_Click(object sender, RoutedEventArgs e) => App.Instance!.Window.ShowPage("settings");
}
