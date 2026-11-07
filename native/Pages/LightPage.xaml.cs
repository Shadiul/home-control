using LedControl.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LedControl.Pages;

public sealed class Swatch(string name, int h, int s)
{
    public string Name { get; } = name;
    public int H { get; } = h;
    public int S { get; } = s;
    public SolidColorBrush Brush { get; } = new(LightPage.ToColor(ColorMath.HsvToRgb(h, s / 100.0, 1)));
}

public sealed partial class LightPage : Page
{
    static readonly (int H, int S)[] SwatchValues =
        [(0, 100), (16, 100), (32, 100), (50, 100), (90, 90), (130, 100),
         (175, 90), (200, 100), (225, 100), (265, 85), (290, 80), (325, 80)];

    readonly List<Swatch> swatches;
    bool syncing;
    int lastH = 30, lastS = 100;

    static LightSession Session => App.Instance!.Session;

    public LightPage()
    {
        syncing = true; // controls raise change events while the XAML loads
        InitializeComponent();
        syncing = false;
        swatches = SwatchValues.Select(v => new Swatch(ColorMath.HueName(v.H, v.S), v.H, v.S)).ToList();
        Swatches.ItemsSource = swatches;
        SceneGrid.ItemsSource = LightSession.Scenes;
        Session.Changed += Sync;
        Loaded += (_, _) => Sync();
    }

    public static Color ToColor((byte R, byte G, byte B) c) => Color.FromArgb(255, c.R, c.G, c.B);

    void Sync()
    {
        syncing = true;
        try
        {
            var cfg = App.Instance!.Config;
            DeviceName.Text = string.IsNullOrWhiteSpace(cfg.Device.Name) ? "Light" : cfg.Device.Name;
            string kind = Session.StatusKind;
            VisualStateManager.GoToState(this, kind switch
            {
                "ok" => "StatusOk", "error" => "StatusError", "setup" => "StatusSetup", _ => "StatusBusy",
            }, true);
            StatusText.Text = kind == "error" ? "Not connected" : Session.StatusText;
            ErrorBar.IsOpen = kind == "error";
            ErrorBar.Message = Session.StatusText;
            SetupBar.IsOpen = kind == "setup";

            var st = Session.Shown;
            PowerSwitch.IsEnabled = st != null;
            if (st == null)
            {
                StateText.Text = kind == "busy" ? "Connecting…" : "Unknown";
                UpdatePreview(null);
                return;
            }
            PowerSwitch.IsOn = st.On;
            StateText.Text = Session.MusicRunning ? "Music sync is running" : st.Describe();

            bool colour = st.IsColour;
            var mode = colour ? ColourItem : WhiteItem;
            if (ModeBar.SelectedItem != mode) ModeBar.SelectedItem = mode;
            WhitePanel.Visibility = colour ? Visibility.Collapsed : Visibility.Visible;
            ColourPanel.Visibility = colour ? Visibility.Visible : Visibility.Collapsed;
            ColourItem.Visibility = st.HasColour ? Visibility.Visible : Visibility.Collapsed;
            TempCard.Visibility = st.HasTemp ? Visibility.Visible : Visibility.Collapsed;

            BrightnessSlider.Value = st.Brightness;
            TempSlider.Value = st.Temp;
            ColourBrightness.Value = st.V;
            if (colour)
            {
                lastH = st.H;
                lastS = st.S;
            }
            Swatches.SelectedItem = colour
                ? swatches.FirstOrDefault(w => Math.Abs(w.H - st.H) <= 2 && Math.Abs(w.S - st.S) <= 3)
                : null;
            UpdatePreview(st);
        }
        finally
        {
            syncing = false;
        }
    }

    void UpdatePreview(LightState? st)
    {
        bool on = st?.On == true;
        var (r, g, b) = st?.Rgb ?? ((byte)255, (byte)255, (byte)255);
        BulbBrush.Color = Color.FromArgb(255, r, g, b);
        Bulb.Opacity = on ? 1 : 0;
        double glow = on ? 0.3 + 0.6 * Math.Sqrt(st!.Level) : 0;
        GlowInner.Color = Color.FromArgb((byte)(glow * 255), r, g, b);
        GlowOuter.Color = Color.FromArgb(0, r, g, b);
        if (on)
        {
            double lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255;
            BulbIcon.Foreground = new SolidColorBrush(lum > 0.6 ? Color.FromArgb(0xE4, 0, 0, 0) : Colors.White);
        }
        else
        {
            BulbIcon.ClearValue(IconElement.ForegroundProperty);
        }
    }

    void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (Session.Shown != null) Session.TogglePower();
    }

    void Power_Toggled(object sender, RoutedEventArgs e)
    {
        if (!syncing) Session.Submit(LightCommand.Power(PowerSwitch.IsOn));
    }

    void Mode_Changed(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (syncing) return;
        if (sender.SelectedItem == ColourItem)
            Session.Submit(LightCommand.Colour(lastH, lastS, (int)ColourBrightness.Value));
        else
            Session.Submit(LightCommand.White((int)BrightnessSlider.Value, (int)TempSlider.Value));
    }

    void White_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!syncing) Session.Submit(LightCommand.White((int)BrightnessSlider.Value, (int)TempSlider.Value));
    }

    void ColourBrightness_Changed(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!syncing) Session.Submit(LightCommand.Colour(lastH, lastS, (int)ColourBrightness.Value));
    }

    void Swatch_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not Swatch w) return;
        lastH = w.H;
        lastS = w.S;
        Session.Submit(LightCommand.Colour(w.H, w.S, (int)ColourBrightness.Value));
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
        Session.Submit(LightCommand.Colour(lastH, lastS, (int)ColourBrightness.Value));
    }

    void Scene_Click(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is Scene s) Session.Submit(s.Command);
    }

    void Retry_Click(object sender, RoutedEventArgs e)
    {
        App.Instance!.Controller.Reset();
        _ = Session.RefreshAsync();
    }

    void OpenSettings_Click(object sender, RoutedEventArgs e) => App.Instance!.Window.ShowPage("settings");
}
