using LedControl.Art;
using LedControl.Controls;
using LedControl.Core;
using LedControl.Music;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LedControl.Pages;

/// <summary>
/// Music sync: Start / Stop, a minimal stage (one orb in the style's colour breathing inside a soft halo, with
/// play / stop in its middle and what the light is doing), the options in a card lit by the style's colour,
/// and the four styles as illustrated cards.
/// </summary>
public sealed partial class MusicPage : Page
{
    static readonly (string Key, string Name, string Desc, uint Colour)[] Styles =
    [
        ("pulse", "Pulse", "One colour that breathes with the volume", 0),
        ("spectrum", "Spectrum", "Bass shows red, mids green, treble blue", 0x3DAA67),
        ("beat", "Beat", "A new colour and a flash on every beat", 0xE8A23C),
        ("party", "Party", "Colours spin faster as the music gets louder", 0x8A5BE0),
    ];

    // Pulse colours: the hue sent to the light, and the colour shown (the shared swatch colours).
    static readonly (int Hue, string Name, uint Rgb)[] PulseColourList =
    [
        (0, "Red", 0xE8453C), (25, "Orange", 0xF2802E), (50, "Yellow", 0xF2D33C), (130, "Green", 0x3DAA67),
        (190, "Teal", 0x2BB5A8), (230, "Blue", 0x3E6FE0), (280, "Purple", 0xA85BD8), (320, "Pink", 0xE05B9E),
    ];

    static readonly Color Violet = Paint.Hex(0x7A4FD6);

    sealed class StyleCard
    {
        public required string Key;
        public required Button Button;
        public required Border Ring, Shadow, Radio;
        public required Grid Face;
        public required StyleArt Art;
    }

    sealed class SpeedItem
    {
        public required int Fps;
        public required Button Button;
        public required Border Face, Bar;
        public required TextBlock Text;
    }

    readonly FillSlider sens = new("Sensitivity", "M12 3a3 3 0 0 0-3 3v6a3 3 0 0 0 6 0V6a3 3 0 0 0-3-3zM5.5 11a6.5 6.5 0 0 0 13 0M12 17.5V21")
    {
        Minimum = 0, Maximum = 100, LargeChange = 10,
    };
    readonly List<StyleCard> styleCards = [];
    readonly List<SpeedItem> speedItems = [];
    readonly List<(SwatchTile Tile, int Hue)> pulseButtons = [];
    List<AudioCapture.Source> sources = [];
    bool syncing, visible;

    /// <summary>For the design screenshots: draw the page as if music sync were running (nothing is started).</summary>
    internal bool PreviewRunning
    {
        get => previewRunning;
        set
        {
            previewRunning = value;
            SyncState();
        }
    }
    bool previewRunning;

    static LightSession Session => App.Instance!.Session;
    static MusicPrefs Prefs => App.Instance!.Config.Music;

    public MusicPage()
    {
        syncing = true; // controls raise change events while the XAML loads
        InitializeComponent();

        SensBox.Children.Add(sens);
        sens.ValueChanged += v =>
        {
            Prefs.Sensitivity = (int)v;
            sens.Text = Prefs.Sensitivity.ToString();
            SavePrefs();
        };
        Stage.PlayButton.Click += Start_Click;

        foreach (int fps in MusicPrefs.Speeds) SpeedButtons.Children.Add(BuildSpeed(fps));
        foreach (var (hue, name, rgb) in PulseColourList)
        {
            var tile = new SwatchTile("Pulse in " + name.ToLowerInvariant(), Paint.Hex(rgb));
            tile.Click += (_, _) => PickPulse(hue);
            PulseColours.Children.Add(tile);
            pulseButtons.Add((tile, hue));
        }
        foreach (var s in Styles) StyleGrid.Children.Add(BuildStyleCard(s.Key, s.Name, s.Desc));
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

    /// <summary>Stage beside the options when there's room for both (440 + 340), else stacked.</summary>
    void Split_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool wide = e.NewSize.Width >= 796;
        Split.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Split.ColumnSpacing = wide ? 16 : 0;
        Grid.SetColumn(OptionsCard, wide ? 1 : 0);
        Grid.SetRow(OptionsCard, wide ? 0 : 1);
    }

    // ------------------------------------------------------------ building

