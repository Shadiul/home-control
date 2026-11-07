using System.Runtime.InteropServices;
using LedControl.Art;
using LedControl.Controls;
using LedControl.Core;
using LedControl.Music;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace LedControl.Shell;

/// <summary>
/// The quick panel: click the notification-area icon to open it just above the taskbar. One tab per device
/// (light, each air conditioner, music sync) with a picture of the device lit by its state and a big power
/// button, then its controls on a soft wash of its colour: fill sliders and tiles. It hides when you click
/// anywhere else.
/// </summary>
public sealed partial class QuickPanel : Window
{
    const double PanelWidth = 380, PanelHeight = 680;
    const string BulbIcon = "M9 18h6M10 21h4M12 3a6 6 0 0 0-3.6 10.8c.7.6 1.1 1.3 1.1 2.2v.5h5V16c0-.9.4-1.6 1.1-2.2A6 6 0 0 0 12 3z";
    const string SnowIcon = "M12 2.5v19M3.8 7.3l16.4 9.4M20.2 7.3 3.8 16.7M9.5 4.5 12 6l2.5-1.5M9.5 19.5 12 18l2.5 1.5";
    const string NoteIcon = "M9 18V5.5l11-2V16M6.5 20.5a2.5 2.5 0 1 1 0-5 2.5 2.5 0 0 1 0 5zM17.5 18.5a2.5 2.5 0 1 1 0-5 2.5 2.5 0 0 1 0 5z";
    const string SunIcon = "M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM12 2.5v2M12 19.5v2M4.6 4.6 6 6M18 18l1.4 1.4M2.5 12h2M19.5 12h2M4.6 19.4 6 18M18 6l1.4-1.4";
    const string WarmthIcon = "M12 3a2 2 0 0 0-2 2v9.3a4 4 0 1 0 4 0V5a2 2 0 0 0-2-2zM12 17.5v-6";
    const string FanIcon = "M12 12c-1-4 0-8 3-8.5s3 4.5-3 8.5zM12 12c4-1 8 0 8.5 3s-4.5 3-8.5-3zM12 12c1 4 0 8-3 8.5s-3-4.5 3-8.5zM12 12c-4 1-8 0-8.5-3s4.5-3 8.5 3z";
    const string MicIcon = "M12 3a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V6a3 3 0 0 0-3-3zM5.5 11a6.5 6.5 0 0 0 13 0M12 17.5V21";
    const string PcIcon = "M7.5 3h9a1.5 1.5 0 0 1 1.5 1.5v15a1.5 1.5 0 0 1-1.5 1.5h-9A1.5 1.5 0 0 1 6 19.5v-15A1.5 1.5 0 0 1 7.5 3zM9 7h6M9 10h6M12 16.5h.01";

    // PC lighting colours: two whites and six colours.
    static readonly (string Name, uint Rgb)[] PcColours =
        [("Warm white", 0xFFC47E), ("Cool white", 0xEEF4FF), ("Red", 0xE8453C), ("Orange", 0xF2802E), ("Green", 0x3DAA67), ("Teal", 0x2BB5A8), ("Blue", 0x3E6FE0), ("Purple", 0xA85BD8)];

    static readonly Color Violet = Paint.Hex(0x8A5BE0), Ice = Paint.Hex(0x3A9EE8), Grey = Paint.Hex(0x8A8A8A);

    // Colours for the light and for music sync's Pulse style: name and the colour shown.
    static readonly (string Name, uint Rgb)[] Hues =
        [("Red", 0xE8453C), ("Orange", 0xF2802E), ("Yellow", 0xF2D33C), ("Green", 0x3DAA67), ("Teal", 0x2BB5A8), ("Blue", 0x3E6FE0), ("Purple", 0xA85BD8), ("Pink", 0xE05B9E)];

    // Music sync's Pulse hues, in the same order (the values the Music sync page uses).
    static readonly int[] PulseHues = [0, 25, 50, 130, 190, 230, 280, 320];

    static readonly Dictionary<string, string> SceneIcons = new()
    {
        ["Bright"] = SunIcon,
        ["Reading"] = "M4 5.5C6.5 4 9.5 4 12 6c2.5-2 5.5-2 8-.5V19c-2.5-1.5-5.5-1.5-8 .5-2.5-2-5.5-2-8-.5zM12 6v13.5",
        ["Relax"] = "M5 9h11v5a5 5 0 0 1-5 5h-1a5 5 0 0 1-5-5zM16 10h1.5a2.5 2.5 0 0 1 0 5H16M8.5 3.5c0 1.5 1 1.5 1 3M12.5 3.5c0 1.5 1 1.5 1 3",
        ["Night"] = "M20 14.5A8 8 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z",
        ["Movie"] = "M4 5h16v14H4zM8 5v14M16 5v14M4 9.5h4M4 14.5h4M16 9.5h4M16 14.5h4",
        ["Party"] = "M12 3l1.8 4.7L18.5 9l-4.7 1.8L12 15.5l-1.8-4.7L5.5 9l4.7-1.3z",
    };

    // Air conditioner modes: icon and colour.
    static readonly (string Icon, uint Colour)[] Modes =
    [
        ("M20 12a8 8 0 0 1-14.3 4.9M4 12a8 8 0 0 1 14.3-4.9M18.5 3.5v3.6h-3.6M5.5 20.5v-3.6h3.6", 0x0F8A7E),
        (SnowIcon, 0x2F7FD8),
        ("M12 3.5s6 6.4 6 10.9a6 6 0 0 1-12 0c0-4.5 6-10.9 6-10.9z", 0x1C8FB0),
        (FanIcon, 0x5A6B7D),
        ("M12 3c.8 3.6 5 5.2 5 10a5 5 0 0 1-10 0c0-2.4 1.3-3.9 2.4-4.9.4 1.9 1.4 2.9 2.4 2.9-.2-3 .2-5.4.2-8z", 0xD9622B),
    ];

