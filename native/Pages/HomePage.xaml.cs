using LedControl.Art;
using LedControl.Controls;
using LedControl.Core;
using LedControl.Music;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Path = Microsoft.UI.Xaml.Shapes.Path;
using Windows.UI;

namespace LedControl.Pages;

/// <summary>
/// Home: a banner with the house and the day, house-wide scenes, and an immersive card per device
/// (light, each air conditioner, music sync) with its main control on it.
/// </summary>
public sealed partial class HomePage : Page
{
    const string SnowflakeData = "F1 M8.62,8 L8.62,0.8 L7.38,0.8 L7.38,8 Z M8.4,3.22 L6.41,1.55 L5.61,2.51 L7.6,4.18 Z M8.4,4.18 L10.39,2.51 L9.59,1.55 L7.6,3.22 Z M8.31,8.54 L14.55,4.94 L13.92,3.86 L7.69,7.46 Z M12.34,5.96 L12.79,3.4 L11.56,3.18 L11.11,5.74 Z M11.51,6.44 L13.95,7.33 L14.38,6.15 L11.94,5.26 Z M7.69,8.54 L13.92,12.14 L14.55,11.06 L8.31,7.46 Z M11.94,10.74 L14.38,9.85 L13.95,8.67 L11.51,9.56 Z M11.11,10.26 L11.56,12.82 L12.79,12.6 L12.34,10.04 Z M7.38,8 L7.38,15.2 L8.62,15.2 L8.62,8 Z M7.6,12.78 L9.59,14.45 L10.39,13.49 L8.4,11.82 Z M7.6,11.82 L5.61,13.49 L6.41,14.45 L8.4,12.78 Z M7.69,7.46 L1.45,11.06 L2.08,12.14 L8.31,8.54 Z M3.66,10.04 L3.21,12.6 L4.44,12.82 L4.89,10.26 Z M4.49,9.56 L2.05,8.67 L1.62,9.85 L4.06,10.74 Z M8.31,7.46 L2.08,3.86 L1.45,4.94 L7.69,8.54 Z M4.06,5.26 L1.62,6.15 L2.05,7.33 L4.49,6.44 Z M4.89,5.74 L4.44,3.18 L3.21,3.4 L3.66,5.96 Z";

    static App Host => App.Instance!;
    static LightSession Session => Host.Session;

    // ------------------------------------------------------------ scenes

    sealed record SceneDef(string Name, string Description, uint Colour, uint Room,
                           string Light, string Soft, string Fill, string Line, Action Apply);

    readonly List<SceneDef> scenes;
    readonly List<(Border Ring, Border Hover)> sceneButtons = [];
    int activeScene = -1;

    // ------------------------------------------------------------ devices

    readonly DeviceCard lightCard = new();
    readonly BulbArt bulb = new();
    readonly RadialDial lightDial = new() { Minimum = 1, Maximum = 100, Step = 1, LargeStep = 10 };
    readonly TextBlock lightValue = ValueText(), lightCaption = CaptionText();

    sealed class AcCardParts
    {
        public required AcUnit Unit;
        public required DeviceCard Card;
        public required AcFrontArt Art;
        public required RadialDial Dial;
        public required TextBlock Value, Caption;
        public required Button Down, Up;
    }

    readonly List<AcCardParts> acCards = [];

    readonly DeviceCard musicCard = new();
    readonly SpeakerArt speaker = new();
    readonly MusicRing ring = new();

    public HomePage()
    {
        InitializeComponent();
        scenes = BuildScenes();
        foreach (var (s, i) in scenes.Select((s, i) => (s, i))) SceneGrid.Children.Add(SceneButton(s, i));

        BuildLightCard();
        BuildMusicCard();
        RebuildDevices();

        Loaded += (_, _) =>
        {
            Session.Changed += OnLightChanged;
            Session.MusicChanged += OnMusicChanged;
            Session.MusicFrame += OnMusicFrame;
            Host.Acs.Changed += OnAcsChanged;
            Host.Acs.ListChanged += RebuildDevices;
            RefreshAll();
        };
        Unloaded += (_, _) =>
        {
            Session.Changed -= OnLightChanged;
            Session.MusicChanged -= OnMusicChanged;
            Session.MusicFrame -= OnMusicFrame;
            Host.Acs.Changed -= OnAcsChanged;
            Host.Acs.ListChanged -= RebuildDevices;
        };
    }

