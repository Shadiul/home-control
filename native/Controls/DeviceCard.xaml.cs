using HomeControl.Art;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace HomeControl.Controls;

public sealed partial class DeviceCard : UserControl
{
    bool settingPower;
    string[] chips = [];

    public DeviceCard()
    {
        InitializeComponent();
    }

    /// <summary>The user flipped the power switch (not raised when code sets <see cref="IsOn"/>).</summary>
    public event Action<bool>? PowerToggled;
    public event Action? OpenClicked;

    public string Title
    {
        get => TitleText.Text;
        set
        {
            TitleText.Text = value;
            AutomationProperties.SetName(Power, value + " power");
            AutomationProperties.SetName(OpenButton, "Open " + value);
        }
    }

    public string Status { get => StatusText.Text; set => StatusText.Text = value; }
    public object? Icon { get => IconHost.Content; set => IconHost.Content = value; }
    public object? Art { get => ArtHost.Content; set => ArtHost.Content = value; }
    public object? Body { get => BodyHost.Content; set => BodyHost.Content = value; }

    public bool HasPower { get => Power.Visibility == Visibility.Visible; set => Power.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
    public bool PowerEnabled { get => Power.IsEnabled; set => Power.IsEnabled = value; }

    public bool IsOn
    {
        get => Power.IsOn;
        set
        {
            if (Power.IsOn != value)
            {
                settingPower = true;
                Power.IsOn = value;
                settingPower = false;
            }
            VisualStateManager.GoToState(Power, value ? "On" : "Off", false); // after a theme change it can keep showing the old state
        }
    }

    public void SetChips(params string[] texts)
    {
        if (texts.SequenceEqual(chips)) return;
        chips = texts;
        Chips.Children.Clear();
        foreach (var t in texts)
            Chips.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["SceneChip"],
                Child = new TextBlock { Text = t, FontSize = 12, Foreground = Paint.Brush(0xFFFFFF, 0xE6), VerticalAlignment = VerticalAlignment.Center },
            });
    }

    /// <summary>The scene behind the card: a vertical gradient, and a glow near the top (null for none).</summary>
    public void SetScene(Color top, Color middle, Color bottom, Color? glow, double glowY = 0.2, double glowRx = 0.9, double glowRy = 0.45)
    {
        BaseLayer.Background = Draw.Vertical((0, top), (0.55, middle), (1, bottom));
        GlowLayer.Background = glow is { } g
            ? Draw.Radial(new(0.5, glowY), glowRx, glowRy, (0, g), (0.55, Paint.WithAlpha(g, g.A / 255.0 * 0.25)), (0.8, Paint.WithAlpha(g, 0)))
            : null;
    }

    void Power_Toggled(object sender, RoutedEventArgs e)
    {
        if (!settingPower) PowerToggled?.Invoke(Power.IsOn);
    }

    void Open_Click(object sender, RoutedEventArgs e) => OpenClicked?.Invoke();
}
