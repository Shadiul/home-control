using HomeControl.Art;
using HomeControl.Controls;
using HomeControl.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace HomeControl.Pages;

/// <summary>
/// Light: a realistic bulb that glows in the light's colour (click it to turn the light on or off) beside a
/// controls card lit by the light's colour: White / Colour tiles, thick brightness and warmth sliders, white
/// presets or colour swatches. Then illustrated scene cards.
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

    // White presets: name and warmth (0 warm to 100 cool).
    static readonly (string Name, int Temp)[] Whites = [("Candle", 0), ("Warm", 25), ("Neutral", 50), ("Daylight", 75), ("Cool", 100)];

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

    const string SunIcon = "M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM12 2.5v2M12 19.5v2M4.6 4.6 6 6M18 18l1.4 1.4M2.5 12h2M19.5 12h2M4.6 19.4 6 18M18 6l1.4-1.4";
    const string DimSunIcon = "M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6zM12 5v1M12 18v1M5 12h1M18 12h1";
    const string PaletteIcon = "M12 3a9 9 0 1 0 0 18c1.2 0 1.8-.8 1.8-1.7 0-1.2-1-1.6-1-2.6 0-.9.7-1.5 1.7-1.5H17a4 4 0 0 0 4-4C21 6.6 17 3 12 3zM7.5 11.5h.01M9.5 7.5h.01M14.5 7.5h.01";

    sealed class ModeTile
    {
        public required Button Button;
        public required Border Face, Badge, Shadow;
        public required Path[] Icon;
        public required TextBlock Name, Sub;
    }

    sealed class WhiteTile
    {
        public required Button Button;
        public required Border Face;
        public required Ellipse Glow;
        public required TextBlock Name;
        public required int Temp;
    }

    readonly BigBulb bulb = new();
    readonly ThickSlider brightness = new("Brightness") { Minimum = 1, Maximum = 100, LargeChange = 10 };
    readonly ThickSlider warmth = new("Warmth, from warm to cool white") { Minimum = 0, Maximum = 100, LargeChange = 10 };
    readonly Path[] dimSun, brightSun;
    readonly ModeTile whiteTile, colourTile;
    readonly List<WhiteTile> whiteTiles = [];
    readonly List<(SwatchTile Tile, string Name, int H, int S)> swatchButtons = [];
    readonly ColorPicker picker = new()
    {
        ColorSpectrumShape = ColorSpectrumShape.Box, IsAlphaEnabled = false, IsColorSliderVisible = false,
        IsColorChannelTextInputVisible = false, IsHexInputVisible = true,
    };
    readonly List<(Button Button, Border Ring, Scene Scene)> sceneButtons = [];
    SwatchTile? customTile;
    bool syncing;
    int lastH = 30, lastS = 100;

    static LightSession Session => App.Instance!.Session;

    public LightPage()
    {
        syncing = true; // controls raise change events while the XAML loads
        InitializeComponent();
        BulbBox.Child = bulb;

        whiteTile = BuildModeTile("White", "Warm to cool whites", SunIcon, 0, White_Click);
        colourTile = BuildModeTile("Colour", "Any colour you like", PaletteIcon, 1, Colour_Click);

        var (dimBox, dim) = Draw.LineIcon(DimSunIcon, 16, 1.8);
        var (brightBox, bright) = Draw.LineIcon(SunIcon, 20, 1.8);
        dimSun = dim;
        brightSun = bright;
        dimBox.VerticalAlignment = brightBox.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(brightness, 1);
        Grid.SetColumn(brightBox, 2);
        BrightnessRow.Children.Add(dimBox);
        BrightnessRow.Children.Add(brightness);
        BrightnessRow.Children.Add(brightBox);
        brightness.ValueChanged += _ => Brightness_Changed();
        WarmthHost.Content = warmth;
        warmth.ValueChanged += _ => Temp_Changed();

        foreach (var (name, temp) in Whites) WhiteGrid.Children.Add(BuildWhiteTile(name, temp));
        foreach (var (name, rgb) in SwatchColours) SwatchGrid.Children.Add(SwatchButton(name, rgb));
        SwatchGrid.Children.Add(CustomButton());
        foreach (var scene in LightSession.Scenes) SceneGrid.Children.Add(SceneButton(scene));
        syncing = false;

        Session.Changed += Sync;
        Session.MusicChanged += Sync;
        ActualThemeChanged += (_, _) => Sync();
        Loaded += (_, _) => Sync();
    }

    public static Color ToColor((byte R, byte G, byte B) c) => Color.FromArgb(255, c.R, c.G, c.B);

    // ------------------------------------------------------------ building

    /// <summary>Preview beside the controls when there's room for both (340 + 420), else stacked.</summary>
    void Split_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool wide = e.NewSize.Width >= 776;
        Split.ColumnDefinitions[1].Width = wide ? new GridLength(1.25, GridUnitType.Star) : new GridLength(0);
        Split.ColumnSpacing = wide ? 16 : 0;
        Grid.SetColumn(ControlsCard, wide ? 1 : 0);
        Grid.SetRow(ControlsCard, wide ? 0 : 1);
    }

    /// <summary>A 60px mode tile: an icon badge, the name and a line under it.</summary>
    ModeTile BuildModeTile(string name, string sub, string icon, int column, RoutedEventHandler click)
    {
        var (box, strokes) = Draw.LineIcon(icon, 18, 1.8);
        var badge = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Child = box };
        var title = new TextBlock { Text = name, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var line = new TextBlock { Text = sub, FontSize = 12, Opacity = 0.8, TextTrimming = TextTrimming.CharacterEllipsis };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { title, line } };
        Grid.SetColumn(texts, 1);
        var face = new Border
        {
            Height = 60, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(14, 0, 14, 0),
            Child = new Grid
            {
                ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Center,
                ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
                Children = { badge, texts },
            },
        };
        var shadow = Glow.Shadow(16);
        shadow.Background = Glow.Soft(Paint.Hex(0), 0.18);
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["BareButton"], HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = new Grid { Children = { shadow, face } },
        };
        AutomationProperties.SetName(button, $"{name}: {sub}");
        button.Click += click;
        Grid.SetColumn(button, column);
        ModeTiles.Children.Add(button);
        return new ModeTile { Button = button, Face = face, Badge = badge, Shadow = shadow, Icon = strokes, Name = title, Sub = line };
    }

    static Color WhiteColour(int temp) => Paint.Mix(Paint.Hex(0xFFC47E), Paint.Hex(0xEEF4FF), temp / 100.0);

    UIElement BuildWhiteTile(string name, int temp)
    {
        var c = WhiteColour(temp);
        var glow = new Ellipse { Width = 36, Height = 36, Margin = new Thickness(-8), Fill = Glow.Soft(c, 0.8), Opacity = 0 };
        var label = new TextBlock { Text = name, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
        var face = new Border
        {
            Height = 56, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Children =
                {
                    new Grid
                    {
                        Width = 20, Height = 20, HorizontalAlignment = HorizontalAlignment.Center,
                        Children = { glow, new Ellipse { Fill = new SolidColorBrush(c), Stroke = Paint.Brush(0, 0x1A), StrokeThickness = 1 } },
                    },
                    label,
                },
            },
        };
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["BareButton"], HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = face,
        };
        AutomationProperties.SetName(button, $"{name} white");
        ToolTipService.SetToolTip(button, $"{name} white");
        button.Click += (_, _) => Session.Submit(LightCommand.White(CurrentLevel(), temp));
        whiteTiles.Add(new WhiteTile { Button = button, Face = face, Glow = glow, Name = label, Temp = temp });
        return button;
    }

    UIElement SwatchButton(string name, uint rgb)
    {
        var c = Paint.Hex(rgb);
        var (h, s, _) = ColorMath.RgbToHsv(c.R, c.G, c.B);
        var tile = new SwatchTile(name, c);
        int hue = (int)Math.Round(h) % 360, sat = (int)Math.Round(s * 100);
        tile.Click += (_, _) =>
        {
            lastH = hue;
            lastS = sat;
            Session.Submit(LightCommand.Colour(hue, sat, CurrentLevel()));
        };
        swatchButtons.Add((tile, name, hue, sat));
        return tile;
    }

    /// <summary>Custom: the same tile with a rainbow swatch, opening a colour picker.</summary>
    UIElement CustomButton()
    {
        var tile = new SwatchTile("Custom colour", Paint.Hex(0x808080), SwatchTile.RainbowFace());
        var flyout = new Flyout { Content = picker };
        flyout.Opening += PickerFlyout_Opening;
        tile.Flyout = flyout;
        picker.ColorChanged += Picker_ColorChanged;
        customTile = tile;
        return tile;
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

    int CurrentLevel() => (int)brightness.Value;

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
            foreach (var c in new Control[] { whiteTile.Button, colourTile.Button, brightness, warmth, PreviewButton }) c.IsEnabled = known;

            int level = st == null ? 0 : colour ? st.V : st.Brightness;
            var lightColour = st != null ? ToColor(st.Rgb) : Paint.Hex(0xFFC47E);
            bulb.Show(on, lightColour, level / 100.0);
            string shade = st == null ? "" : colour ? ColourName(st) : WhiteName(st.Temp);
            Headline.Text = st == null ? (kind == "busy" ? "Connecting…" : "Unknown")
                : Session.MusicRunning ? "Music sync is running"
                : on ? $"{shade} · {level}%" : "Off";
            AutomationProperties.SetName(bulb, on ? $"Light bulb glowing {shade.ToLowerInvariant()} at {level}%" : "Light bulb, off");

            // Dim backdrop so the glow reads; when off, a plain neutral surface.
            Stage.Background = Palette.B(on ? (p.Dark ? Paint.Hex(0x1A1A1C) : Paint.Hex(0x2A2A2D)) : (p.Dark ? Paint.Hex(0x323232) : Paint.Hex(0xE9E9E9)));
            StageGlow.Background = on
                ? Draw.Radial(new(0.5, 0.38), 0.7, 0.7, (0, Paint.WithAlpha(lightColour, 0.33)), (0.38, Paint.WithAlpha(lightColour, 0.09)), (0.7, Paint.WithAlpha(lightColour, 0)))
                : null;
            // A soft wash of the light's colour from the top right of the controls; none when off.
            Wash.Background = on
                ? Draw.Radial(new(1, 0), 1.2, 0.9, (0, Paint.WithAlpha(lightColour, 0.25)), (0.4, Paint.WithAlpha(lightColour, 0.078)), (0.7, Paint.WithAlpha(lightColour, 0)))
                : null;

            // Mode tiles
            ShowModeTile(whiteTile, known && !colour, p);
            ShowModeTile(colourTile, known && colour, p);
            colourTile.Button.Visibility = st?.HasColour == false ? Visibility.Collapsed : Visibility.Visible;
            WhitePanel.Visibility = colour || st?.HasTemp == false ? Visibility.Collapsed : Visibility.Visible;
            ColourPanel.Visibility = colour ? Visibility.Visible : Visibility.Collapsed;
            if (st != null)
            {
                if (!brightness.IsDragging) brightness.Value = Math.Max(1, level);
                if (!warmth.IsDragging) warmth.Value = st.Temp;
                if (colour)
                {
                    lastH = st.H;
                    lastS = st.S;
                }
            }
            var well = Palette.B(p.Dark ? Paint.Hex(0x383838) : Paint.Hex(0xEFEFEF));
            BrightnessPill.Background = WarmthPill.Background = ColourPill.Background = well;

            // Brightness: fills from a dim to a full version of the light's colour, with a glow.
            var track = p.Dark ? Paint.Hex(0x454545) : Paint.Hex(0xDCDCDC);
            brightness.SetColours(Palette.B(track), Draw.Gradient(new(0, 0), new(1, 0), (0, Paint.Mix(lightColour, track, 0.55)), (1, lightColour)),
                                  on ? lightColour : null, Paint.Mix(lightColour, Paint.Hex(0), 0.25));
            BrightnessText.Text = known ? $"{Math.Max(1, level)}%" : "--";
            brightness.ValueText = BrightnessText.Text;
            Draw.Tint(dimSun, p.Text3);
            Draw.Tint(brightSun, p.Text2);

            // Warmth: the track runs warm to cool; the handle takes the colour it's on.
            int temp = st?.Temp ?? 0;
            warmth.SetColours(Draw.Gradient(new(0, 0), new(1, 0), (0, Paint.Hex(0xF09A34)), (0.35, Paint.Hex(0xFFD9A3)), (0.6, Paint.Hex(0xFFF6EA)), (1, Paint.Hex(0xC9DDFB))),
                              null, null, Paint.Mix(Paint.Hex(0xE8892A), Paint.Hex(0x6F9FE8), temp / 100.0));
            WarmthText.Text = WarmthName(temp);
            warmth.ValueText = WarmthText.Text;
            foreach (var w in whiteTiles)
            {
                bool picked = known && !colour && st!.Temp == w.Temp;
                w.Face.Background = Palette.B(picked ? p.Card : p.Ctrl);
                w.Face.BorderBrush = Palette.B(picked ? p.Accent : p.CtrlStroke);
                w.Glow.Opacity = picked ? 1 : 0;
                w.Name.Foreground = Palette.B(p.Text);
                w.Button.IsEnabled = known;
                AutomationProperties.SetItemStatus(w.Button, picked ? "Selected" : "");
            }

            // Colour
            foreach (var (tile, _, h, s) in swatchButtons)
            {
                tile.Show(p, colour && Picked(st!, h, s));
                tile.IsEnabled = known;
            }
            customTile?.Show(p, false);
            if (customTile != null) customTile.IsEnabled = known;
            ColourDot.Fill = Palette.B(lightColour);
            ColourNameText.Text = st != null && colour ? ColourName(st) : "";

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

    static bool Picked(LightState st, int h, int s) => Math.Min(Math.Abs(h - st.H), 360 - Math.Abs(h - st.H)) <= 4 && Math.Abs(s - st.S) <= 6;

    /// <summary>The swatch's name when the light matches one, otherwise a general name for the hue.</summary>
    string ColourName(LightState st) =>
        swatchButtons.FirstOrDefault(b => Picked(st, b.H, b.S)).Name ?? ColorMath.HueName(st.H, st.S);

    static string WarmthName(int temp) => temp < 35 ? "Warm" : temp < 70 ? "Neutral" : "Cool";

    static void ShowModeTile(ModeTile t, bool picked, Palette p)
    {
        var fg = picked ? p.OnAccent : p.Text;
        t.Face.Background = Palette.B(picked ? p.Accent : p.Ctrl);
        t.Face.BorderBrush = Palette.B(picked ? p.Accent : p.CtrlStroke);
        t.Badge.Background = Palette.B(picked ? Paint.Hex(0xFFFFFF, 0x33) : (p.Dark ? Paint.Hex(0x383838) : Paint.Hex(0xEFEFEF)));
        t.Shadow.Opacity = picked ? 1 : 0;
        t.Name.Foreground = t.Sub.Foreground = Palette.B(fg);
        Draw.Tint(t.Icon, fg);
        AutomationProperties.SetItemStatus(t.Button, picked ? "Selected" : "");
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
        Session.Submit(LightCommand.White(CurrentLevel(), (int)warmth.Value));

    void Colour_Click(object sender, RoutedEventArgs e) =>
        Session.Submit(LightCommand.Colour(lastH, lastS, CurrentLevel()));

    void Brightness_Changed()
    {
        if (syncing) return;
        var st = Session.Shown;
        Session.Submit(st?.IsColour == true
            ? LightCommand.Colour(lastH, lastS, CurrentLevel())
            : LightCommand.White(CurrentLevel(), (int)warmth.Value));
    }

    void Temp_Changed()
    {
        if (!syncing) Session.Submit(LightCommand.White(CurrentLevel(), (int)warmth.Value));
    }

    void PickerFlyout_Opening(object? sender, object e)
    {
        syncing = true;
        picker.Color = ToColor(ColorMath.HsvToRgb(lastH, lastS / 100.0, 1));
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