    void RefreshAll()
    {
        UpdateHero();
        UpdateLight();
        UpdateAcs();
        UpdateMusic();
        UpdateSceneHint();
    }

    void OnLightChanged()
    {
        UpdateLight();
        UpdateHero();
    }

    void OnAcsChanged()
    {
        UpdateAcs();
        UpdateHero();
    }

    void OnMusicChanged()
    {
        UpdateMusic();
        UpdateLight();
    }

    void OnMusicFrame(MusicFrame f)
    {
        if (!Session.MusicRunning) return;
        ring.Draw(f.Bars);
        speaker.Pulse(f.Level, f.Bass);
    }

    // ------------------------------------------------------------ banner

    void UpdateHero()
    {
        var now = DateTime.Now;
        DateText.Text = now.ToString("dddd, d MMMM");
        int h = now.Hour;
        bool night = h < 6 || h >= 18;
        GreetingText.Text = h < 5 ? "Good night" : h < 12 ? "Good morning" : h < 18 ? "Good afternoon" : "Good evening";
        // Morning: pale blue into gold. Afternoon: clear blue. Evening and night: deep blue into sunset.
        (uint a, uint b, uint c) sky = h is >= 5 and < 11 ? (0x2C6DB5u, 0x6FA9DEu, 0xE9B77Au)
                                     : h is >= 11 and < 17 ? (0x1E5BA6u, 0x3B86D2u, 0x86C3EEu)
                                     : (0x142A52u, 0x27508Fu, 0xC9825Au);
        HeroBrush.GradientStops[0].Color = Paint.Hex(sky.a);
        HeroBrush.GradientStops[1].Color = Paint.Hex(sky.b);
        HeroBrush.GradientStops[2].Color = Paint.Hex(sky.c);

        var st = Session.Shown;
        bool lightOn = st?.On == true;
        var colour = LightColour(st);
        bool anyAc = Host.Acs.Units.Any(u => u.Shown?.On == true);
        House.Show(night, lightOn, colour, st?.Level ?? 0, anyAc);
        AutomationProperties.SetName(House, $"Your home: light {(lightOn ? "on" : "off")}, air conditioners {(anyAc ? "running" : "off")}");

        HeroChips.Children.Clear();
        var rooms = Host.Acs.Units.Select(u => u.Shown?.RoomTemp).OfType<int>().ToList();
        if (rooms.Count > 0)
            HeroChips.Children.Add(HeroChip(rooms.Min() == rooms.Max() ? $"Inside {rooms.Min()} °C" : $"Inside {rooms.Min()}–{rooms.Max()} °C", Paint.Hex(0x9BD3FF)));
        HeroChips.Children.Add(HeroChip(lightOn ? $"Light on · {(st!.IsColour ? st.V : st.Brightness)}%" : "Light off",
                                        lightOn ? colour : Paint.Hex(0xFFFFFF, 0x80)));
        int total = 1 + Host.Acs.Units.Count;
        int connected = (Session.StatusKind == "ok" ? 1 : 0) + Host.Acs.Units.Count(u => u.StatusKind == "ok");
        bool waiting = Session.StatusKind == "busy" || Host.Acs.Units.Any(u => u.StatusKind == "busy");
        HeroChips.Children.Add(HeroChip(
            connected == total ? (total == 1 ? "1 device connected" : $"{total} devices connected")
            : waiting ? "Connecting…" : $"{connected} of {total} devices connected",
            connected == total ? Paint.Hex(0x6CCB5F) : waiting ? Paint.Hex(0xFFFFFF, 0x99) : Paint.Hex(0xFFC83D)));
    }