    UIElement BuildSpeed(int fps)
    {
        var text = new TextBlock { Text = fps.ToString(), FontSize = 14, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var bar = new Border { Width = 16, Height = 3, CornerRadius = new CornerRadius(1.5), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2) };
        var face = new Border { Height = 30, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = new Grid { Children = { text, bar } } };
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["BareButton"], HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, Content = face,
        };
        AutomationProperties.SetName(button, $"{fps} updates a second");
        button.Click += (_, _) =>
        {
            Prefs.Fps = fps;
            SavePrefs();
            LoadPrefs();
            SyncState();
        };
        speedItems.Add(new SpeedItem { Fps = fps, Button = button, Face = face, Bar = bar, Text = text });
        return button;
    }

    static Color PulseColour(int hue)
    {
        foreach (var (h, _, rgb) in PulseColourList)
            if (h == hue) return Paint.Hex(rgb);
        return ToColor(ColorMath.HsvToRgb(hue, 1, 1));
    }

    /// <summary>The style's main colour: the stage's orb, the options' wash.</summary>
    static Color MainColour(string style) =>
        Styles.FirstOrDefault(s => s.Key == style) is { Colour: > 0 } s ? Paint.Hex(s.Colour) : PulseColour(Prefs.Hue);

    UIElement BuildStyleCard(string key, string name, string desc)
    {
        var colour = key == "pulse" ? PulseColour(Prefs.Hue) : MainColour(key);
        var art = new StyleArt(key, PulseColour(Prefs.Hue)) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) };
        var radio = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), VerticalAlignment = VerticalAlignment.Center };
        var title = new Grid
        {
            Children =
            {
                new TextBlock { Text = name, FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = Paint.Brush(0xFFFFFF) },
                radio,
            },
        };
        radio.HorizontalAlignment = HorizontalAlignment.Right;
        var face = new Grid
        {
            Height = 156, CornerRadius = new CornerRadius(12),
            Children =
            {
                new Border { Background = Draw.Vertical((0, Paint.Hex(0x24212B)), (1, Paint.Hex(0x121114))) },
                new Border(), // the style's colour glowing from behind the art (set in LoadPrefs)
                art,
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(14, 20, 14, 12), Spacing = 2,
                    Background = Draw.Vertical((0, Paint.Hex(0, 0)), (1, Paint.Hex(0, 0xA6))),
                    Children =
                    {
                        title,
                        new TextBlock { Text = desc, FontSize = 12, LineHeight = 16, TextWrapping = TextWrapping.Wrap, Foreground = Paint.Brush(0xFFFFFF, 0xD9) },
                    },
                },
            },
        };
        var ring = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(2), Padding = new Thickness(2), Child = face };
        var shadow = Glow.Shadow(20);
        var button = new Button
        {
            Style = (Style)Application.Current.Resources["BareButton"], HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(14), Content = new Grid { Children = { shadow, ring } },
        };
        AutomationProperties.SetName(button, $"{name}: {desc}");
        button.Click += (_, _) => PickStyle(key);
        styleCards.Add(new StyleCard { Key = key, Button = button, Ring = ring, Shadow = shadow, Radio = radio, Face = face, Art = art });
        return button;
    }

    // ------------------------------------------------------------ settings

    void LoadPrefs()
    {
        syncing = true;
        var p = Palette.For(this);
        var m = Prefs;
        var pulse = PulseColour(m.Hue);
        foreach (var c in styleCards)
        {
            bool on = c.Key == m.Style;
            var colour = c.Key == "pulse" ? pulse : MainColour(c.Key);
            c.Art.SetPulse(pulse);
            ((Border)c.Face.Children[1]).Background = Draw.Radial(new(0.5, 0.3), 0.8, 0.7,
                (0, Paint.WithAlpha(colour, 0.33)), (0.5, Paint.WithAlpha(colour, 0.08)), (0.75, Paint.WithAlpha(colour, 0)));
            c.Ring.BorderBrush = Palette.B(on ? p.Accent : Paint.Hex(0, 0));
            c.Shadow.Background = Glow.Soft(on ? colour : Paint.Hex(0), on ? 0.33 : 0.12);
            c.Shadow.Opacity = 1;
            // The radio dot: a white ring filled with the style's colour when picked, an outline when not.
            c.Radio.BorderThickness = new Thickness(on ? 5 : 1.5);
            c.Radio.BorderBrush = Palette.B(on ? Paint.Hex(0xFFFFFF) : Paint.Hex(0xFFFFFF, 0xB3));
            c.Radio.Background = on ? Palette.B(colour) : null;
            AutomationProperties.SetItemStatus(c.Button, on ? "Selected" : "");
        }
        if (!sens.IsDragging) sens.Value = m.Sensitivity;
        sens.Text = m.Sensitivity.ToString();
        sens.SetColours(p, null, FillSlider.FillOf(Violet, 0x44 / 255.0, 0x99 / 255.0), p.Text);

        SpeedBox.Background = Palette.B(p.Dark ? Paint.Hex(0xFFFFFF, 0x0A) : Paint.Hex(0, 0x08));
        SpeedBox.BorderBrush = Palette.B(p.Dark ? Paint.Hex(0xFFFFFF, 0x14) : Paint.Hex(0, 0x0F));
        foreach (var s in speedItems)
        {
            bool on = s.Fps == m.Fps;
            // A WinUI segmented control: the picked item is a raised surface with an accent bar under its label.
            s.Face.Background = Palette.B(on ? p.Ctrl : Paint.Hex(0, 0));
            s.Face.BorderBrush = Palette.B(on ? p.CtrlStroke : Paint.Hex(0, 0));
            s.Bar.Background = Palette.B(on ? p.Accent : Paint.Hex(0, 0));
            s.Text.FontWeight = on ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
            s.Text.Foreground = Palette.B(p.Text);
            AutomationProperties.SetItemStatus(s.Button, on ? "Selected" : "");
        }
        foreach (var (tile, hue) in pulseButtons) tile.Show(p, hue == m.Hue);
        PulseDesc.Text = m.Style == "pulse" ? "The colour the Pulse style breathes in" : "For the Pulse style. Picking one switches to it";
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
        if (Prefs.Style == key) return;
        Prefs.Style = key;
        SavePrefs();
        LoadPrefs();
        SyncState();
    }

    void PickPulse(int hue)
    {
        Prefs.Hue = hue;
        Prefs.Style = "pulse"; // picking a pulse colour implies the Pulse style
        SavePrefs();
        LoadPrefs();
        SyncState();
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

    void SyncState()
    {
        bool live = Session.MusicRunning || Session.MusicKind == "busy" || previewRunning;
        string kind = Session.MusicKind;
        var p = Palette.For(this);
        var main = MainColour(Prefs.Style);

        // Start: a violet pill with a glow. Stop: a quiet outlined button.
        StartText.Text = live ? "Stop" : "Start";
        StartIcon.Data = Draw.Geo(live ? "M6 5h4v14H6zM14 5h4v14h-4z" : "M7 4.5v15l12.5-7.5z");
        var fg = Palette.B(live ? p.Text : Paint.Hex(0xFFFFFF));
        StartIcon.Fill = fg;
        StartText.Foreground = fg;
        StartFace.Background = Palette.B(live ? p.Ctrl : Violet);
        StartFace.BorderBrush = Palette.B(live ? p.CtrlStroke : Violet);
        StartGlow.Background = live ? null : Glow.Soft(Violet, 0.45);
        AutomationProperties.SetName(StartButton, live ? "Stop music sync" : "Start music sync");

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

        // Stage
        Stage.Show(live, main);
        StageBase.Background = live ? Draw.Vertical((0, Paint.Hex(0x221E2B)), (1, Paint.Hex(0x111014)))
                                    : Draw.Vertical((0, Paint.Hex(0x2E2F33)), (1, Paint.Hex(0x1A1B1E)));
        StageGlow.Background = live
            ? Draw.Radial(new(0.5, 0.34), 0.7, 0.6, (0, Paint.WithAlpha(main, 0.33)), (0.45, Paint.WithAlpha(main, 0.094)), (0.75, Paint.WithAlpha(main, 0)))
            : null;
        string styleName = Styles.FirstOrDefault(s => s.Key == Prefs.Style).Name ?? "Spectrum";
        var st = Session.Shown;
        if (live)
        {
            Headline.Text = previewRunning ? "Listening to Stereo Mix" : Session.MusicText;
            if (previewRunning) ShowLightNow(MainColour(Prefs.Style));
            StatusLine.Text = $"{styleName} · {Prefs.Fps} updates a second";
        }
        else
        {
            Headline.Text = kind == "error" ? "Couldn't start" : "Stopped";
            StatusLine.Text = st == null ? "Start it to make the light follow the music."
                : $"The light is {st.Describe().Replace(" · ", ", ").ToLowerInvariant()}.";
            ShowLightNow(st?.On == true ? ToColor(st.Rgb) : Paint.Hex(0x6B6B6B));
        }
        AutomationProperties.SetName(Stage, live ? $"Music sync running: {styleName}" : "Music sync stopped");

        // Options: a soft wash of the style's colour while it runs.
        Wash.Background = live
            ? Draw.Radial(new(1, 0), 1.2, 0.9, (0, Paint.WithAlpha(main, 0.2)), (0.45, Paint.WithAlpha(main, 0.063)), (0.75, Paint.WithAlpha(main, 0)))
            : null;
    }

    void ShowLightNow(Color c)
    {
        LightDot.Fill = new SolidColorBrush(c);
        LightGlow.Fill = Glow.Soft(c, 0.7);
    }

    void OnFrame(MusicFrame f)
    {
        if (!visible || !Session.MusicRunning) return;
        ShowLightNow(ToColor(ColorMath.HsvToRgb(f.H, f.S, Math.Max(0.35, f.V))));
    }
}
