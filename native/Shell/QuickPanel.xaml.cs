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
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace LedControl.Shell;

/// <summary>
/// The quick panel: click the notification-area icon to open it just above the taskbar. One tab per device
/// (light, each air conditioner, music sync) with a picture of the device lit by its state, a big power button,
/// its main controls, and chips for colours, modes or styles. It hides when you click anywhere else.
/// </summary>
public sealed partial class QuickPanel : Window
{
    const double PanelWidth = 380, PanelHeight = 680;
    const string BulbIcon = "M9 18h6M10 21h4M12 3a6 6 0 0 0-3.6 10.8c.7.6 1.1 1.3 1.1 2.2v.5h5V16c0-.9.4-1.6 1.1-2.2A6 6 0 0 0 12 3z";
    const string SnowIcon = "M12 2.5v19M3.8 7.3l16.4 9.4M20.2 7.3 3.8 16.7M9.5 4.5 12 6l2.5-1.5M9.5 19.5 12 18l2.5 1.5";
    const string NoteIcon = "M9 18V5.5l11-2V16M6.5 20.5a2.5 2.5 0 1 1 0-5 2.5 2.5 0 0 1 0 5zM17.5 18.5a2.5 2.5 0 1 1 0-5 2.5 2.5 0 0 1 0 5z";

    static readonly (string Name, uint Rgb, int Temp)[] LightChips =
        [("Warm", 0xFFC47E, 0), ("Cool", 0xEEF4FF, 100), ("Red", 0xE8453C, -1), ("Green", 0x3DAA67, -1), ("Blue", 0x3E6FE0, -1), ("Purple", 0xA85BD8, -1)];

    static readonly string[] ModeIcons =
    [
        "M20 12a8 8 0 0 1-14.3 4.9M4 12a8 8 0 0 1 14.3-4.9M18.5 3.5v3.6h-3.6M5.5 20.5v-3.6h3.6",
        SnowIcon,
        "M12 3.5s6 6.4 6 10.9a6 6 0 0 1-12 0c0-4.5 6-10.9 6-10.9z",
        "M12 12c-1-4 0-8 3-8.5s3 4.5-3 8.5zM12 12c4-1 8 0 8.5 3s-4.5 3-8.5-3zM12 12c1 4 0 8-3 8.5s-3-4.5 3-8.5zM12 12c-4 1-8 0-8.5-3s4.5-3 8.5 3z",
        "M12 3c.8 3.6 5 5.2 5 10a5 5 0 0 1-10 0c0-2.4 1.3-3.9 2.4-4.9.4 1.9 1.4 2.9 2.4 2.9-.2-3 .2-5.4.2-8z",
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
        public AcUnit? Unit;      // null: the light or music sync
        public bool IsMusic;
    }

    readonly IntPtr hwnd;
    readonly List<Tab> tabs = [];
    readonly List<(ButtonBase Button, Path[]? Icon, Func<bool> Selected, bool Swatch)> chips = [];
    readonly BigBulb bulb = new();
    readonly AcFrontArt acArt = new();
    readonly SpeakerCone speaker = new();
    readonly Viewbox bulbFrame, acFrame;
    readonly DispatcherQueueTimer saveTimer;
    Tab? current;
    bool syncing;
    long hiddenAt;

    static App Host => App.Instance!;
    static LightSession Session => Host.Session;

