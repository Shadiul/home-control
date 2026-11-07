using CommunityToolkit.WinUI.Controls;
using LedControl.Art;
using LedControl.Controls;
using LedControl.Core;
using LedControl.Core.Rgb;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace LedControl.Pages;

/// <summary>
/// PC lighting: the case drawn and lit in the chosen colour beside a controls card (brightness, colour swatches,
/// and whether to follow music sync or the room light), then the controllers found (NZXT, MSI, Corsair) and the
/// switch that hands them back to their own lighting.
/// </summary>
public sealed partial class PcPage : Page
{
    // Whites and colours, as the shared swatch tiles.
    static readonly (string Name, uint Rgb)[] Colours =
    [
        ("Warm white", 0xFFC47E), ("Neutral white", 0xFFE6C8), ("Cool white", 0xEEF4FF),
        ("Red", 0xE8453C), ("Orange", 0xF2802E), ("Amber", 0xF5B12E), ("Yellow", 0xF2D33C),
        ("Green", 0x3DAA67), ("Teal", 0x2BB5A8), ("Sky", 0x3A9EE8), ("Blue", 0x3E6FE0),
        ("Violet", 0x7A5BE0), ("Purple", 0xA85BD8), ("Pink", 0xE05B9E),
    ];

    const string SunIcon = "M12 8a4 4 0 1 0 0 8 4 4 0 0 0 0-8zM12 2.5v2M12 19.5v2M4.6 4.6 6 6M18 18l1.4 1.4M2.5 12h2M19.5 12h2M4.6 19.4 6 18M18 6l1.4-1.4";
    const string DimSunIcon = "M12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6zM12 5v1M12 18v1M5 12h1M18 12h1";
    const string MusicIcon = "M9 18V5.5l11-2V16M6.5 20.5a2.5 2.5 0 1 1 0-5 2.5 2.5 0 0 1 0 5zM17.5 18.5a2.5 2.5 0 1 1 0-5 2.5 2.5 0 0 1 0 5z";
    const string BulbIcon = "M9 18h6M10 21h4M12 3a6 6 0 0 0-3.6 10.8c.7.6 1.1 1.3 1.1 2.2v.5h5V16c0-.9.4-1.6 1.1-2.2A6 6 0 0 0 12 3z";

    readonly PcCaseArt art = new();
    readonly ThickSlider brightness = new("Brightness") { Minimum = 1, Maximum = 100, LargeChange = 10 };
    readonly Path[] dimSun, brightSun;
    readonly List<(SwatchTile Tile, string Name, int Rgb)> swatches = [];
    readonly ChipTile followMusic, followRoom;
    readonly ColorPicker picker = new()
    {
        ColorSpectrumShape = ColorSpectrumShape.Box, IsAlphaEnabled = false, IsColorSliderVisible = false,
        IsColorChannelTextInputVisible = false, IsHexInputVisible = true,
    };
    SwatchTile? customTile;
    bool syncing;

    static App A => App.Instance!;
    static PcLightingPrefs Prefs => A.Config.Pc;

    public PcPage()
    {
        syncing = true;
        InitializeComponent();
        CaseBox.Child = art;

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
        brightness.ValueChanged += v =>
        {
            if (syncing) return;
            Prefs.Brightness = (int)v;
            Prefs.On = true;
            A.ApplyPcLighting();
        };

        foreach (var (name, rgb) in Colours)
        {
            var tile = new SwatchTile(name, Paint.Hex(rgb));
            tile.Click += (_, _) => PickColour((int)rgb);
            swatches.Add((tile, name, (int)rgb));
            SwatchGrid.Children.Add(tile);
        }
        customTile = new SwatchTile("Custom colour", Paint.Hex(0x808080), SwatchTile.RainbowFace());
        var flyout = new Flyout { Content = picker };
        flyout.Opening += (_, _) =>
        {
            syncing = true;
            picker.Color = Paint.Hex((uint)Prefs.Rgb);
            syncing = false;
        };
        customTile.Flyout = flyout;
        picker.ColorChanged += (_, e) =>
        {
            if (!syncing) PickColour(e.NewColor.R << 16 | e.NewColor.G << 8 | e.NewColor.B);
        };
        SwatchGrid.Children.Add(customTile);

        followMusic = new ChipTile("Music sync", MusicIcon, Paint.Hex(0x8A5BE0), row: true, height: 44, labelSize: 13);
        followMusic.Click += (_, _) => { Prefs.FollowMusic = !Prefs.FollowMusic; A.ApplyPcLighting(); };
        ToolTipService.SetToolTip(followMusic, "Show music sync's colours while it runs");
        followRoom = new ChipTile("Room light", BulbIcon, Paint.Hex(0xE8A33C), row: true, height: 44, labelSize: 13);
        followRoom.Click += (_, _) => { Prefs.FollowRoom = !Prefs.FollowRoom; A.ApplyPcLighting(); };
        ToolTipService.SetToolTip(followRoom, "Match the room light's colour");
        FollowGrid.Children.Add(followMusic);
        FollowGrid.Children.Add(followRoom);
        syncing = false;

        A.PcChanged += Sync;
        A.Session.Changed += Sync;
        ActualThemeChanged += (_, _) => Sync();
        Loaded += (_, _) => Sync();
    }

