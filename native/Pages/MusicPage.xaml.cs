using LedControl.Core;
using LedControl.Music;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace LedControl.Pages;

public sealed partial class MusicPage : Page
{
    static readonly Dictionary<string, string> StyleDescriptions = new()
    {
        ["pulse"] = "One colour that breathes with the volume",
        ["spectrum"] = "Bass shows red, mids green, treble blue",
        ["beat"] = "A new colour and a flash on every beat",
        ["party"] = "Colours spin with the energy and flash on beats",
    };
    static readonly int[] PulseHues = [0, 25, 50, 130, 190, 230, 280, 320];

    readonly Rectangle[] bars = new Rectangle[Analyzer.BarCount];
    readonly SolidColorBrush barBrush = new(Colors.Gray);
    readonly List<Button> pulseButtons = [];
    List<AudioCapture.Source> sources = [];
    bool syncing, visible;

    static LightSession Session => App.Instance!.Session;
    static MusicPrefs Prefs => App.Instance!.Config.Music;

    public MusicPage()
    {
        syncing = true; // controls raise change events while the XAML loads

        InitializeComponent();

        syncing = false;
        for (int i = 0; i < bars.Length; i++)
        {
            BarsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var r = new Rectangle { RadiusX = 3, RadiusY = 3, VerticalAlignment = VerticalAlignment.Bottom, Height = 4, Fill = barBrush };
            Grid.SetColumn(r, i);
            BarsGrid.Children.Add(r);
            bars[i] = r;
        }
        foreach (int fps in MusicPrefs.Speeds)
            SpeedBox.Items.Add(new ComboBoxItem { Content = $"{fps} per second", Tag = fps });
        foreach (int hue in PulseHues)
        {
            var b = new Button
            {
                Style = (Style)Application.Current.Resources["BareButton"],
                Tag = hue,
                CornerRadius = new CornerRadius(15),
                Content = new Border
                {
                    Width = 30, Height = 30, CornerRadius = new CornerRadius(15), BorderThickness = new Thickness(2),
                    Child = new Ellipse
                    {
                        Width = 20, Height = 20,
                        Fill = new SolidColorBrush(LightPage.ToColor(ColorMath.HsvToRgb(hue, 1, 1))),
                    },
                },
            };
            AutomationPropertiesHelper.SetName(b, ColorMath.HueName(hue, 100));
            ToolTipService.SetToolTip(b, ColorMath.HueName(hue, 100));
            b.Click += Pulse_Click;
            PulseColours.Children.Add(b);
            pulseButtons.Add(b);
        }
        LoadPrefs();
        Session.MusicChanged += SyncState;
        Session.MusicFrame += OnFrame;
        Loaded += (_, _) =>
        {
            visible = true;
            LoadSources();
            SyncState();
        };
        Unloaded += (_, _) => visible = false;
        ActualThemeChanged += (_, _) => MarkPulse();
    }

    void LoadPrefs()
    {
        syncing = true;
        var m = Prefs;
        StyleBox.SelectedItem = StyleBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == m.Style);
        StyleCard.Description = StyleDescriptions.GetValueOrDefault(m.Style, "");
        SensSlider.Value = m.Sensitivity;
        SpeedBox.SelectedItem = SpeedBox.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == m.Fps);
        MarkPulse();
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

    void MarkPulse()
    {
        foreach (var b in pulseButtons)
        {
            var border = (Border)b.Content;
            border.BorderBrush = (int)b.Tag == Prefs.Hue
                ? new SolidColorBrush(ActualTheme == ElementTheme.Dark ? Colors.White : Color.FromArgb(0xE4, 0, 0, 0))
                : new SolidColorBrush(Colors.Transparent);
        }
        PulseCard.Opacity = Prefs.Style == "pulse" ? 1 : 0.6;
    }

    void SavePrefs()
    {
        App.Instance!.ApplyMusicPrefs();
        try { App.Instance.Config.Save(); } catch (Exception e) { App.Log("Saving settings failed: " + e.Message); }
    }

    void SyncState()
    {
        bool running = Session.MusicRunning;
        string kind = Session.MusicKind;
        StartText.Text = running ? "Stop" : "Start";
        StartIcon.Glyph = running ? "" : "";
        AutomationPropertiesHelper.SetName(StartButton, running ? "Stop music sync" : "Start music sync");
        MusicStatus.Text = Session.MusicText;
        MusicBusy.IsActive = kind == "busy";
        IdleHint.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
        if (kind == "error")
        {
            MusicError.Message = Session.MusicText;
            MusicError.IsOpen = true;
        }
        else if (kind == "ok")
        {
            MusicError.IsOpen = false;
        }
        if (!running)
        {
            foreach (var r in bars) r.Height = 4;
            barBrush.Color = Color.FromArgb(0x50, 0x80, 0x80, 0x80);
            VisGlowInner.Color = Color.FromArgb(0, 0, 0, 0);
            ColourChip.ClearValue(Border.BackgroundProperty);
        }
    }

    void OnFrame(MusicFrame f)
    {
        if (!visible || !Session.MusicRunning) return;
        double max = Math.Max(10, BarsGrid.ActualHeight - 6);
        for (int i = 0; i < bars.Length && i < f.Bars.Length; i++) bars[i].Height = 4 + f.Bars[i] * max;
        var (r, g, b) = ColorMath.HsvToRgb(f.H, Math.Max(0.15, f.S), 1);
        barBrush.Color = Color.FromArgb((byte)(150 + 105 * Math.Min(1, f.V * 1.5)), r, g, b);
        VisGlowInner.Color = Color.FromArgb((byte)(160 * f.V), r, g, b);
        VisGlowOuter.Color = Color.FromArgb(0, r, g, b);
        var (cr, cg, cb) = ColorMath.HsvToRgb(f.H, f.S, Math.Max(0.08, f.V));
        ColourChip.Background = new SolidColorBrush(Color.FromArgb(255, cr, cg, cb));
    }

    void Start_Click(object sender, RoutedEventArgs e)
    {
        MusicError.IsOpen = false;
        Session.ToggleMusic();
        SyncState();
    }

    void Style_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (syncing || StyleBox.SelectedItem is not ComboBoxItem item) return;
        Prefs.Style = (string)item.Tag;
        StyleCard.Description = StyleDescriptions.GetValueOrDefault(Prefs.Style, "");
        MarkPulse();
        SavePrefs();
    }

    void Sens_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (syncing) return;
        Prefs.Sensitivity = (int)SensSlider.Value;
        SavePrefs();
    }

    void Pulse_Click(object sender, RoutedEventArgs e)
    {
        Prefs.Hue = (int)((Button)sender).Tag;
        if (Prefs.Style != "pulse")
        {
            Prefs.Style = "pulse"; // picking a pulse colour implies the Pulse style
            LoadPrefs();
        }
        MarkPulse();
        SavePrefs();
    }

    void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (syncing || SourceBox.SelectedItem is not ComboBoxItem item) return;
        Prefs.Source = (string)item.Tag;
        SavePrefs();
    }

    void Speed_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (syncing || SpeedBox.SelectedItem is not ComboBoxItem item) return;
        Prefs.Fps = (int)item.Tag;
        SavePrefs();
    }
}

static class AutomationPropertiesHelper
{
    public static void SetName(DependencyObject o, string name) =>
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(o, name);
}
