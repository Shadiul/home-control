using LedControl.Art;
using LedControl.Controls;
using LedControl.Core;
using LedControl.Music;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace LedControl.Pages;

/// <summary>
/// Music sync: Start / Stop, a live spectrum with the light's colour, the four styles as cards, and options
/// (sensitivity, audio source, update speed, Pulse colour).
/// </summary>
public sealed partial class MusicPage : Page
{
    static readonly (string Key, string Name, string Desc)[] Styles =
    [
        ("pulse", "Pulse", "One colour that breathes with the volume"),
        ("spectrum", "Spectrum", "Bass shows red, mids green, treble blue"),
        ("beat", "Beat", "A new colour and a flash on every beat"),
        ("party", "Party", "Colours spin faster as the music gets louder"),
    ];
    // Pulse colours: the hue sent to the light, and the colour shown (the shared swatch colours).
    static readonly (int Hue, string Name, uint Rgb)[] PulseColourList =
    [
        (0, "Red", 0xE8453C), (25, "Orange", 0xF2802E), (50, "Yellow", 0xF2D33C), (130, "Green", 0x3DAA67),
        (190, "Teal", 0x2BB5A8), (230, "Blue", 0x3E6FE0), (280, "Purple", 0xA85BD8), (320, "Pink", 0xE05B9E),
    ];

    readonly Rectangle[] bars = new Rectangle[Analyzer.BarCount];
    readonly List<(Border Card, RadioButton Radio, string Key)> styleCards = [];
    readonly List<(ToggleButton Button, int Fps)> speedButtons = [];
    readonly List<(SwatchTile Tile, int Hue)> pulseButtons = [];
    List<AudioCapture.Source> sources = [];
    bool syncing, visible;

    static LightSession Session => App.Instance!.Session;
    static MusicPrefs Prefs => App.Instance!.Config.Music;

    public MusicPage()
    {
        syncing = true; // controls raise change events while the XAML loads
        InitializeComponent();

        for (int i = 0; i < bars.Length; i++)
        {
            BarsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var r = new Rectangle { RadiusX = 2, RadiusY = 2, VerticalAlignment = VerticalAlignment.Bottom, Height = 6 };
            Grid.SetColumn(r, i);
            BarsGrid.Children.Add(r);
            bars[i] = r;
        }

        foreach (var (key, name, desc) in Styles)
        {
            var radio = new RadioButton
            {
                GroupName = "MusicStyle", MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
                Content = new StackPanel
                {
                    Spacing = 2,
                    Children =
                    {
                        new TextBlock { Text = name, FontSize = 14 },
                        new TextBlock { Text = desc, FontSize = 12, TextWrapping = TextWrapping.Wrap, Opacity = 0.75 },
                    },
                },
            };
            AutomationProperties.SetName(radio, $"{name}: {desc}");
            radio.Checked += (_, _) => PickStyle(key);
            var card = new Border { Padding = new Thickness(14, 10, 14, 12), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = radio };
            card.Tapped += (_, _) => radio.IsChecked = true;
            StyleGrid.Children.Add(card);
            styleCards.Add((card, radio, key));
        }

        for (int i = 0; i < MusicPrefs.Speeds.Length; i++)
        {
            int fps = MusicPrefs.Speeds[i];
            var b = new ToggleButton
            {
                Content = fps.ToString(), Width = 52, Height = 32, Padding = new Thickness(0),
                CornerRadius = new CornerRadius(i == 0 ? 4 : 0, i == MusicPrefs.Speeds.Length - 1 ? 4 : 0, i == MusicPrefs.Speeds.Length - 1 ? 4 : 0, i == 0 ? 4 : 0),
                Margin = new Thickness(i == 0 ? 0 : -1, 0, 0, 0),
            };
            AutomationProperties.SetName(b, $"{fps} a second");
            b.Click += (_, _) =>
            {
                Prefs.Fps = fps;
                SavePrefs();
                LoadPrefs();
            };
            SpeedButtons.Children.Add(b);
            speedButtons.Add((b, fps));
        }

        foreach (var (hue, name, rgb) in PulseColourList)
        {
            var tile = new SwatchTile("Pulse in " + name.ToLowerInvariant(), Paint.Hex(rgb));
            tile.Click += (_, _) => PickPulse(hue);
            Grid.SetColumn(tile, pulseButtons.Count);
            PulseColours.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            PulseColours.Children.Add(tile);
            pulseButtons.Add((tile, hue));
        }
        MusicError.Closed += (_, _) => MusicError.Visibility = Visibility.Collapsed;

        syncing = false;
        LoadPrefs();
        Session.MusicChanged += SyncState;
        Session.Changed += SyncState;
        Session.MusicFrame += OnFrame;
        Loaded += (_, _) =>
        {
            visible = true;
            LoadSources();
            LoadPrefs();
            SyncState();
        };
        Unloaded += (_, _) => visible = false;
        ActualThemeChanged += (_, _) =>
        {
            LoadPrefs();
            SyncState();
        };
    }

    public static Color ToColor((byte R, byte G, byte B) c) => Color.FromArgb(255, c.R, c.G, c.B);

    // ------------------------------------------------------------ settings