    static Border HeroChip(string text, Color dot) => new()
    {
        Height = 32, Padding = new Thickness(12, 0, 12, 0), CornerRadius = new CornerRadius(16),
        Background = Paint.Brush(0xFFFFFF, 0x29), BorderBrush = Paint.Brush(0xFFFFFF, 0x38), BorderThickness = new Thickness(1),
        Child = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new Ellipse { Width = 8, Height = 8, Fill = new SolidColorBrush(dot), VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = text, FontSize = 13, Foreground = Paint.Brush(0xFFFFFF) },
            },
        },
    };

    void Hero_SizeChanged(object sender, SizeChangedEventArgs e) =>
        House.Visibility = e.NewSize.Width < 560 ? Visibility.Collapsed : Visibility.Visible;

    // ------------------------------------------------------------ scenes

    List<SceneDef> BuildScenes()
    {
        var acs = Host.Acs;
        string both = acs.Units.Count == 2 ? "both ACs" : "the ACs";
        return
        [
            new("Cool down", $"{Cap(both)} to 24 °C with Turbo", 0x3A9EE8, 0x163A63,
                "M24 22 L6 56 H62 L46 22 Z", "M2 56h76v2H2z", "M14 8h52a4 4 0 0 1 4 4v6a4 4 0 0 1-4 4H14a4 4 0 0 1-4-4v-6a4 4 0 0 1 4-4z",
                "M26 28v14M40 28v18M54 28v14 M36 38l4 4 4-4",
                () =>
                {
                    foreach (var u in acs.Units) u.Set(("Pow", 1), ("Mod", AcState.Cool), ("SetTem", 24), ("Tur", 1), ("Quiet", 0));
                }),
            new("Movie", "Light dim blue, running ACs on Quiet", 0x3E6FE0, 0x141B33,
                "M17 6h46v27H17z", "M14 3h52v33H14z", "M37 13v13l11-6.5z M36 36h8v3h-8z M29 39h22v2H29z M8 49a6 6 0 0 1 6-6h52a6 6 0 0 1 6 6v8H8z", "",
                () =>
                {
                    Session.Submit(LightSession.Scenes.First(s => s.Name == "Movie").Command);
                    foreach (var u in acs.Units.Where(u => u.Shown?.On == true)) u.Set(("Quiet", 1), ("Tur", 0));
                }),
            new("Sleep", "Night light, ACs on Sleep at 27 °C", 0xF2802E, 0x2A1A12,
                "M64 36 L56 50 H74 Z", "M6 42h56v8H6z M6 32h8v18H6z M18 36h14v6H18z",
                "M54 2a10 10 0 1 0 10 14a8 8 0 1 1-10-14z M22 6l1.4 3 3 1.4-3 1.4-1.4 3-1.4-3-3-1.4 3-1.4z M8 50h3v4H8z M57 50h3v4h-3z", "M4 55h72",
                () =>
                {
                    Session.Submit(LightSession.Scenes.First(s => s.Name == "Night").Command);
                    foreach (var u in acs.Units) u.Set(("Pow", 1), ("SwhSlp", 1), ("SetTem", 27), ("Tur", 0));
                }),
            new("All off", $"Light, music sync and {both} off", 0x8A8F98, 0x1C1E22,
                "", "M6 54h68v2H6z M16 22h20v16H16z M44 22h20v16H44z", "M40 12a14 14 0 1 0 0.1 0z", "M40 6v12 M32 11a12 12 0 1 0 16 0",
                () =>
                {
                    Session.Submit(LightCommand.Power(false)); // also stops music sync
                    foreach (var u in acs.Units) u.Set(("Pow", 0));
                }),
        ];
    }

    static string Cap(string s) => char.ToUpper(s[0]) + s[1..];

    static Geometry? Geo(string data) =>
        string.IsNullOrWhiteSpace(data) ? null : (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);

    UIElement SceneButton(SceneDef s, int index)
    {
        var c = Paint.Hex(s.Colour);
        var art = new Canvas { Width = 80, Height = 58 };
        if (Geo(s.Light) is { } light) art.Children.Add(new Path { Data = light, Fill = new SolidColorBrush(Paint.WithAlpha(c, 0.6)) });
        if (Geo(s.Soft) is { } soft) art.Children.Add(new Path { Data = soft, Fill = Paint.Brush(0xFFFFFF, 0x4D) });
        if (Geo(s.Fill) is { } fill) art.Children.Add(new Path { Data = fill, Fill = Paint.Brush(0xFFFFFF, 0xEB) });
        if (Geo(s.Line) is { } line)
            art.Children.Add(new Path
            {
                Data = line, Stroke = Paint.Brush(0xFFFFFF, 0xD9), StrokeThickness = 2,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            });

        var hover = new Border { Background = Paint.Brush(0xFFFFFF, 0x14), Opacity = 0 };
        var face = new Grid
        {
            Height = 132, CornerRadius = new CornerRadius(14),
            Children =
            {
                new Border { Background = new LinearGradientBrush
                {
                    StartPoint = new Windows.Foundation.Point(0.33, 0), EndPoint = new Windows.Foundation.Point(0.67, 1),
                    GradientStops = { new GradientStop { Offset = 0, Color = Paint.Hex(s.Room) }, new GradientStop { Offset = 1, Color = Paint.Hex(0x0F1012) } },
                } },
                new Border { Background = new RadialGradientBrush
                {
                    Center = new Windows.Foundation.Point(0.78, 0.3), GradientOrigin = new Windows.Foundation.Point(0.78, 0.3), RadiusX = 0.8, RadiusY = 0.9,
                    GradientStops =
                    {
                        new GradientStop { Offset = 0, Color = Paint.WithAlpha(c, 0.67) },
                        new GradientStop { Offset = 0.45, Color = Paint.WithAlpha(c, 0.2) },
                        new GradientStop { Offset = 0.75, Color = Paint.WithAlpha(c, 0) },
                    },
                } },
                hover,
                new Viewbox { Width = 100, Height = 72, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                              Margin = new Thickness(0, 8, 10, 0), Child = art },
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(16, 0, 16, 14), Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = s.Name, FontSize = 16, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Paint.Brush(0xFFFFFF) },
                        new TextBlock { Text = s.Description, FontSize = 12, Foreground = Paint.Brush(0xFFFFFF, 0xD9), TextWrapping = TextWrapping.Wrap },
                    },
                },
            },
        };
        var ring = new Border { CornerRadius = new CornerRadius(16), BorderThickness = new Thickness(2), Padding = new Thickness(2), Child = face };
        var button = new Button { Style = (Style)Application.Current.Resources["BareButton"], HorizontalAlignment = HorizontalAlignment.Stretch,
                                  HorizontalContentAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(16), Content = ring };
        AutomationProperties.SetName(button, $"{s.Name}: {s.Description}");
        ToolTipService.SetToolTip(button, s.Description);
        button.PointerEntered += (_, _) => hover.Opacity = 1;
        button.PointerExited += (_, _) => hover.Opacity = 0;
        button.Click += (_, _) =>
        {
            s.Apply();
            activeScene = index;
            UpdateSceneHint();
        };
        sceneButtons.Add((ring, hover));
        return button;
    }

    void UpdateSceneHint()
    {
        for (int i = 0; i < sceneButtons.Count; i++)
            sceneButtons[i].Ring.BorderBrush = i == activeScene
                ? (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        SceneHint.Text = activeScene >= 0 ? scenes[activeScene].Description + "." : "One tap sets the light and the air conditioners together.";
    }

    /// <summary>The user changed a device by hand: no scene is active any more.</summary>
    void ClearScene()
    {
        if (activeScene < 0) return;
        activeScene = -1;
        UpdateSceneHint();
    }

    // ------------------------------------------------------------ shared pieces of the device cards

    static TextBlock ValueText() => new()
    {
        FontFamily = new FontFamily("Segoe UI Variable Display"), FontSize = 40, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        Foreground = Paint.Brush(0xFFFFFF), HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 40,
    };

    static TextBlock CaptionText() => new() { FontSize = 12, Foreground = Paint.Brush(0xFFFFFF, 0xB8), HorizontalAlignment = HorizontalAlignment.Center };

    static Button RoundButton(string glyph, string label, Action click)
    {
        var b = new Button
        {
            Style = (Style)Application.Current.Resources["SceneRoundButton"],
            Content = new FontIcon { Glyph = glyph, FontSize = 12 },
        };
        AutomationProperties.SetName(b, label);
        ToolTipService.SetToolTip(b, label);
        b.Click += (_, _) => click();
        return b;
    }

    static StackPanel DialCentre(TextBlock value, TextBlock caption, Button down, Button up) => new()
    {
        Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        Children =
        {
            value, caption,
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 4, 0, 0),
                             HorizontalAlignment = HorizontalAlignment.Center, Children = { down, up } },
        },
    };

    static FontIcon Glyph(string glyph) => new() { Glyph = glyph, FontSize = 18 };

    void RebuildDevices()
    {
        DeviceGrid.Children.Clear();
        DeviceGrid.Children.Add(lightCard);
        acCards.Clear();
        foreach (var unit in Host.Acs.Units) acCards.Add(BuildAcCard(unit));
        foreach (var a in acCards) DeviceGrid.Children.Add(a.Card);
        DeviceGrid.Children.Add(musicCard);
        UpdateAcs();
    }

    // ------------------------------------------------------------ light

    static Color LightColour(LightState? st)
    {
        var (r, g, b) = st?.Rgb ?? ((byte)255, (byte)196, (byte)126);
        return Color.FromArgb(255, r, g, b);
    }

    void BuildLightCard()
    {
        lightCard.Icon = Glyph("");
        lightCard.Art = bulb;
        var down = RoundButton("", "Dim the light", () => NudgeLight(-5));
        var up = RoundButton("", "Brighten the light", () => NudgeLight(5));
        lightDial.Center = DialCentre(lightValue, lightCaption, down, up);
        AutomationProperties.SetName(lightDial, "Brightness");
        lightDial.ValueChanged += v => SetBrightness((int)v);
        lightCard.Body = lightDial;
        lightCard.PowerToggled += on =>
        {
            ClearScene();
            Session.Submit(LightCommand.Power(on));
        };
        lightCard.OpenClicked += () => Host.Window.ShowPage("light");
    }

    void NudgeLight(int delta)
    {
        var st = Session.Shown;
        int now = st == null ? 50 : st.IsColour ? st.V : st.Brightness;
        SetBrightness(Math.Clamp((st?.On == true ? now : 0) + delta, 1, 100));
    }

    void SetBrightness(int v)
    {
        ClearScene();
        var st = Session.Shown ?? new LightState();
        Session.Submit(st.IsColour ? LightCommand.Colour(st.H, st.S, v) : LightCommand.White(v, st.Temp));
    }

    void UpdateLight()
    {
        var st = Session.Shown;
        string name = Host.Config.Device.Name;
        lightCard.Title = string.IsNullOrWhiteSpace(name) ? "Light" : name;
        bool known = st != null, on = st?.On == true, music = Session.MusicRunning;
        int level = st == null ? 0 : st.IsColour ? st.V : st.Brightness;
        var colour = LightColour(st);

        lightCard.Status = Session.StatusKind switch
        {
            "ok" => music ? "Following the music" : st!.Describe(),
            _ => Session.StatusText,
        };
        lightCard.IsOn = on;
        lightCard.PowerEnabled = known;
        bulb.Show(on, colour, level / 100.0);

        if (!lightDial.IsDragging) lightDial.Value = Math.Max(1, level);
        lightDial.IsEnabled = known && !music;
        lightDial.ArcBrush = new SolidColorBrush(!on ? Paint.Hex(0xFFFFFF, 0x73) : st!.IsColour ? Paint.Mix(colour, Paint.Hex(0xFFFFFF), 0.15) : Paint.Hex(0xF2B45A));
        lightValue.Text = known ? $"{Math.Max(1, level)}%" : "--";
        lightCaption.Text = music ? "Music sync" : on ? "Brightness" : "Off · brightness";

        var chips = new List<string>();
        if (st != null)
        {
            chips.Add(st.IsColour ? ColorMath.HueName(st.H, st.S) : st.Temp < 25 ? "Warm white" : st.Temp > 75 ? "Cool white" : "White");
            if (!string.IsNullOrEmpty(st.Via)) chips.Add(st.Via == "cloud" ? "Tuya Cloud" : "Home network");
        }
        lightCard.SetChips([.. chips]);

        if (on)
        {
            var glow = Paint.WithAlpha(colour, 0.3 + 0.2 * level / 100.0);
            lightCard.SetScene(Paint.Mix(Paint.Hex(0x2E2B28), colour, 0.08), Paint.Hex(0x1A1918), Paint.Hex(0x121213), glow);
        }
        else lightCard.SetScene(Paint.Hex(0x3A3B3F), Paint.Hex(0x26272B), Paint.Hex(0x1C1D20), null);
        AutomationProperties.SetName(bulb, on ? $"Light bulb glowing at {level}%" : "Light bulb, off");
    }

    // ------------------------------------------------------------ air conditioners

    AcCardParts BuildAcCard(AcUnit unit)
    {
        var parts = new AcCardParts
        {
            Unit = unit, Card = new DeviceCard(), Art = new AcFrontArt(),
            Dial = new RadialDial { Minimum = AcState.MinTemp, Maximum = AcState.MaxTemp, Step = 1, LargeStep = 3 },
            Value = ValueText(), Caption = CaptionText(),
            Down = RoundButton("", "Lower the temperature", () => NudgeAc(unit, -1)),
            Up = RoundButton("", "Raise the temperature", () => NudgeAc(unit, 1)),
        };
        parts.Card.Icon = new PathIcon { Data = Geo(SnowflakeData), Width = 16, Height = 16 };
        parts.Card.Art = parts.Art;
        parts.Dial.Center = DialCentre(parts.Value, parts.Caption, parts.Down, parts.Up);
        AutomationProperties.SetName(parts.Dial, "Target temperature");
        parts.Dial.ValueChanged += v =>
        {
            ClearScene();
            unit.Set(("SetTem", (int)v));
        };
        parts.Card.Body = parts.Dial;
        parts.Card.PowerToggled += on =>
        {
            ClearScene();
            unit.Set(("Pow", on ? 1 : 0));
        };
        parts.Card.OpenClicked += () => Host.Window.ShowAc(unit);
        return parts;
    }

    void NudgeAc(AcUnit unit, int delta)
    {
        if (unit.Shown is not { } st) return;
        ClearScene();
        unit.Set(("SetTem", Math.Clamp(st.SetTemp + delta, AcState.MinTemp, AcState.MaxTemp)));
    }

    void UpdateAcs()
    {
        foreach (var p in acCards)
        {
            var st = p.Unit.Shown;
            bool known = st != null, on = st?.On == true, heat = st?.Mode == AcState.Heat, usesTemp = st?.UsesSetTemp ?? true;
            p.Card.Title = p.Unit.Config.DisplayName;
            string doing = st == null ? "" : !on ? "off" : st.Mode switch
            {
                AcState.Cool => "cooling", AcState.Heat => "heating", AcState.Dry => "drying", AcState.Fan => "fan only", _ => "auto",
            };
            p.Card.Status = p.Unit.StatusKind == "ok" && st != null
                ? (st.RoomTemp is { } room ? $"Room {room} °C · {doing}" : Cap(doing))
                : p.Unit.StatusText;
            p.Card.IsOn = on;
            p.Card.PowerEnabled = known;
            p.Art.Show(on, st?.SetTemp, heat);
            AutomationProperties.SetName(p.Art, $"{p.Unit.Config.DisplayName}{(on ? ", running" : ", off")}");

            if (!p.Dial.IsDragging && st != null) p.Dial.Value = st.SetTemp;
            p.Dial.IsEnabled = known && usesTemp;
            p.Down.IsEnabled = p.Up.IsEnabled = known && usesTemp;
            p.Dial.ArcBrush = new SolidColorBrush(!on ? Paint.Hex(0xFFFFFF, 0x73) : heat ? Paint.Hex(0xFFB27A) : Paint.Hex(0x7FD6FF));
            p.Value.Text = st != null ? $"{st.SetTemp}°" : "--";
            p.Caption.Text = st == null ? "Connecting" : !on ? "Off · set to"
                : !usesTemp ? $"Not used in {AcState.ModeNames[st.Mode]}"
                : st.Mode switch { AcState.Cool => "Cooling to", AcState.Heat => "Heating to", _ => "Auto · set to" };

            var chips = new List<string>();
            if (st != null)
            {
                chips.Add(AcState.ModeNames[st.Mode]);
                chips.Add("Fan " + AcState.FanNames[st.FanSpeed].ToLowerInvariant());
                if (st.Is("Tur")) chips.Add("Turbo");
                if (st.Is("Quiet")) chips.Add("Quiet");
                if (st.Is("SwhSlp")) chips.Add("Sleep");
            }
            p.Card.SetChips([.. chips]);

            if (on && heat)
                p.Card.SetScene(Paint.Hex(0xC4702E), Paint.Hex(0x8F431D), Paint.Hex(0x4A2012), Paint.Hex(0xFFC896, 0x73), 0.22, 0.9, 0.4);
            else if (on)
                p.Card.SetScene(Paint.Hex(0x2E74C4), Paint.Hex(0x1D4F8F), Paint.Hex(0x12294A), Paint.Hex(0x96D7FF, 0x73), 0.22, 0.9, 0.4);
            else
                p.Card.SetScene(Paint.Hex(0x4A4F57), Paint.Hex(0x2C2F35), Paint.Hex(0x1E2024), null);
        }
    }

    // ------------------------------------------------------------ music sync

    void BuildMusicCard()
    {
        musicCard.Title = "Music sync";
        musicCard.Icon = Glyph("");
        musicCard.HasPower = false;
        musicCard.Art = speaker;
        musicCard.Body = ring;
        ring.PlayButton.Click += (_, _) =>
        {
            ClearScene();
            Session.ToggleMusic();
        };
        musicCard.OpenClicked += () => Host.Window.ShowPage("music");
    }

    void UpdateMusic()
    {
        bool running = Session.MusicRunning || Session.MusicKind == "busy";
        musicCard.Status = running ? Session.MusicText : "Stopped";
        speaker.Show(running);
        ring.Show(running);
        AutomationProperties.SetName(speaker, running ? "Speaker playing, with sound waves" : "Speaker, quiet");
        var prefs = Host.Config.Music;
        string style = prefs.Style.Length > 0 ? Cap(prefs.Style) : "Spectrum";
        musicCard.SetChips(style, prefs.Source == MusicPrefs.SystemSource ? "System audio" : "Audio input");
        if (running)
            musicCard.SetScene(Paint.Hex(0x2A2236), Paint.Hex(0x17141E), Paint.Hex(0x0F0E13), Paint.Hex(0x8A5BE0, 0x77), 0.22);
        else
            musicCard.SetScene(Paint.Hex(0x2E2F33), Paint.Hex(0x222327), Paint.Hex(0x18191C), null);
    }
}