    public QuickPanel()
    {
        InitializeComponent();
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        bulbFrame = new Viewbox { Height = 140, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 14, 0, 0), Child = bulb };
        acFrame = new Viewbox { Height = 150, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 8, 0, 0), Child = acArt };
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
        Session.MusicFrame += OnMusicFrame;
        Host.Acs.Changed += Refresh;
        Host.Acs.ListChanged += BuildTabs;
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
        Refresh();
        AppWindow.Show();
        Activate();
        WinIntegration.SetForegroundWindow(hwnd);
    }

    void HidePanel()
    {
        hiddenAt = Environment.TickCount64;
        AppWindow.Hide();
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
        current = tabs.FirstOrDefault(t => keep != null && t.Unit == keep.Unit && t.IsMusic == keep.IsMusic && t.Unit != null)
               ?? tabs.FirstOrDefault(t => keep?.IsMusic == true && t.IsMusic)
               ?? tabs[0];
        ShowTab();
    }

    /// <summary>A short tab name for a unit: its name, or "AC 1", "AC 2" when it has none.</summary>
    static string ShortName(AcUnit u)
    {
        if (!string.IsNullOrWhiteSpace(u.Config.Name)) return u.Config.Name.Trim();
        return "AC " + (App.Instance!.Acs.Units.IndexOf(u) + 1);
    }

    void AddTab(string name, string icon, AcUnit? unit, bool music)
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
        ToolTipService.SetToolTip(button, unit?.Config.DisplayName ?? (music ? "Music sync" : "Light"));
        var tab = new Tab { Button = button, Face = face, Dot = dot, Bar = bar, Name = label, Icon = strokes, Unit = unit, IsMusic = music };
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
        syncing = true;
        chips.Clear();
        Chips.Children.Clear();
        Chips.ColumnDefinitions.Clear();
        TempRow.Visibility = current.Unit != null ? Visibility.Visible : Visibility.Collapsed;

        if (current.Unit is { } unit)
        {
            ArtHost.Content = acFrame;
            SliderLabel.Text = "Fan speed";
            MainSlider.Minimum = 1;
            MainSlider.Maximum = 5;
            AutomationProperties.SetName(MainSlider, "Fan speed");
            ChipsTitle.Text = "Mode";
            for (int m = 0; m < AcState.ModeNames.Length; m++)
            {
                int mode = m;
                AddIconChip(AcState.ModeNames[m], ModeIcons[m], () => unit.Shown?.Mode == mode && unit.Shown != null, () => unit.Set(("Mod", mode)));
            }
            AddIconChip("Fan auto", "M20 12a8 8 0 0 1-14.3 4.9M4 12a8 8 0 0 1 14.3-4.9", () => unit.Shown?.FanSpeed == 0,
                () => ToggleFanAuto(unit));
        }
        else if (current.IsMusic)
        {
            ArtHost.Content = speaker;
            SliderLabel.Text = "Sensitivity";
            MainSlider.Minimum = 0;
            MainSlider.Maximum = 100;
            AutomationProperties.SetName(MainSlider, "Sensitivity");
            ChipsTitle.Text = "Style";
            foreach (var (key, name, icon) in Styles)
                AddIconChip(name, icon, () => Host.Config.Music.Style == key, () =>
                {
                    Host.Config.Music.Style = key;
                    SaveMusicPrefs();
                    Refresh();
                });
        }
        else
        {
            ArtHost.Content = bulbFrame;
            SliderLabel.Text = "Brightness";
            MainSlider.Minimum = 1;
            MainSlider.Maximum = 100;
            AutomationProperties.SetName(MainSlider, "Brightness");
            ChipsTitle.Text = "Colour";
            foreach (var (name, rgb, temp) in LightChips) AddSwatchChip(name, rgb, temp);
        }
        syncing = false;
        Refresh();
    }

    // ------------------------------------------------------------ chips

    void AddChip(ButtonBase button, Path[]? icon, Func<bool> selected, bool swatch)
    {
        Grid.SetColumn(button, chips.Count);
        Chips.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Chips.Children.Add(button);
        chips.Add((button, icon, selected, swatch));
    }

    void AddIconChip(string name, string icon, Func<bool> selected, Action pick)
    {
        var (box, strokes) = Draw.LineIcon(icon, 18);
        var b = new ToggleButton
        {
            Height = 52, CornerRadius = new CornerRadius(10), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center, Children = { box, new TextBlock { Text = name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center } } },
        };
        AutomationProperties.SetName(b, name);
        ToolTipService.SetToolTip(b, name);
        b.Click += (_, _) => pick();
        AddChip(b, strokes, selected, false);
    }

    void AddSwatchChip(string name, uint rgb, int temp)
    {
        var c = Paint.Hex(rgb);
        var b = new Button
        {
            Height = 52, CornerRadius = new CornerRadius(10), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new StackPanel
            {
                Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center,
                Children =
                {
                    new Ellipse { Width = 26, Height = 26, Fill = new SolidColorBrush(c), Stroke = Paint.Brush(0, 0x1A), StrokeThickness = 1 },
                    new TextBlock { Text = name, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center },
                },
            },
        };
        var (h, s, _) = ColorMath.RgbToHsv(c.R, c.G, c.B);
        int hue = (int)Math.Round(h) % 360, sat = (int)Math.Round(s * 100);
        AutomationProperties.SetName(b, name);
        ToolTipService.SetToolTip(b, name);
        b.Click += (_, _) =>
        {
            int level = (int)MainSlider.Value;
            Session.Submit(temp >= 0 ? LightCommand.White(level, temp) : LightCommand.Colour(hue, sat, level));
        };
        AddChip(b, null, () =>
        {
            var st = Session.Shown;
            if (st == null || !st.On) return false;
            return temp >= 0 ? !st.IsColour && Math.Abs(st.Temp - temp) <= 10
                             : st.IsColour && Math.Min(Math.Abs(st.H - hue), 360 - Math.Abs(st.H - hue)) <= 6 && Math.Abs(st.S - sat) <= 8;
        }, true);
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
                bool on = t.Unit != null ? t.Unit.Shown?.On == true : t.IsMusic ? Session.MusicRunning : Session.Shown?.On == true;
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
            else ShowLight(p);

            foreach (var (button, icon, selected, swatch) in chips)
            {
                bool sel = selected();
                if (button is ToggleButton tb) tb.IsChecked = sel;
                if (icon != null) Draw.Tint(icon, sel ? p.OnAccent : p.Text);
                if (swatch)
                {
                    button.BorderBrush = sel ? Palette.B(p.Accent) : null;
                    button.BorderThickness = new Thickness(sel ? 2 : 1);
                }
                AutomationProperties.SetItemStatus(button, sel ? "Selected" : "");
            }

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

    void SetStage(Color top, Color bottom, Brush? glow, string name, string status, bool on, string powerLabel, bool canPower)
    {
        StageBase.Background = Draw.Vertical((0, top), (1, bottom));
        StageGlow.Background = glow;
        StageName.Text = name;
        StageStatus.Text = status;
        PowerButton.IsChecked = on;
        PowerButton.IsEnabled = canPower;
        AutomationProperties.SetName(PowerButton, powerLabel);
        ToolTipService.SetToolTip(PowerButton, powerLabel);
    }

    void SetSlider(double value, string text, Color fill, bool enabled)
    {
        MainSlider.Value = value;
        SliderValue.Text = text;
        MainSlider.IsEnabled = enabled;
        SliderBox.Opacity = enabled ? 1 : 0.45;
        var brush = new SolidColorBrush(fill);
        // The filled part takes the device's colour. (The thumb keeps the accent: its colour can't be changed
        // once it's drawn, so a per-tab colour would stick from the previous tab.)
        foreach (var state in new[] { "", "PointerOver", "Pressed" }) MainSlider.Resources["SliderTrackValueFill" + state] = brush;
        MainSlider.Foreground = brush;
    }

    void ShowLight(Palette p)
    {
        var st = Session.Shown;
        bool on = st?.On == true, colour = st?.IsColour == true;
        int level = st == null ? 50 : colour ? st.V : st.Brightness;
        var c = st != null ? Color.FromArgb(255, st.Rgb.R, st.Rgb.G, st.Rgb.B) : Paint.Hex(0xFFC47E);
        string name = string.IsNullOrWhiteSpace(Host.Config.Device.Name) ? "Light" : Host.Config.Device.Name;
        string status = Session.StatusKind != "ok" || st == null ? Session.StatusText
            : Session.MusicRunning ? "Following the music" : st.Describe();
        if (on)
            SetStage(Paint.Hex(0x2A2A2E), Paint.Hex(0x141416),
                Draw.Radial(new(0.5, 0.4), 0.7, 0.7, (0, Paint.WithAlpha(c, 0.4)), (0.45, Paint.WithAlpha(c, 0.1)), (0.7, Paint.WithAlpha(c, 0))),
                name, status, true, "Turn the light off", st != null);
        else
            SetStage(Paint.Hex(0x3A3B3F), Paint.Hex(0x242528), null, name, status, false, "Turn the light on", st != null);
        bulb.Show(on, c, level / 100.0);
        AutomationProperties.SetName(ArtHost, on ? $"Light bulb glowing at {level}%" : "Light bulb, off");
        SetSlider(Math.Max(1, level), st != null ? $"{Math.Max(1, level)}%" : "", Paint.Hex(0xE8A33C), st != null);
    }

    void ShowAc(AcUnit unit, Palette p)
    {
        var st = unit.Shown;
        bool on = st?.On == true, heat = st?.Mode == AcState.Heat, usesTemp = st?.UsesSetTemp ?? false;
        string doing = st == null ? "" : !on ? "off" : st.Mode switch
        {
            AcState.Cool => "cooling", AcState.Heat => "heating", AcState.Dry => "drying", AcState.Fan => "fan only", _ => "auto",
        };
        string status = unit.StatusKind != "ok" || st == null ? unit.StatusText
            : st.RoomTemp is { } room ? $"Room {room} °C · {doing}" : char.ToUpper(doing[0]) + doing[1..];
        string label = $"Turn {unit.Config.DisplayName} {(on ? "off" : "on")}";
        if (on && heat) SetStage(Paint.Hex(0xC4702E), Paint.Hex(0xF5A35B), null, unit.Config.DisplayName, status, true, label, true);
        else if (on) SetStage(Paint.Hex(0x2C6CB8), Paint.Hex(0x5BB4F5), null, unit.Config.DisplayName, status, true, label, true);
        else SetStage(Paint.Hex(0x4A4F57), Paint.Hex(0x2B2E33), null, unit.Config.DisplayName, status, false, label, st != null);
        acArt.Show(on, st?.SetTemp, heat);
        AutomationProperties.SetName(ArtHost, $"{unit.Config.DisplayName}{(on ? ", running" : ", off")}");

        TempValue.Text = st != null ? $"{st.SetTemp}°" : "--";
        TempValue.Foreground = Palette.B(on && usesTemp ? p.Accent : p.Text);
        TempCaption.Text = st == null ? "Connecting" : !usesTemp ? $"Not used in {AcState.ModeNames[st.Mode]}"
            : on ? $"Target · {AcState.ModeNames[st.Mode]}" : "Off · target";
        TempDown.IsEnabled = usesTemp && st!.SetTemp > AcState.MinTemp;
        TempUp.IsEnabled = usesTemp && st!.SetTemp < AcState.MaxTemp;

        int speed = st?.FanSpeed ?? 3;
        bool auto = speed == 0;
        int shown = auto ? lastFan.GetValueOrDefault(unit.Config.Mac, 3) : speed;
        if (!auto) lastFan[unit.Config.Mac] = speed;
        SetSlider(shown, st == null ? "" : auto ? "Auto" : AcState.FanNames[speed], p.Accent, st != null && !auto);
    }

    readonly Dictionary<string, int> lastFan = [];

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

    void ShowMusic(Palette p)
    {
        bool running = Session.MusicRunning || Session.MusicKind == "busy";
        string status = running ? Session.MusicText : "Stopped";
        if (running)
            SetStage(Paint.Hex(0x24202E), Paint.Hex(0x121116),
                Draw.Radial((0, Paint.Hex(0x8A5BE0, 0x66)), (0.5, Paint.Hex(0x3E6FE0, 0x26)), (0.75, Paint.Hex(0x3E6FE0, 0))),
                "Music sync", status, true, "Stop music sync", true);
        else
            SetStage(Paint.Hex(0x2E2F33), Paint.Hex(0x1A1B1E), null, "Music sync", status, false, "Start music sync", true);
        speaker.Show(running);
        AutomationProperties.SetName(ArtHost, running ? "Speaker, with the music visualiser running" : "Speaker, stopped");
        int sens = Host.Config.Music.Sensitivity;
        SetSlider(sens, sens.ToString(), Paint.Hex(0x8A5BE0), true);
    }

    void OnMusicFrame(MusicFrame f)
    {
        if (AppWindow.IsVisible && current?.IsMusic == true && Session.MusicRunning) speaker.Pulse(f.Bars, f.Bass);
    }

    // ------------------------------------------------------------ changes

    void Power_Click(object sender, RoutedEventArgs e)
    {
        if (current?.Unit is { } unit) unit.TogglePower();
        else if (current?.IsMusic == true) Session.ToggleMusic();
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

    void MainSlider_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (syncing || current == null) return;
        int v = (int)e.NewValue;
        if (current.Unit is { } unit)
        {
            lastFan[unit.Config.Mac] = v;
            unit.Set(("WdSpd", v));
        }
        else if (current.IsMusic)
        {
            Host.Config.Music.Sensitivity = v;
            SliderValue.Text = v.ToString();
            Host.ApplyMusicPrefs();
            saveTimer.Stop();
            saveTimer.Start(); // save once the slider settles
        }
        else
        {
            var st = Session.Shown ?? new LightState();
            Session.Submit(st.IsColour ? LightCommand.Colour(st.H, st.S, v) : LightCommand.White(v, st.Temp));
        }
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
            Host.Window.ShowPage(current?.IsMusic == true ? "music" : "light");
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