    // Air conditioner options: name, what it does, Gree key, icon, colour.
    static readonly (string Name, string Says, string Key, string Icon, uint Colour)[] Options =
    [
        ("Turbo", "Turbo: full power to reach the target fast", "Tur", "M13 2 4 14h7l-1 8 9-12h-7z", 0xD9480F),
        ("Quiet", "Quiet: lower fan noise", "Quiet", "M4 9h4l5-4v14l-5-4H4zM17 9.5l4 5M21 9.5l-4 5", 0x4C5BD4),
        ("Sleep", "Sleep: eases the temperature overnight", "SwhSlp", "M20 14.5A8 8 0 1 1 9.5 4a6.5 6.5 0 0 0 10.5 10.5z", 0x7048C2),
        ("Swing ↕", "Swing up and down", "SwUpDn", "M12 3v18M8 7l4-4 4 4M8 17l4 4 4-4", 0x2F7FD8),
        ("Swing ↔", "Swing left and right", "SwingLfRig", "M3 12h18M7 8l-4 4 4 4M17 8l4 4-4 4", 0x2F7FD8),
        ("Display", "Display light on the unit", "Lig", "M4 6h16v10H4zM9 20h6M12 16v4", 0x0B8578),
    ];

    static readonly (string Key, string Name, string Icon)[] Styles =
    [
        ("pulse", "Pulse", "M3 12h3l2-5 4 10 2-5h7"),
        ("spectrum", "Spectrum", "M5 20V12M10 20V6M15 20V10M20 20V4"),
        ("beat", "Beat", "M13 2 4 14h7l-1 8 9-12h-7z"),
        ("party", "Party", "M12 3l1.8 4.7L18.5 9l-4.7 1.8L12 15.5l-1.8-4.7L5.5 9l4.7-1.3z"),
    ];

    sealed class Tab
    {
        public required Button Button;
        public required Grid Face;
        public required Ellipse Dot;
        public required Border Bar;
        public required TextBlock Name;
        public required Path[] Icon;
        public AcUnit? Unit;      // null: the light, music sync or PC lighting
        public bool IsMusic, IsPc;
    }

    readonly IntPtr hwnd;
    readonly List<Tab> tabs = [];
    readonly List<Action<Palette>> updaters = []; // each control of the shown tab, brought up to date
    readonly List<TextBlock> titles = [];
    readonly BigBulb bulb = new();
    readonly AcFrontArt acArt = new();
    readonly SpeakerCone speaker = new();
    readonly PcCaseArt pcArt = new();
    readonly Viewbox bulbFrame, acFrame, speakerFrame, pcFrame;
    readonly DispatcherQueueTimer saveTimer;
    readonly Dictionary<string, int> lastFan = [];
    Tab? current;
    bool syncing;
    long hiddenAt;

    static App Host => App.Instance!;
    static LightSession Session => Host.Session;
    static MusicPrefs Prefs => Host.Config.Music;