    void PickColour(int rgb)
    {
        Prefs.Rgb = rgb & 0xFFFFFF;
        Prefs.On = true;
        Prefs.FollowRoom = false; // picking a colour stops following the room light
        A.ApplyPcLighting();
    }

    /// <summary>Preview beside the controls when there's room for both (340 + 420), else stacked.</summary>
    void Split_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        bool wide = e.NewSize.Width >= 776;
        Split.ColumnDefinitions[1].Width = wide ? new GridLength(1.25, GridUnitType.Star) : new GridLength(0);
        Split.ColumnSpacing = wide ? 16 : 0;
        Grid.SetColumn(ControlsCard, wide ? 1 : 0);
        Grid.SetRow(ControlsCard, wide ? 0 : 1);
    }

    string ColourName(int rgb) =>
        swatches.FirstOrDefault(s => s.Rgb == rgb).Name ?? ColorMath.HueName((int)Math.Round(ColorMath.RgbToHsv((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb).H), 100);

    void Sync()
    {
        syncing = true;
        try
        {
            var p = Palette.For(this);
            var pc = A.Pc;
            var state = A.PcState();
            bool enabled = Prefs.Enabled, on = enabled && state.On;
            var colour = Color.FromArgb(255, state.R, state.G, state.B);
            int rgb = state.R << 16 | state.G << 8 | state.B;

            // Status line
            string kind = enabled ? pc.StatusKind : "off";
            StatusDot.Fill = Palette.B(kind switch
            {
                "ok" => p.Dark ? Paint.Hex(0x6CCB5F) : Paint.Hex(0x0F7B0F),
                "error" => p.Dark ? Paint.Hex(0xFF99A4) : Paint.Hex(0xC42B1C),
                "busy" or "conflict" => p.Dark ? Paint.Hex(0xFCE100) : Paint.Hex(0x9D5D00),
                _ => p.Text3,
            });
            StatusText.Text = !enabled ? "Handed back to the devices' own lighting"
                : kind == "ok" ? string.Join(" · ", pc.Devices.Where(d => d.Ok).Select(d => ShortName(d.Kind)))
                : pc.StatusText;
            ConflictBar.IsOpen = enabled && kind == "conflict";
            ConflictBar.Message = pc.StatusText;
            ConflictBar.Visibility = ConflictBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            HandedBackBar.IsOpen = !enabled;
            HandedBackBar.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;

            PowerSwitch.IsEnabled = enabled;
            PowerSwitch.IsOn = on;
            VisualStateManager.GoToState(PowerSwitch, on ? "On" : "Off", false);
            PowerText.Text = enabled ? (on ? "On" : "Off") : "";
            EnabledSwitch.IsOn = enabled;
            VisualStateManager.GoToState(EnabledSwitch, enabled ? "On" : "Off", false);
            foreach (var c in new Control[] { brightness, PreviewButton, followMusic, followRoom }) c.IsEnabled = enabled;
            foreach (var s in swatches) s.Tile.IsEnabled = enabled;
            if (customTile != null) customTile.IsEnabled = enabled;

            // Preview
            bool musicShowing = on && Prefs.FollowMusic && A.Session.MusicRunning;
            art.Show(on, colour, state.Brightness / 100.0);
            string name = Prefs.FollowRoom ? "Room light colour" : ColourName(rgb);
            Headline.Text = !enabled ? "Not controlled" : musicShowing ? "Following music sync" : on ? $"{name} · {state.Brightness}%" : "Off";
            SubLine.Text = !enabled ? "Turn on below to set the PC's lights here."
                : pc.CoolingText.Length > 0 ? $"Corsair cooler: {pc.CoolingText}"
                : "Click the preview to turn the PC lights on or off";
            AutomationProperties.SetName(art, on ? $"PC lit {name.ToLowerInvariant()} at {state.Brightness}%" : "PC lights off");
            Stage.Background = Palette.B(on ? (p.Dark ? Paint.Hex(0x1A1A1C) : Paint.Hex(0x2A2A2D)) : (p.Dark ? Paint.Hex(0x323232) : Paint.Hex(0xE9E9E9)));
            StageGlow.Background = on
                ? Draw.Radial(new(0.5, 0.4), 0.7, 0.7, (0, Paint.WithAlpha(colour, 0.3)), (0.4, Paint.WithAlpha(colour, 0.08)), (0.7, Paint.WithAlpha(colour, 0)))
                : null;
            Wash.Background = on
                ? Draw.Radial(new(1, 0), 1.2, 0.9, (0, Paint.WithAlpha(colour, 0.25)), (0.4, Paint.WithAlpha(colour, 0.078)), (0.7, Paint.WithAlpha(colour, 0)))
                : null;

            // Brightness
            if (!brightness.IsDragging) brightness.Value = state.Brightness;
            var track = p.Dark ? Paint.Hex(0x454545) : Paint.Hex(0xDCDCDC);
            brightness.SetColours(Palette.B(track), Draw.Gradient(new(0, 0), new(1, 0), (0, Paint.Mix(colour, track, 0.55)), (1, colour)),
                                  on ? colour : null, Paint.Mix(colour, Paint.Hex(0), 0.25));
            BrightnessText.Text = $"{state.Brightness}%";
            brightness.ValueText = BrightnessText.Text;
            Draw.Tint(dimSun, p.Text3);
            Draw.Tint(brightSun, p.Text2);
            var well = Palette.B(p.Dark ? Paint.Hex(0x383838) : Paint.Hex(0xEFEFEF));
            BrightnessPill.Background = ColourPill.Background = well;

            // Colour
            foreach (var (tile, _, c) in swatches) tile.Show(p, enabled && !Prefs.FollowRoom && c == Prefs.Rgb);
            customTile?.Show(p, false);
            ColourDot.Fill = Palette.B(colour);
            ColourNameText.Text = name;

            followMusic.Show(p, Prefs.FollowMusic, enabled);
            followRoom.Show(p, Prefs.FollowRoom, enabled);

            ShowDevices(p, pc, enabled);
        }
        finally
        {
            syncing = false;
        }
    }

    static string ShortName(string kind) => kind switch { "nzxt" => "NZXT", "msi" => "Motherboard", "corsair" => "Corsair cooler", _ => kind };

    /// <summary>One Settings-style card per controller, with a status dot.</summary>
    void ShowDevices(Palette p, PcLighting pc, bool enabled)
    {
        DeviceList.Children.Clear();
        var list = pc.Devices;
        if (list.Count == 0)
        {
            DeviceList.Children.Add(new SettingsCard
            {
                Header = enabled ? (pc.StatusKind == "busy" ? "Looking for lighting controllers…" : "No lighting controllers found") : "Not looking for controllers",
                Description = "Home Control works with NZXT Smart Device V2 and RGB & Fan Controllers, MSI Mystic Light motherboards and the Corsair iCUE Commander CORE.",
                HeaderIcon = new FontIcon { Glyph = "" },
            });
            return;
        }
        foreach (var d in list)
        {
            var dot = new Ellipse
            {
                Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center,
                Fill = Palette.B(!enabled ? p.Text3 : d.Ok ? (p.Dark ? Paint.Hex(0x6CCB5F) : Paint.Hex(0x0F7B0F)) : (p.Dark ? Paint.Hex(0xFF99A4) : Paint.Hex(0xC42B1C))),
            };
            var status = new TextBlock
            {
                FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Foreground = Palette.B(p.Text2),
                Text = !enabled ? "Own lighting" : d.Ok ? "Connected" : "Not working",
            };
            string detail = d.Kind switch
            {
                "nzxt" => d.Detail,
                "msi" => "RGB and ARGB headers on the motherboard. Nothing lights up unless something is plugged into them.",
                _ => pc.CoolingText.Length > 0 ? $"Cooler pump and fans · {pc.CoolingText}" : "Cooler pump and fans",
            };
            if (!d.Ok && d.Error.Length > 0) detail = d.Error;
            DeviceList.Children.Add(new SettingsCard
            {
                Header = d.Name,
                Description = detail,
                HeaderIcon = new FontIcon { Glyph = d.Kind switch { "nzxt" => "", "msi" => "", _ => "" } },
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { dot, status } },
            });
        }
    }

    void Preview_Click(object sender, RoutedEventArgs e)
    {
        Prefs.On = !Prefs.On;
        A.ApplyPcLighting();
    }

    void Power_Toggled(object sender, RoutedEventArgs e)
    {
        if (syncing || PowerSwitch.IsOn == Prefs.On) return;
        Prefs.On = PowerSwitch.IsOn;
        A.ApplyPcLighting();
    }

    void Enabled_Toggled(object sender, RoutedEventArgs e)
    {
        if (syncing || EnabledSwitch.IsOn == Prefs.Enabled) return;
        A.SetPcLightingEnabled(EnabledSwitch.IsOn);
    }

    void Enable_Click(object sender, RoutedEventArgs e) => A.SetPcLightingEnabled(true);
}