    void LoadPrefs()
    {
        syncing = true;
        var p = Palette.For(this);
        var m = Prefs;
        foreach (var (card, radio, key) in styleCards)
        {
            bool on = key == m.Style;
            radio.IsChecked = on;
            card.Background = Palette.B(p.Ctrl);
            card.BorderBrush = Palette.B(on ? p.Accent : p.CtrlStroke);
        }
        SensSlider.Value = m.Sensitivity;
        SensText.Text = m.Sensitivity.ToString();
        foreach (var (b, fps) in speedButtons) b.IsChecked = fps == m.Fps;
        foreach (var (tile, hue) in pulseButtons) tile.Show(p, hue == m.Hue);
        PulseRow.Opacity = m.Style == "pulse" ? 1 : 0.55;
        syncing = false;
    }

    void LoadSources()
    {
        syncing = true;
        sources = AudioCapture.ListSources();
        SourceBox.Items.Clear();
        foreach (var s in sources) SourceBox.Items.Add(new ComboBoxItem { Content = s.Label, Tag = s.Id });
        string saved = Prefs.Source;
        int index = sources.FindIndex(s => s.Id == saved || s.Name == saved);
        SourceBox.SelectedIndex = Math.Max(0, index);
        syncing = false;
    }

    static void SavePrefs()
    {
        App.Instance!.ApplyMusicPrefs();
        try { App.Instance.Config.Save(); } catch (Exception e) { App.Log("Saving settings failed: " + e.Message); }
    }

    void PickStyle(string key)
    {
        if (syncing || Prefs.Style == key) return;
        Prefs.Style = key;
        SavePrefs();
        LoadPrefs();
    }

    void Sens_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (syncing) return;
        Prefs.Sensitivity = (int)SensSlider.Value;
        SensText.Text = Prefs.Sensitivity.ToString();
        SavePrefs();
    }

    void PickPulse(int hue)
    {
        Prefs.Hue = hue;
        Prefs.Style = "pulse"; // picking a pulse colour implies the Pulse style
        SavePrefs();
        LoadPrefs();
    }

    void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (syncing || SourceBox.SelectedItem is not ComboBoxItem item) return;
        Prefs.Source = (string)item.Tag;
        SavePrefs();
    }

    // ------------------------------------------------------------ running

    void Start_Click(object sender, RoutedEventArgs e)
    {
        MusicError.IsOpen = false;
        MusicError.Visibility = Visibility.Collapsed;
        Session.ToggleMusic();
        SyncState();
    }

    /// <summary>Bar colours follow the style, as on the design: one colour, red/green/blue thirds, amber and pink, or a rainbow.</summary>
    Color BarColour(int i, Palette p) => Prefs.Style switch
    {
        "spectrum" => i < bars.Length * 0.33 ? Paint.Hex(0xE8603C) : i < bars.Length * 0.67 ? Paint.Hex(0x3DAA67) : Paint.Hex(0x3A84E8),
        "beat" => i % 10 < 5 ? Paint.Hex(0xE8A23C) : Paint.Hex(0xD9579E),
        "party" => Paint.Hex(new uint[] { 0xE8603C, 0xE8A23C, 0x3DAA67, 0x2BB5A8, 0x3A84E8, 0x8A5BE0 }[i % 6]),
        _ => p.Accent,
    };

    void SyncState()
    {
        bool running = Session.MusicRunning;
        string kind = Session.MusicKind;
        var p = Palette.For(this);
        StartText.Text = running ? "Stop" : "Start";
        StartIcon.Glyph = running ? "" : "";
        StartButton.Style = (Style)Application.Current.Resources[running ? "DefaultButtonStyle" : "AccentButtonStyle"];
        AutomationProperties.SetName(StartButton, running ? "Stop music sync" : "Start music sync");
        MusicBusy.IsActive = kind == "busy";
        MusicBusy.Visibility = kind == "busy" ? Visibility.Visible : Visibility.Collapsed;
        if (kind == "error")
        {
            MusicError.Message = Session.MusicText;
            MusicError.IsOpen = true;
            MusicError.Visibility = Visibility.Visible;
        }
        else if (kind == "ok")
        {
            MusicError.IsOpen = false;
            MusicError.Visibility = Visibility.Collapsed;
        }

        if (running)
        {
            MusicStatus.Text = $"{Session.MusicText} · {Prefs.Fps} updates a second";
            for (int i = 0; i < bars.Length; i++) bars[i].Fill = Palette.B(BarColour(i, p));
        }
        else
        {
            var st = Session.Shown;
            MusicStatus.Text = kind == "busy" ? Session.MusicText
                : st == null ? "Stopped." : $"Stopped. The light is {st.Describe().ToLowerInvariant()}.";
            var idle = Palette.B(p.Dark ? Paint.Hex(0x4A4A4A) : Paint.Hex(0xD9D9D9));
            foreach (var r in bars)
            {
                r.Height = 6;
                r.Fill = idle;
            }
            ColourChip.Fill = Palette.B(st?.On == true ? ToColor(st.Rgb) : p.Ctrl);
        }
    }

    void OnFrame(MusicFrame f)
    {
        if (!visible || !Session.MusicRunning) return;
        double max = Math.Max(10, BarsGrid.ActualHeight - 6);
        for (int i = 0; i < bars.Length && i < f.Bars.Length; i++) bars[i].Height = 6 + f.Bars[i] * max;
        ColourChip.Fill = new SolidColorBrush(ToColor(ColorMath.HsvToRgb(f.H, f.S, Math.Max(0.08, f.V))));
    }
}