    public QuickPanel()
    {
        InitializeComponent();
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        bulbFrame = new Viewbox { Height = 105, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 11, 0, 0), Child = bulb };
        acFrame = new Viewbox { Height = 120, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 0, 0), Child = acArt };
        speakerFrame = new Viewbox { Height = 150, VerticalAlignment = VerticalAlignment.Top, Child = speaker };
        pcFrame = new Viewbox { Height = 112, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0), Child = pcArt };
        AppWindow.Title = AppInfo.Name;
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"));
        AppWindow.IsShownInSwitchers = false;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.SetBorderAndTitleBar(true, false); // Windows 11 gives it rounded corners and a shadow, like its own flyouts
            p.IsResizable = p.IsMaximizable = p.IsMinimizable = false;
            p.IsAlwaysOnTop = true;
        }
        Activated += (_, e) =>
        {
            if (e.WindowActivationState == WindowActivationState.Deactivated && AppWindow.IsVisible) HidePanel();
        };
        AppWindow.Closing += (_, e) =>
        {
            e.Cancel = true; // keep it for next time
            HidePanel();
        };
        saveTimer = Host.UI.CreateTimer();
        saveTimer.Interval = TimeSpan.FromMilliseconds(600);
        saveTimer.IsRepeating = false;
        saveTimer.Tick += (_, _) => SaveMusicPrefs();

        Session.Changed += Refresh;
        Session.MusicChanged += Refresh;
        Host.Acs.Changed += Refresh;
        Host.Acs.ListChanged += BuildTabs;
        Host.PcChanged += Refresh;
        Root.ActualThemeChanged += (_, _) => Refresh();
        BuildTabs();
    }

    // ------------------------------------------------------------ showing and hiding

    public bool IsOpen => AppWindow.IsVisible;

    /// <summary>The notification-area icon was clicked.</summary>
    public void Toggle()
    {
        if (AppWindow.IsVisible)
        {
            HidePanel();
            return;
        }
        // The click that took the focus away has already closed it: don't open it straight back up.
        if (Environment.TickCount64 - hiddenAt < 350) return;
        ShowPanel();
    }

    void ShowPanel()
    {
        ApplyTheme(Host.Config.Theme);
        Place();
        AppWindow.Show();
        ShowTab();
        Activate();
        WinIntegration.SetForegroundWindow(hwnd);
    }

    void HidePanel()
    {
        hiddenAt = Environment.TickCount64;
        AppWindow.Hide();
        speaker.Show(false); // no animation while hidden
    }

    void ApplyTheme(string theme) => Root.RequestedTheme = theme switch
    {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    /// <summary>Bottom right of the screen the pointer is on, just above the taskbar.</summary>
    void Place()
    {
        GetCursorPos(out var cursor);
        var area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Primary).WorkArea;
        double scale = WinIntegration.GetDpiForWindow(hwnd) / 96.0;
        int margin = (int)(12 * scale);
        int w = (int)(PanelWidth * scale), h = Math.Min((int)(PanelHeight * scale), area.Height - 2 * margin);
        AppWindow.MoveAndResize(new RectInt32(area.X + area.Width - w - margin, area.Y + area.Height - h - margin, w, h));
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    // ------------------------------------------------------------ tabs

    void BuildTabs()
    {
        var keep = current;
        tabs.Clear();
        Tabs.Children.Clear();
        Tabs.ColumnDefinitions.Clear();
        AddTab("Light", BulbIcon, null, false);
        foreach (var u in Host.Acs.Units) AddTab(ShortName(u), SnowIcon, u, false);
        AddTab("Music", NoteIcon, null, true);
        AddTab("PC", PcIcon, null, false, pc: true);
        current = tabs.FirstOrDefault(t => keep != null && t.Unit == keep.Unit && t.IsMusic == keep.IsMusic && t.Unit != null)
               ?? tabs.FirstOrDefault(t => keep?.IsMusic == true && t.IsMusic)
               ?? tabs.FirstOrDefault(t => keep?.IsPc == true && t.IsPc)
               ?? tabs[0];
        ShowTab();
    }

    /// <summary>A short tab name for a unit: its name, or "AC 1", "AC 2" when it has none.</summary>
    static string ShortName(AcUnit u)
    {
        if (!string.IsNullOrWhiteSpace(u.Config.Name)) return u.Config.Name.Trim();
        return "AC " + (App.Instance!.Acs.Units.IndexOf(u) + 1);
    }

    void AddTab(string name, string icon, AcUnit? unit, bool music, bool pc = false)
    {
        var (box, strokes) = Draw.LineIcon(icon, 20);
        var dot = new Ellipse { Width = 8, Height = 8, StrokeThickness = 1.5, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -2, -5, 0) };
        var label = new TextBlock { Text = name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 84 };
        var bar = new Border { Width = 18, Height = 3, CornerRadius = new CornerRadius(1.5), VerticalAlignment = VerticalAlignment.Bottom };
        var face = new Grid
        {
            Height = 56, CornerRadius = new CornerRadius(10), Padding = new Thickness(4, 6, 4, 0),
            Children =
            {
                new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Children = { new Grid { HorizontalAlignment = HorizontalAlignment.Center, Children = { box, dot } }, label } },
                bar,
            },
        };
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["BareButton"], CornerRadius = new CornerRadius(10),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = face,
        };
        AutomationProperties.SetName(button, name);
        ToolTipService.SetToolTip(button, unit?.Config.DisplayName ?? (music ? "Music sync" : pc ? "PC lighting" : "Light"));
        var tab = new Tab { Button = button, Face = face, Dot = dot, Bar = bar, Name = label, Icon = strokes, Unit = unit, IsMusic = music, IsPc = pc };
        button.Click += (_, _) =>
        {
            current = tab;
            ShowTab();
        };
        Grid.SetColumn(button, tabs.Count);
        Tabs.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Tabs.Children.Add(button);
        tabs.Add(tab);
    }

    /// <summary>Builds the controls for the selected tab.</summary>
    void ShowTab()
    {
        if (current == null) return;
        updaters.Clear();
        titles.Clear();
        Controls.Children.Clear();
        TempRow.Visibility = current.Unit != null ? Visibility.Visible : Visibility.Collapsed;
        if (current.Unit is { } unit) BuildAc(unit);
        else if (current.IsMusic) BuildMusic();
        else if (current.IsPc) BuildPc();
        else BuildLight();
        Refresh();
    }

    // ------------------------------------------------------------ building blocks

    /// <summary>A titled group of tiles in a grid of equal columns.</summary>
    void AddGroup(string title, int columns, IReadOnlyList<FrameworkElement> items, Func<string>? liveTitle = null)
    {
        var heading = new TextBlock { Text = title, FontSize = 12, LineHeight = 16 };
        titles.Add(heading);
        if (liveTitle != null) updaters.Add(_ => heading.Text = liveTitle());
        var grid = new AdaptiveGrid { MinItemWidth = 1, MaxColumns = columns, ColumnSpacing = 6, RowSpacing = 6 };
        AutomationProperties.SetName(grid, title);
        foreach (var item in items) grid.Children.Add(item);
        Controls.Children.Add(new StackPanel { Spacing = 6, Children = { heading, grid } });
    }

    static Color TraySurface(Palette p) => p.Dark ? Paint.Hex(0x323232) : Paint.Hex(0xFFFFFF);

    static (int Hue, int Sat) HueSat(uint rgb)
    {
        var c = Paint.Hex(rgb);
        var (h, s, _) = ColorMath.RgbToHsv(c.R, c.G, c.B);
        return ((int)Math.Round(h) % 360, (int)Math.Round(s * 100));
    }

    static bool NearHue(int a, int b, int within) => Math.Min(Math.Abs(a - b), 360 - Math.Abs(a - b)) <= within;

    // ------------------------------------------------------------ the light

    static int LightLevel(LightState? st) => st == null ? 50 : st.IsColour ? st.V : st.Brightness;

    static string WarmthName(int temp) => temp < 35 ? "Warm" : temp < 70 ? "Neutral" : "Cool";

    static Color LightColour(LightState? st) =>
        st != null ? Color.FromArgb(255, st.Rgb.R, st.Rgb.G, st.Rgb.B) : Paint.Hex(0xFFC47E);

    void BuildLight()
    {
        ArtHost.Content = bulbFrame;

        var bright = new FillSlider("Brightness", SunIcon) { Minimum = 1, Maximum = 100, LargeChange = 10 };
        bright.ValueChanged += v =>
        {
            var st = Session.Shown ?? new LightState();
            Session.Submit(st.IsColour ? LightCommand.Colour(st.H, st.S, (int)v) : LightCommand.White((int)v, st.Temp));
        };
        Controls.Children.Add(bright);
        updaters.Add(p =>
        {
            var st = Session.Shown;
            bool on = st?.On == true;
            int level = Math.Max(1, LightLevel(st));
            var col = !on ? Grey : st!.IsColour ? LightColour(st) : Paint.Hex(0xE8A33C);
            if (!bright.IsDragging) bright.Value = level;
            bright.Text = st != null ? $"{level}%" : "";
            bright.SetColours(p, null, FillSlider.FillOf(col), p.Text);
            bright.IsEnabled = st != null;
        });

        // Warmth: the track itself runs warm to cool, with just the grip. Moving it switches the light to white.
        var warmth = new FillSlider("Warmth", WarmthIcon) { Minimum = 0, Maximum = 100, LargeChange = 10 };
        var warmTrack = Draw.Gradient(new(0, 0), new(1, 0), (0, Paint.Hex(0xF2A541)), (0.45, Paint.Hex(0xFFE2B8)), (0.65, Paint.Hex(0xFFF8EE)), (1, Paint.Hex(0xC9DDFB)));
        warmth.ValueChanged += v => Session.Submit(LightCommand.White(Math.Max(1, LightLevel(Session.Shown)), (int)v));
        Controls.Children.Add(warmth);
        updaters.Add(p =>
        {
            var st = Session.Shown;
            int temp = st?.Temp ?? 0;
            if (!warmth.IsDragging) warmth.Value = temp;
            warmth.Text = WarmthName(temp);
            warmth.SetColours(p, warmTrack, null, Paint.Hex(0x1B1B1B));
            warmth.IsEnabled = st != null;
            if (st != null) warmth.Opacity = st.IsColour ? 0.6 : 1;
            warmth.Visibility = st?.HasTemp == false ? Visibility.Collapsed : Visibility.Visible;
        });

        var swatches = new List<FrameworkElement>();
        foreach (var (name, rgb) in Hues)
        {
            var (hue, sat) = HueSat(rgb);
            var tile = new SwatchTile(name, Paint.Hex(rgb));
            tile.Click += (_, _) => Session.Submit(LightCommand.Colour(hue, sat, Math.Max(1, LightLevel(Session.Shown))));
            updaters.Add(p =>
            {
                var st = Session.Shown;
                tile.Show(p, st is { On: true, IsColour: true } && NearHue(st.H, hue, 6) && Math.Abs(st.S - sat) <= 8, TraySurface(p));
                tile.IsEnabled = st != null && st.HasColour;
            });
            swatches.Add(tile);
        }
        AddGroup("Colour", 8, swatches);

        var scenes = new List<FrameworkElement>();
        foreach (var scene in LightSession.Scenes)
        {
            var tile = new ChipTile(scene.Name, SceneIcons.GetValueOrDefault(scene.Name), default, row: true, height: 40, labelSize: 13);
            AutomationProperties.SetName(tile, scene.Name + " scene");
            tile.Click += (_, _) => Session.Submit(scene.Command);
            updaters.Add(p =>
            {
                var st = Session.Shown;
                var c = scene.Command;
                bool active = st is { On: true } && (c.Kind == CommandKind.White
                    ? !st.IsColour && st.Brightness == c.Brightness && st.Temp == c.Temp
                    : st.IsColour && st.H == c.H && st.S == c.S && st.V == c.V);
                tile.Colour = p.Accent;
                tile.Show(p, active, st != null, p.OnAccent);
            });
            scenes.Add(tile);
        }
        AddGroup("Scene", 3, scenes);
    }

    void ShowLight(Palette p)
    {
        var st = Session.Shown;
        bool on = st?.On == true;
        var c = LightColour(st);
        string name = string.IsNullOrWhiteSpace(Host.Config.Device.Name) ? "Light" : Host.Config.Device.Name;
        string status = Session.StatusKind != "ok" || st == null ? Session.StatusText
            : Session.MusicRunning ? "Following the music" : st.Describe();
        if (on)
            SetStage(Paint.Hex(0x2A2A2E), Paint.Hex(0x141416),
                Draw.Radial(new(0.5, 0.4), 0.7, 0.7, (0, Paint.WithAlpha(c, 0.4)), (0.45, Paint.WithAlpha(c, 0.1)), (0.7, Paint.WithAlpha(c, 0))),
                name, status, true, "Turn the light off", st != null, p);
        else
            SetStage(Paint.Hex(0x3A3B3F), Paint.Hex(0x242528), null, name, status, false, "Turn the light on", st != null, p);
        bulb.Show(on, c, LightLevel(st) / 100.0);
        AutomationProperties.SetName(ArtHost, on ? $"Light bulb glowing at {LightLevel(st)}%" : "Light bulb, off");
        SetWash(c, on);
    }

    // ------------------------------------------------------------ air conditioners

    void BuildAc(AcUnit unit)
    {
        ArtHost.Content = acFrame;

        // Fan speed, with Auto beside it. Dragging picks a manual speed and turns Auto off.
        var fan = new FillSlider("Fan", FanIcon) { Minimum = 1, Maximum = 5, LargeChange = 1 };
        fan.ValueChanged += v =>
        {
            lastFan[unit.Config.Mac] = (int)v;
            unit.Set(("WdSpd", (int)v));
        };
        var auto = new ChipTile("Auto", null, Ice, height: 48, labelSize: 13) { Width = 64 };
        AutomationProperties.SetName(auto, "Fan auto");
        auto.Click += (_, _) => ToggleFanAuto(unit);
        Grid.SetColumn(auto, 1);
        Controls.Children.Add(new Grid
        {
            ColumnSpacing = 8,
            ColumnDefinitions = { new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }, new ColumnDefinition { Width = GridLength.Auto } },
            Children = { fan, auto },
        });
        updaters.Add(p =>
        {
            var st = unit.Shown;
            int speed = st?.FanSpeed ?? 3;
            bool isAuto = speed == 0;
            if (!isAuto) lastFan[unit.Config.Mac] = speed;
            int shown = isAuto ? lastFan.GetValueOrDefault(unit.Config.Mac, 3) : speed;
            if (!fan.IsDragging) fan.Value = shown;
            fan.Text = st == null ? "" : isAuto ? "Auto" : AcState.FanNames[speed];
            fan.SetColours(p, null, FillSlider.FillOf(isAuto ? Grey : Ice), p.Text);
            fan.IsEnabled = st != null;
            if (st != null) fan.Opacity = isAuto ? 0.75 : 1;
            auto.Show(p, isAuto && st != null, st != null);
        });

        var modes = new List<FrameworkElement>();
        for (int m = 0; m < Modes.Length; m++)
        {
            int mode = m;
            var tile = new ChipTile(AcState.ModeNames[m], Modes[m].Icon, Paint.Hex(Modes[m].Colour));
            tile.Click += (_, _) => unit.Set(("Mod", mode));
            updaters.Add(p => tile.Show(p, unit.Shown?.Mode == mode && unit.Shown != null, unit.Shown != null));
            modes.Add(tile);
        }
        AddGroup("Mode", 5, modes);

        var options = new List<FrameworkElement>();
        foreach (var (name, says, key, icon, colour) in Options)
        {
            var tile = new ChipTile(name, icon, Paint.Hex(colour), row: true, height: 44, labelSize: 13);
            tile.Click += (_, _) => ToggleOption(unit, key);
            updaters.Add(p =>
            {
                var st = unit.Shown;
                bool on = st != null && IsOptionOn(st, key);
                // Turbo only works in Cool and Heat, as on the Climate page.
                bool usable = st != null && (key != "Tur" || st.Mode is AcState.Cool or AcState.Heat);
                tile.Show(p, on && usable, usable);
                AutomationProperties.SetName(tile, $"{says} ({(on ? "on" : "off")})");
                ToolTipService.SetToolTip(tile, key == "Tur" && st != null && !usable ? "Turbo: only in Cool and Heat" : says);
            });
            options.Add(tile);
        }
        AddGroup("Options", 3, options);
    }

    static bool IsOptionOn(AcState st, string key) => key switch
    {
        "SwUpDn" => st.Get(key) == 1 || st.Get(key) is >= 7 and <= 11,
        "SwingLfRig" => st.Get(key) == 1,
        _ => st.Is(key),
    };

    static void ToggleOption(AcUnit unit, string key)
    {
        if (unit.Shown is not { } st) return;
        bool turnOn = !IsOptionOn(st, key);
        // Quiet uses 2 for "on" (1 is an automatic mode on some units). Turbo and Quiet switch each other off, as on the unit.
        if (key == "Tur" && turnOn) unit.Set(("Tur", 1), ("Quiet", 0));
        else if (key == "Quiet" && turnOn) unit.Set(("Quiet", 2), ("Tur", 0));
        // Swing: 1 swings; turning it off lets the unit choose the direction.
        else unit.Set((key, turnOn ? 1 : 0));
    }

    void ShowAc(AcUnit unit, Palette p)
    {
        var st = unit.Shown;
        bool on = st?.On == true, heat = st?.Mode == AcState.Heat, usesTemp = st?.UsesSetTemp ?? false;
        var modeCol = Paint.Hex(Modes[st?.Mode ?? AcState.Cool].Colour);
        string doing = st == null ? "" : !on ? "off" : st.Mode switch
        {
            AcState.Cool => "cooling", AcState.Heat => "heating", AcState.Dry => "drying", AcState.Fan => "fan only", _ => "auto",
        };
        string status = unit.StatusKind != "ok" || st == null ? unit.StatusText
            : st.RoomTemp is { } room ? $"Room {room} °C · {doing}" : char.ToUpper(doing[0]) + doing[1..];
        string label = $"Turn {unit.Config.DisplayName} {(on ? "off" : "on")}";
        if (on && heat) SetStage(Paint.Hex(0xC4702E), Paint.Hex(0xF5A35B), null, unit.Config.DisplayName, status, true, label, true, p);
        else if (on) SetStage(Paint.Hex(0x2C6CB8), Paint.Hex(0x5BB4F5), null, unit.Config.DisplayName, status, true, label, true, p);
        else SetStage(Paint.Hex(0x4A4F57), Paint.Hex(0x2B2E33), null, unit.Config.DisplayName, status, false, label, st != null, p);
        acArt.Show(on, st?.SetTemp, heat);
        AutomationProperties.SetName(ArtHost, $"{unit.Config.DisplayName}{(on ? ", running" : ", off")}");
        SetWash(modeCol, on);

        TempRow.Background = Palette.B(p.Tile);
        TempRow.BorderBrush = Palette.B(p.TileStroke);
        TempDown.Background = TempUp.Background = Palette.B(p.TileButton);
        TempValue.Text = st != null ? $"{st.SetTemp}°" : "--";
        TempValue.Foreground = Palette.B(on && usesTemp ? modeCol : p.Text);
        TempCaption.Foreground = Palette.B(p.Text3);
        TempCaption.Text = st == null ? "Connecting" : !usesTemp ? $"Not used in {AcState.ModeNames[st.Mode]}"
            : on ? $"Target · {AcState.ModeNames[st.Mode]}" : "Off · target";
        TempDown.IsEnabled = usesTemp && st!.SetTemp > AcState.MinTemp;
        TempUp.IsEnabled = usesTemp && st!.SetTemp < AcState.MaxTemp;
    }

    void ToggleFanAuto(AcUnit unit)
    {
        if (unit.Shown is not { } st) return;
        if (st.FanSpeed == 0) unit.Set(("WdSpd", lastFan.GetValueOrDefault(unit.Config.Mac, 3)));
        else
        {
            lastFan[unit.Config.Mac] = st.FanSpeed;
            unit.Set(("WdSpd", 0));
        }
    }

    // ------------------------------------------------------------ music sync

    void BuildMusic()
    {
        ArtHost.Content = speakerFrame;

        var sens = new FillSlider("Sensitivity", MicIcon) { Minimum = 0, Maximum = 100, LargeChange = 10 };
        sens.ValueChanged += v =>
        {
            Prefs.Sensitivity = (int)v;
            sens.Text = Prefs.Sensitivity.ToString();
            Host.ApplyMusicPrefs();
            saveTimer.Stop();
            saveTimer.Start(); // save once the slider settles
        };
        Controls.Children.Add(sens);
        updaters.Add(p =>
        {
            if (!sens.IsDragging) sens.Value = Prefs.Sensitivity;
            sens.Text = Prefs.Sensitivity.ToString();
            sens.SetColours(p, null, FillSlider.FillOf(Violet), p.Text);
        });

        var styles = new List<FrameworkElement>();
        foreach (var (key, name, icon) in Styles)
        {
            var tile = new ChipTile(name, icon, Violet);
            tile.Click += (_, _) => SetPrefs(() => Prefs.Style = key);
            updaters.Add(p => tile.Show(p, Prefs.Style == key));
            styles.Add(tile);
        }
        AddGroup("Style", 4, styles);

        var speeds = new List<FrameworkElement>();
        foreach (int fps in MusicPrefs.Speeds)
        {
            var tile = new ChipTile("/ s", null, Violet, row: true, height: 40, labelSize: 12, num: fps.ToString());
            AutomationProperties.SetName(tile, $"{fps} updates a second");
            ToolTipService.SetToolTip(tile, $"{fps} updates a second");
            tile.Click += (_, _) => SetPrefs(() => Prefs.Fps = fps);
            updaters.Add(p => tile.Show(p, Prefs.Fps == fps));
            speeds.Add(tile);
        }
        AddGroup("Update speed · lower is gentler on the light", 4, speeds);

        var pulses = new List<FrameworkElement>();
        for (int i = 0; i < Hues.Length; i++)
        {
            int hue = PulseHues[i];
            var tile = new SwatchTile("Pulse in " + Hues[i].Name.ToLowerInvariant(), Paint.Hex(Hues[i].Rgb));
            // Picking a pulse colour switches to the Pulse style.
            tile.Click += (_, _) => SetPrefs(() =>
            {
                Prefs.Hue = hue;
                Prefs.Style = "pulse";
            });
            updaters.Add(p =>
            {
                tile.Show(p, Prefs.Hue == hue, TraySurface(p));
                tile.Opacity = Prefs.Style == "pulse" ? 1 : 0.6;
            });
            pulses.Add(tile);
        }
        AddGroup("Pulse colour", 8, pulses, () => Prefs.Style == "pulse" ? "Pulse colour" : "Pulse colour · for the Pulse style");

        // What to listen to
        var heading = new TextBlock { Text = "Listen to", FontSize = 12, LineHeight = 16 };
        titles.Add(heading);
        var box = new ComboBox { Height = 40, CornerRadius = new CornerRadius(12), HorizontalAlignment = HorizontalAlignment.Stretch, FontSize = 13 };
        AutomationProperties.SetName(box, "Listen to");
        syncing = true;
        var sources = AudioCapture.ListSources();
        foreach (var s in sources) box.Items.Add(new ComboBoxItem { Content = s.Label, Tag = s.Id });
        box.SelectedIndex = Math.Max(0, sources.FindIndex(s => s.Id == Prefs.Source || s.Name == Prefs.Source));
        syncing = false;
        box.SelectionChanged += (_, _) =>
        {
            if (syncing || box.SelectedItem is not ComboBoxItem item) return;
            SetPrefs(() => Prefs.Source = (string)item.Tag);
        };
        updaters.Add(p =>
        {
            box.Background = Palette.B(p.Tile);
            box.BorderBrush = Palette.B(p.TileStroke);
        });
        Controls.Children.Add(new StackPanel { Spacing = 6, Children = { heading, box } });
    }

    void SetPrefs(Action change)
    {
        change();
        SaveMusicPrefs();
        Refresh();
    }

    void ShowMusic(Palette p)
    {
        bool running = Session.MusicRunning || Session.MusicKind == "busy";
        string status = Session.MusicKind == "error" ? Session.MusicText
            : running ? $"Listening · {Prefs.Fps} updates a second" : "Stopped";
        if (running)
            SetStage(Paint.Hex(0x24202E), Paint.Hex(0x121116),
                Draw.Radial((0, Paint.Hex(0x8A5BE0, 0x66)), (0.5, Paint.Hex(0x3E6FE0, 0x26)), (0.75, Paint.Hex(0x3E6FE0, 0))),
                "Music sync", status, true, "Stop music sync", true, p);
        else
            SetStage(Paint.Hex(0x2E2F33), Paint.Hex(0x1A1B1E), null, "Music sync", status, false, "Start music sync", true, p);
        speaker.Show(running && AppWindow.IsVisible);
        AutomationProperties.SetName(ArtHost, running ? "Speaker, glowing while music sync runs" : "Speaker, stopped");
        SetWash(Violet, running);
    }

    // ------------------------------------------------------------ PC lighting

    static PcLightingPrefs PcPrefs => Host.Config.Pc;

    void BuildPc()
    {
        ArtHost.Content = pcFrame;

        var bright = new FillSlider("Brightness", SunIcon) { Minimum = 1, Maximum = 100, LargeChange = 10 };
        bright.ValueChanged += v =>
        {
            PcPrefs.Brightness = (int)v;
            PcPrefs.On = true;
            Host.ApplyPcLighting();
        };
        Controls.Children.Add(bright);
        updaters.Add(p =>
        {
            var st = Host.PcState();
            bool on = PcPrefs.Enabled && st.On;
            if (!bright.IsDragging) bright.Value = st.Brightness;
            bright.Text = $"{st.Brightness}%";
            bright.SetColours(p, null, FillSlider.FillOf(on ? Color.FromArgb(255, st.R, st.G, st.B) : Grey), p.Text);
            bright.IsEnabled = PcPrefs.Enabled;
        });

        var swatches = new List<FrameworkElement>();
        foreach (var (name, rgb) in PcColours)
        {
            var tile = new SwatchTile(name, Paint.Hex(rgb));
            tile.Click += (_, _) =>
            {
                PcPrefs.Rgb = (int)rgb;
                PcPrefs.On = true;
                PcPrefs.FollowRoom = false;
                Host.ApplyPcLighting();
            };
            updaters.Add(p =>
            {
                tile.Show(p, PcPrefs.Enabled && !PcPrefs.FollowRoom && PcPrefs.Rgb == (int)rgb, TraySurface(p));
                tile.IsEnabled = PcPrefs.Enabled;
            });
            swatches.Add(tile);
        }
        AddGroup("Colour", 8, swatches);

        var music = new ChipTile("Music sync", NoteIcon, Violet, row: true, height: 44, labelSize: 13);
        music.Click += (_, _) => { PcPrefs.FollowMusic = !PcPrefs.FollowMusic; Host.ApplyPcLighting(); };
        var room = new ChipTile("Room light", BulbIcon, Paint.Hex(0xE8A33C), row: true, height: 44, labelSize: 13);
        room.Click += (_, _) => { PcPrefs.FollowRoom = !PcPrefs.FollowRoom; Host.ApplyPcLighting(); };
        updaters.Add(p =>
        {
            music.Show(p, PcPrefs.FollowMusic, PcPrefs.Enabled);
            room.Show(p, PcPrefs.FollowRoom, PcPrefs.Enabled);
        });
        AddGroup("Follow", 2, [music, room]);
    }

    void ShowPc(Palette p)
    {
        var st = Host.PcState();
        bool enabled = PcPrefs.Enabled, on = enabled && st.On;
        var c = Color.FromArgb(255, st.R, st.G, st.B);
        var pc = Host.Pc;
        string status = !enabled ? "Handed back to the devices' own lighting"
            : pc.StatusKind is "error" or "conflict" ? pc.StatusText
            : !on ? "Off"
            : PcPrefs.FollowMusic && Session.MusicRunning ? "Following music sync"
            : pc.CoolingText.Length > 0 ? pc.CoolingText
            : $"{st.Brightness}%";
        if (on)
            SetStage(Paint.Hex(0x2A2A2E), Paint.Hex(0x141416),
                Draw.Radial(new(0.5, 0.4), 0.7, 0.7, (0, Paint.WithAlpha(c, 0.35)), (0.45, Paint.WithAlpha(c, 0.09)), (0.7, Paint.WithAlpha(c, 0))),
                "PC lighting", status, true, "Turn the PC lights off", enabled, p);
        else
            SetStage(Paint.Hex(0x3A3B3F), Paint.Hex(0x242528), null, "PC lighting", status, false, "Turn the PC lights on", enabled, p);
        pcArt.Show(on, c, st.Brightness / 100.0);
        AutomationProperties.SetName(ArtHost, on ? $"PC lit at {st.Brightness}%" : "PC lights off");
        SetWash(c, on);
    }

    // ------------------------------------------------------------ showing the state

    void Refresh()
    {
        if (current == null) return;
        syncing = true;
        try
        {
            var p = Palette.For(Root);
            var green = p.Dark ? Paint.Hex(0x6CCB5F) : Paint.Hex(0x0F7B0F);

            // Tabs: the chosen one raised with a bar under it; a green dot on devices that are on.
            foreach (var t in tabs)
            {
                bool sel = t == current;
                bool on = t.Unit != null ? t.Unit.Shown?.On == true : t.IsMusic ? Session.MusicRunning
                        : t.IsPc ? PcPrefs.Enabled && PcPrefs.On : Session.Shown?.On == true;
                t.Face.Background = Palette.B(sel ? (p.Dark ? Paint.Hex(0x3A3A3A) : Paint.Hex(0xFFFFFF)) : Paint.Hex(0, 0));
                t.Bar.Background = Palette.B(sel ? p.Accent : Paint.Hex(0, 0));
                t.Name.Foreground = Palette.B(sel ? p.Text : p.Text2);
                t.Name.FontWeight = sel ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
                Draw.Tint(t.Icon, sel ? p.Text : p.Text2);
                t.Dot.Fill = Palette.B(on ? green : Paint.Hex(0, 0));
                t.Dot.Stroke = Palette.B(on ? (p.Dark ? Paint.Hex(0x2C2C2C) : Paint.Hex(0xF9F9F9)) : Paint.Hex(0, 0));
                AutomationProperties.SetItemStatus(t.Button, (sel ? "Selected" : "") + (on ? (sel ? ", on" : "On") : ""));
            }

            if (current.Unit is { } unit) ShowAc(unit, p);
            else if (current.IsMusic) ShowMusic(p);
            else if (current.IsPc) ShowPc(p);
            else ShowLight(p);
            foreach (var t in titles) t.Foreground = Palette.B(p.Text3);
            foreach (var update in updaters) update(p);

            // Footer
            int total = 1 + Host.Acs.Units.Count;
            int connected = (Session.StatusKind == "ok" ? 1 : 0) + Host.Acs.Units.Count(u => u.StatusKind == "ok");
            bool waiting = Session.StatusKind == "busy" || Host.Acs.Units.Any(u => u.StatusKind == "busy");
            FooterText.Text = connected == total ? "All devices connected" : waiting ? "Connecting…" : $"{connected} of {total} devices connected";
            FooterDot.Fill = Palette.B(connected == total ? green : waiting ? p.Text3 : (p.Dark ? Paint.Hex(0xFCE100) : Paint.Hex(0x9D5D00)));
        }
        finally
        {
            syncing = false;
        }
    }

    /// <summary>A soft wash of the device's colour from the top of the control area; none when it's off.</summary>
    void SetWash(Color c, bool on) => Wash.Background = on
        ? Draw.Radial(new(0.5, 0), 1.4, 0.7, (0, Paint.WithAlpha(c, 0.2)), (0.55, Paint.WithAlpha(c, 0.063)), (1, Paint.WithAlpha(c, 0)))
        : null;

    void SetStage(Color top, Color bottom, Brush? glow, string name, string status, bool on, string powerLabel, bool canPower, Palette p)
    {
        StageBase.Background = Draw.Vertical((0, top), (1, bottom));
        StageGlow.Background = glow;
        StageName.Text = name;
        StageStatus.Text = status;
        // The big power button: the accent when on, a white disc when off.
        PowerFace.Background = Palette.B(on ? p.Accent : Paint.Hex(0xFFFFFF, 0xEB));
        PowerFace.BorderBrush = Palette.B(on ? p.Accent : Paint.Hex(0, 0x14));
        PowerIcon.Foreground = Palette.B(on ? p.OnAccent : Paint.Hex(0x1B1B1B));
        PowerButton.IsEnabled = canPower;
        PowerButton.Opacity = canPower ? 1 : 0.6;
        AutomationProperties.SetName(PowerButton, powerLabel);
        AutomationProperties.SetItemStatus(PowerButton, on ? "On" : "Off");
        ToolTipService.SetToolTip(PowerButton, powerLabel);
    }

    // ------------------------------------------------------------ changes

    void Power_Click(object sender, RoutedEventArgs e)
    {
        if (current?.Unit is { } unit) unit.TogglePower();
        else if (current?.IsMusic == true) Session.ToggleMusic();
        else if (current?.IsPc == true)
        {
            PcPrefs.On = !PcPrefs.On;
            Host.ApplyPcLighting();
        }
        else if (Session.Shown != null) Session.TogglePower();
        Refresh();
    }

    void TempDown_Click(object sender, RoutedEventArgs e) => NudgeTemp(-1);
    void TempUp_Click(object sender, RoutedEventArgs e) => NudgeTemp(+1);

    void NudgeTemp(int delta)
    {
        if (current?.Unit is not { Shown: { } st } unit) return;
        unit.Set(("SetTem", Math.Clamp(st.SetTemp + delta, AcState.MinTemp, AcState.MaxTemp)), ("TemUn", 0));
    }

    static void SaveMusicPrefs()
    {
        App.Instance!.ApplyMusicPrefs();
        try { App.Instance.Config.Save(); } catch (Exception e) { App.Log("Saving settings failed: " + e.Message); }
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        HidePanel();
        if (current?.Unit is { } unit) Host.Window.ShowAc(unit);
        else
        {
            Host.Window.ShowPage(current?.IsMusic == true ? "music" : current?.IsPc == true ? "pc" : "light");
            Host.Window.ShowAndFocus();
        }
    }

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        HidePanel();
        Host.Window.ShowPage("settings");
        Host.Window.ShowAndFocus();
    }

    // ------------------------------------------------------------ screenshots (for checking the design)

    /// <summary>Captures every tab in light and dark, off screen.</summary>
    public async Task CaptureAsync(string dir, Func<UIElement, string, Task> save)
    {
        AppWindow.MoveAndResize(new RectInt32(-30000, -30000, (int)(PanelWidth * WinIntegration.GetDpiForWindow(hwnd) / 96.0),
                                              (int)(PanelHeight * WinIntegration.GetDpiForWindow(hwnd) / 96.0)));
        AppWindow.Show();
        await Task.Delay(800);
        foreach (var theme in new[] { "light", "dark" })
        {
            ApplyTheme(theme);
            // Acrylic isn't captured: paint its usual colour instead.
            Root.Background = new SolidColorBrush(theme == "dark" ? Paint.Hex(0x2C2C2C) : Paint.Hex(0xF9F9F9));
            for (int i = 0; i < tabs.Count; i++)
            {
                current = tabs[i];
                ShowTab();
                await Task.Delay(500);
                await save(Root, System.IO.Path.Combine(dir, $"tray-{i}-{theme}.png"));
            }
        }
        AppWindow.Hide();
    }
}
