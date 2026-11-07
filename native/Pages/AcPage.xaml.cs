using LedControl.Art;
using LedControl.Controls;
using CommunityToolkit.WinUI.Controls;
using LedControl.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace LedControl.Pages;

/// <summary>
/// Climate: one tab per air conditioner. A big temperature dial with the room temperature and power,
/// mode cards beside it, three fan and airflow panels (each a picture with its own arc slider),
/// and the other options as Windows Settings cards with tooltips.
/// </summary>
public sealed partial class AcPage : Page
{
    // ------------------------------------------------------------ fixed content

    // Mode: name, icon (24 × 24, stroked), description, deep and light colour.
    static readonly (string Icon, string Desc, uint Deep, uint Light)[] Modes =
    [
        ("M20 12a8 8 0 0 1-14.3 4.9M4 12a8 8 0 0 1 14.3-4.9M18.5 3.5v3.6h-3.6M5.5 20.5v-3.6h3.6", "Picks cool or heat for you", 0x6A4FE0, 0xA992FF),
        ("M12 2.5v19M3.8 7.3l16.4 9.4M20.2 7.3 3.8 16.7M9.5 4.5 12 6l2.5-1.5M9.5 19.5 12 18l2.5 1.5", "Cools to the target", 0x1569C7, 0x5CB8FF),
        ("M12 3.5s6 6.4 6 10.9a6 6 0 0 1-12 0c0-4.5 6-10.9 6-10.9z", "Removes humidity", 0x0B8578, 0x4FD1BF),
        ("M12 12c-1-4 0-8 3-8.5s3 4.5-3 8.5zM12 12c4-1 8 0 8.5 3s-4.5 3-8.5-3zM12 12c1 4 0 8-3 8.5s-3-4.5 3-8.5zM12 12c-4 1-8 0-8.5-3s4.5-3 8.5 3z", "Moves air, no cooling", 0x2F8A45, 0x8CD27A),
        ("M12 3c.8 3.6 5 5.2 5 10a5 5 0 0 1-10 0c0-2.4 1.3-3.9 2.4-4.9.4 1.9 1.4 2.9 2.4 2.9-.2-3 .2-5.4.2-8z", "Warms to the target", 0xC94A0C, 0xFFA35C),
    ];

    // Options: Gree key, name, description, Fluent icon glyph, tooltip.
    static readonly (string Key, string Name, string Desc, string Glyph, string Info)[] Extras =
    [
        ("Tur", "Turbo", "Full power for a while. Cool and Heat only", "\uE945",
         "Runs the fan and compressor at full power for a while, so the room reaches your temperature faster. Works in Cool and Heat only. Turning it on switches Quiet off."),
        ("Quiet", "Quiet", "Lower fan noise", "\uE74F",
         "Slows the fan to make the unit quieter, for bedrooms or calls. The room cools or heats more slowly. Turning it on switches Turbo off."),
        ("SwhSlp", "Sleep", "Eases the temperature overnight", "\uE708",
         "For the night. In Cool it lets the temperature rise a little over the first hours, and in Heat it lets it drop a little, so you stay comfortable and use less power."),
        ("Lig", "Display light", "The numbers on the indoor unit", "\uE7F4",
         "Turns the temperature display on the indoor unit on or off, for example in a dark bedroom. The air conditioner keeps working either way."),
        ("Health", "Health", "Ionizer, on units that have one", "\uE8BE",
         "Turns on the built-in ionizer to help freshen the air. Only some units have one; on others this does nothing."),
        ("Blo", "X-Fan", "Dries the coil after turning off", "\uEDA8",
         "After you turn the unit off in Cool or Dry, the fan keeps running briefly to dry the inside, which helps prevent mould and musty smells."),
    ];

    const string AutoIcon = "M20 12a8 8 0 0 1-14.3 4.9M4 12a8 8 0 0 1 14.3-4.9M18.5 3.5v3.6h-3.6M5.5 20.5v-3.6h3.6";
    const string FanIcon = "M12 12c-1-4 0-8 3-8.5s3 4.5-3 8.5zM12 12c4-1 8 0 8.5 3s-4.5 3-8.5-3zM12 12c1 4 0 8-3 8.5s-3-4.5 3-8.5zM12 12c-4 1-8 0-8.5-3s4.5-3 8.5 3z";
    const string UpDownIcon = "M8 4v16M4.5 7.5 8 4l3.5 3.5M4.5 16.5 8 20l3.5-3.5M14 8h6M14 12h6M14 16h6";
    const string LeftRightIcon = "M4 8h16M7.5 4.5 4 8l3.5 3.5M16.5 4.5 20 8l-3.5 3.5M8 14v6M12 14v6M16 14v6";

    // Louvres: Gree values 2-6 are fixed positions, 1 is a full swing, 0 lets the unit decide.
    // Up and down also has 7-11, a swing over part of the range (set from the remote).
    static readonly string[] VerticalNames = ["Straight ahead", "Slightly down", "Halfway down", "Mostly down", "Straight down"];
    static readonly string[] HorizontalNames = ["Far left", "Left", "Centre", "Right", "Far right"];
    static readonly string[] PartSwingNames = ["lower part", "lower middle", "middle", "upper middle", "upper part"];
    static readonly string[] FanLevelNames = ["Low", "Medium-low", "Medium", "Medium-high", "High"];

    static AcHub Hub => App.Instance!.Acs;

    // ------------------------------------------------------------ parts built in code

    sealed class IconParts
    {
        public required Viewbox Box;
        public required Path[] Strokes;
        public Path? Fill;
    }

    sealed class ModeCard
    {
        public required Button Button;
        public required Grid Face;
        public required Border Tint, Badge;
        public required IconParts Icon, Mark;
        public required TextBlock Name, Desc;
    }

    sealed class FlowPanel
    {
        public required ArcSelector Arc;
        public required TextBlock Status;
        public required Border Well;
        public required IconParts Icon;
        public required ToggleButton Auto;
        public required IconParts AutoIcon;
        public ToggleButton? Swing;
        public IconParts? SwingIcon;
    }

    sealed class OptionCard
    {
        public required int Index;
        public required SettingsCard Card;
        public required FontIcon Icon;
        public required TextBlock Name, Desc;
        public required ToggleSwitch Toggle;
        public required Button Info;
    }

    readonly RadialDial dial = new()
    {
        Diameter = 280, Thickness = 16, KnobSize = new Size(18, 40), Minimum = AcState.MinTemp, Maximum = AcState.MaxTemp,
        Step = 1, LargeStep = 3, TickCount = AcState.MaxTemp - AcState.MinTemp + 1, MinLabel = $"{AcState.MinTemp}°", MaxLabel = $"{AcState.MaxTemp}°",
    };
    readonly TextBlock dialCaption = new() { FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock dialValue = new()
    {
        FontFamily = new FontFamily("Segoe UI Variable Display"), FontSize = 56, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        HorizontalAlignment = HorizontalAlignment.Center, LineHeight = 56,
    };
    readonly Button minus, plus;
    readonly List<ModeCard> modeCards = [];
    readonly FlowPanel fanPanel, vPanel, hPanel;
    readonly FanScene fanScene = new();
    readonly SideUnitScene sideScene = new();
    readonly FrontUnitScene frontScene = new();
    readonly List<OptionCard> tiles = [];

    readonly Dictionary<string, int> lastPosition = []; // per unit and louvre, for turning swing off
    readonly Dictionary<string, int> lastFan = [];       // per unit, the speed to go back to when Auto is turned off
    readonly HashSet<string> errorDismissed = []; // units whose error the user closed (until they connect again)
    AcUnit? unit;
    bool syncing;

    public AcPage()
    {
        syncing = true; // controls raise change events while the XAML loads
        InitializeComponent();

        minus = RoundButton("", "Lower temperature", () => ChangeTemp(-1));
        plus = RoundButton("", "Raise temperature", () => ChangeTemp(+1));
        dial.Center = new StackPanel
        {
            Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                dialCaption, dialValue,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(0, 4, 0, 0),
                                 HorizontalAlignment = HorizontalAlignment.Center, Children = { minus, plus } },
            },
        };
        AutomationProperties.SetName(dial, "Target temperature");
        dial.ValueChanged += v => unit?.Set(("SetTem", (int)v), ("TemUn", 0));
        DialHost.Content = dial;

        for (int m = 0; m < Modes.Length; m++) ModeGrid.Children.Add(BuildModeCard(m).Button);

        fanPanel = BuildFlowPanel("Fan speed", FanIcon, new ArcSelector(fanScene, 140, 104, 82, [135, 202.5, 270, 337.5, 405], FanLevelNames,
            ("Low", 78, 196), ("High", 202, 196), ArcSelector.Look.Amount, "Fan speed"), swing: null);
        vPanel = BuildFlowPanel("Up and down", UpDownIcon, new ArcSelector(sideScene, 103, 74, 100, [0, 22.5, 45, 67.5, 90], VerticalNames,
            ("Ahead", 236, 78), ("Down", 103, 196), ArcSelector.Look.Direction, "Up and down"),
            swing: ("M3 3.5h8.5A2.5 2.5 0 0 1 14 6v1.2L12.6 9H3z", "M19.6 10.6A10 10 0 0 1 11 20.4", "M17.2 9.9 19.6 10.6l.9-2.3M13.3 21.6 11 20.4l1.5-2.1"));
        hPanel = BuildFlowPanel("Left and right", LeftRightIcon, new ArcSelector(frontScene, 140, 72, 100, [160, 125, 90, 55, 20], HorizontalNames,
            ("Left", 30, 132), ("Right", 250, 132), ArcSelector.Look.Direction, "Left and right"),
            swing: ("M5 17.5h14a2 2 0 0 1 2 2V21H3v-1.5a2 2 0 0 1 2-2z", "M3.8 11.2A11 11 0 0 1 20.2 11.2", "M3.4 8.6 3.8 11.2l2.6-.5M20.6 8.6 20.2 11.2l-2.6-.5"));

        fanPanel.Arc.Picked += i => SetFan(i + 1);
        fanPanel.Auto.Click += (_, _) => ToggleFanAuto();
        vPanel.Arc.Picked += i => unit?.Set(("SwUpDn", i + 2));
        hPanel.Arc.Picked += i => unit?.Set(("SwingLfRig", i + 2));
        vPanel.Auto.Click += (_, _) => unit?.Set(("SwUpDn", 0));
        hPanel.Auto.Click += (_, _) => unit?.Set(("SwingLfRig", 0));
        vPanel.Swing!.Click += (_, _) => ToggleSwing("SwUpDn");
        hPanel.Swing!.Click += (_, _) => ToggleSwing("SwingLfRig");

        for (int i = 0; i < Extras.Length; i++) ExtrasGrid.Children.Add(BuildTile(i));
        syncing = false;

        ErrorBar.Closed += (_, _) =>
        {
            if (unit != null) errorDismissed.Add(unit.Config.Mac);
            ErrorBar.Visibility = Visibility.Collapsed;
        };
        Hub.ListChanged += BuildUnits;
        Hub.Changed += Sync;
        ActualThemeChanged += (_, _) => Sync();
        Loaded += (_, _) => Sync();
        BuildUnits();
    }

    // ------------------------------------------------------------ building

    static Geometry Geo(string data) => Draw.Geo(data);

    /// <summary>A 24 × 24 line icon at the given size. Optional filled shape underneath.</summary>
    static IconParts Icon(string strokes, double size, double width, string? fill = null)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        Path? f = null;
        if (fill != null)
        {
            f = new Path { Data = Geo(fill), Opacity = 0.35 };
            canvas.Children.Add(f);
        }
        var parts = strokes.Split('|').Select(s => new Path
        {
            Data = Geo(s), StrokeThickness = width, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
        }).ToArray();
        foreach (var p in parts) canvas.Children.Add(p);
        return new IconParts { Box = new Viewbox { Width = size, Height = size, Child = canvas }, Strokes = parts, Fill = f };
    }

    static void Colour(IconParts icon, Color c)
    {
        foreach (var p in icon.Strokes) p.Stroke = Palette.B(c);
        if (icon.Fill != null) icon.Fill.Fill = Palette.B(c);
    }

    static Button RoundButton(string glyph, string label, Action click)
    {
        var b = new Button
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(20), Padding = new Thickness(0),
            Content = new FontIcon { Glyph = glyph, FontSize = 14 },
        };
        AutomationProperties.SetName(b, label);
        ToolTipService.SetToolTip(b, label);
        b.Click += (_, _) => click();
        return b;
    }

    static Button Bare(UIElement content) => new()
    {
        Style = (Style)Application.Current.Resources["BareButton"], HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch, Content = content,
    };

    static void Hover(Button b, Border overlay)
    {
        b.PointerEntered += (_, _) => overlay.Opacity = 1;
        b.PointerExited += (_, _) => overlay.Opacity = 0;
    }

    ModeCard BuildModeCard(int m)
    {
        var (icon, desc, _, _) = Modes[m];
        var tint = new Border { CornerRadius = new CornerRadius(12) };
        var hover = new Border { CornerRadius = new CornerRadius(12), Opacity = 0 };
        var mark = Icon(icon, 44, 1.4);
        mark.Box.HorizontalAlignment = HorizontalAlignment.Right;
        mark.Box.VerticalAlignment = VerticalAlignment.Center;
        mark.Box.Margin = new Thickness(0, 0, 10, 0);
        var glyph = Icon(icon, 20, 1.8);
        var badge = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Child = glyph.Box };
        var name = new TextBlock { Text = AcState.ModeNames[m], FontSize = 15, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var sub = new TextBlock { Text = desc, FontSize = 12, TextWrapping = TextWrapping.Wrap };
        var face = new Grid
        {
            CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1),
            Children =
            {
                tint, hover, mark.Box,
                ModeContent(badge, name, sub),
            },
        };
        var button = Bare(face);
        button.CornerRadius = new CornerRadius(12);
        AutomationProperties.SetName(button, $"{AcState.ModeNames[m]}: {desc}");
        button.Click += (_, _) => unit?.Set(("Mod", m));
        Hover(button, hover);
        hover.Background = Palette.B(Paint.Hex(0x808080, 0x14));
        // Auto across the top; Cool and Dry, then Fan and Heat.
        int row = m == 0 ? 0 : 1 + (m - 1) / 2, col = m == 0 ? 0 : (m - 1) % 2;
        Grid.SetRow(button, row);
        Grid.SetColumn(button, col);
        if (m == 0) Grid.SetColumnSpan(button, 2);
        var card = new ModeCard { Button = button, Face = face, Tint = tint, Badge = badge, Icon = glyph, Mark = mark, Name = name, Desc = sub };
        modeCards.Add(card);
        return card;
    }

    /// <summary>Badge on the left, name and description beside it (the description wraps).</summary>
    static Grid ModeContent(Border badge, TextBlock name, TextBlock sub)
    {
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, Children = { name, sub } };
        Grid.SetColumn(texts, 1);
        badge.VerticalAlignment = VerticalAlignment.Center;
        return new Grid
        {
            ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 12, 56, 12),
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
            Children = { badge, texts },
        };
    }

    FlowPanel BuildFlowPanel(string title, string icon, ArcSelector arc, (string Unit, string Arc, string Heads)? swing)
    {
        var headIcon = Icon(icon, 18, 1.7);
        var well = new Border { Width = 32, Height = 32, CornerRadius = new CornerRadius(10), Child = headIcon.Box };
        var status = new TextBlock { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
        var header = new Grid
        {
            ColumnSpacing = 10,
            ColumnDefinitions = { new ColumnDefinition { Width = GridLength.Auto }, new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) } },
        };
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }, status } };
        Grid.SetColumn(titles, 1);
        header.Children.Add(well);
        header.Children.Add(titles);

        var autoIcon = Icon(AutoIcon, 15, 1.9);
        var auto = Toggle(autoIcon.Box, "Auto");
        AutomationProperties.SetName(auto, title + ": automatic");
        var buttons = new Grid { ColumnSpacing = 6 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.Children.Add(auto);
        var panel = new FlowPanel { Arc = arc, Status = status, Well = well, Icon = headIcon, Auto = auto, AutoIcon = autoIcon };
        if (swing is { } s)
        {
            var swingIcon = Icon(s.Arc + "|" + s.Heads, 20, 1.8, fill: s.Unit);
            var sw = Toggle(swingIcon.Box, "Swing");
            AutomationProperties.SetName(sw, title + ": swing");
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(sw, 1);
            buttons.Children.Add(sw);
            panel.Swing = sw;
            panel.SwingIcon = swingIcon;
        }
        arc.HorizontalAlignment = HorizontalAlignment.Center;
        var card = new Border
        {
            Style = (Style)Application.Current.Resources["Card"], CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 14, 14, 16),
            Child = new StackPanel { Spacing = 4, Children = { header, arc, buttons } },
        };
        FlowGrid.Children.Add(card);
        return panel;
    }

    static ToggleButton Toggle(UIElement icon, string text) => new()
    {
        Height = 40, CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Stretch,
        Content = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6,
            Children = { icon, new TextBlock { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center } },
        },
    };

    /// <summary>
    /// An option as a Windows Settings card: a monochrome Fluent icon, the name with a grey description,
    /// and an ⓘ, an On / Off label and a standard toggle switch on the right.
    /// </summary>
    UIElement BuildTile(int i)
    {
        var (key, name, desc, glyph, info) = Extras[i];
        var icon = new FontIcon { Glyph = glyph, FontSize = 20 };
        var title = new TextBlock { Text = name, FontSize = 14 };
        var sub = new TextBlock { Text = desc, FontSize = 12, LineHeight = 16, TextWrapping = TextWrapping.Wrap };
        var toggle = new ToggleSwitch { OnContent = "On", OffContent = "Off", MinWidth = 0 };
        AutomationProperties.SetName(toggle, name);
        AutomationProperties.SetHelpText(toggle, info);
        toggle.Toggled += (_, _) =>
        {
            // Only when it differs from the unit: Sync sets the switch too.
            if (!syncing && unit?.Shown is { } st && toggle.IsOn != st.Is(key)) ToggleExtra(i);
        };

        // ⓘ: the tooltip shows on hover or keyboard focus; a click or tap opens the same text.
        StackPanel Tip() => new()
        {
            MaxWidth = 300, Spacing = 4,
            Children =
            {
                new TextBlock { Text = name, FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold },
                new TextBlock { Text = info, FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 16 },
            },
        };
        var infoButton = new Button
        {
            Width = 32, Height = 32, CornerRadius = new CornerRadius(4), Padding = new Thickness(0), Background = Palette.B(Paint.Hex(0, 0)),
            BorderThickness = new Thickness(0), VerticalAlignment = VerticalAlignment.Center, Content = new FontIcon { Glyph = "\uE946", FontSize = 16 },
        };
        AutomationProperties.SetName(infoButton, "About " + name);
        AutomationProperties.SetHelpText(infoButton, info);
        ToolTipService.SetToolTip(infoButton, new ToolTip { Content = Tip() });
        infoButton.Flyout = new Flyout { Content = Tip() };

        var card = new SettingsCard
        {
            Header = title, Description = sub, HeaderIcon = icon, MinHeight = 68,
            Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { infoButton, toggle } },
        };
        tiles.Add(new OptionCard { Index = i, Card = card, Icon = icon, Name = title, Desc = sub, Toggle = toggle, Info = infoButton });
        return card;
    }

    // ------------------------------------------------------------ units

    /// <summary>Shows a specific unit (from Home or the tray menu, for example).</summary>
    public void Select(AcUnit target)
    {
        if (UnitBar.Items.FirstOrDefault(i => i.Tag == target) is { } item) UnitBar.SelectedItem = item;
    }

    void BuildUnits()
    {
        syncing = true;
        string? keep = unit?.Config.Mac;
        UnitBar.Items.Clear();
        foreach (var u in Hub.Units) UnitBar.Items.Add(new SelectorBarItem { Text = u.Config.DisplayName, Tag = u });
        unit = Hub.Units.FirstOrDefault(u => u.Config.Mac == keep) ?? Hub.Units.FirstOrDefault();
        UnitBar.SelectedItem = UnitBar.Items.FirstOrDefault(i => i.Tag == unit);
        RenameButton.Visibility = UnitBar.Visibility = Hub.Units.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SetupBar.IsOpen = Hub.Units.Count == 0;
        SetupBar.Visibility = SetupBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        Controls.Visibility = Hub.Units.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        syncing = false;
        Sync();
    }

    void Unit_Changed(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (syncing) return;
        unit = sender.SelectedItem?.Tag as AcUnit;
        Sync();
    }

    // ------------------------------------------------------------ showing the unit

    /// <summary>A louvre's setting: the fixed position (0-4, or -1), whether it swings, and a sentence.</summary>
    (int Pos, bool Swing, string Status) Louvre(AcState st, string key, string[] names)
    {
        int v = st.Get(key);
        if (v is >= 2 and <= 6)
        {
            lastPosition[unit!.Config.Mac + key] = v;
            return (v - 2, false, "Fixed: " + names[v - 2].ToLowerInvariant());
        }
        return v switch
        {
            0 => (-1, false, "Automatic: the unit chooses"),
            1 => (-1, true, "Swinging"),
            >= 7 and <= 11 when key == "SwUpDn" => (-1, true, "Swinging over the " + PartSwingNames[v - 7]),
            _ => (-1, false, $"Set from the remote (setting {v})"),
        };
    }

    void Sync()
    {
        if (unit == null) return;
        syncing = true;
        try
        {
            var p = Palette.For(this);
            var st = unit.Shown;
            ErrorBar.IsOpen = unit.StatusKind == "error" && !errorDismissed.Contains(unit.Config.Mac);
            ErrorBar.Visibility = ErrorBar.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            if (unit.StatusKind == "ok") errorDismissed.Remove(unit.Config.Mac);
            ErrorBar.Message = unit.StatusText;
            bool known = st != null, on = st?.On == true;
            foreach (var c in new Control[] { PowerSwitch, minus, plus, fanPanel.Auto, vPanel.Auto, hPanel.Auto, vPanel.Swing!, hPanel.Swing! })
                c.IsEnabled = known;
            fanPanel.Arc.IsEnabled = vPanel.Arc.IsEnabled = hPanel.Arc.IsEnabled = known;
            foreach (var m in modeCards) m.Button.IsEnabled = known;

            // Temperature and power
            bool usesTemp = st?.UsesSetTemp ?? true;
            PowerSwitch.IsOn = on;
            VisualStateManager.GoToState(PowerSwitch, on ? "On" : "Off", false); // after a theme change it can keep showing the old state
            StateText.Text = st == null ? (unit.StatusKind == "busy" ? "Connecting…" : "Unknown") : st.Describe();
            RoomText.Text = st?.RoomTemp is int room ? $"{room} °C" : "--";
            if (st != null && !dial.IsDragging) dial.Value = st.SetTemp;
            dial.IsEnabled = known && usesTemp;
            minus.IsEnabled = known && usesTemp && st!.SetTemp > AcState.MinTemp;
            plus.IsEnabled = known && usesTemp && st!.SetTemp < AcState.MaxTemp;
            dialValue.Text = st != null ? $"{st.SetTemp}°" : "--";
            dialValue.Foreground = Palette.B(on && usesTemp ? p.Accent : p.Text);
            dialCaption.Text = st == null ? "Connecting" : !usesTemp ? $"Not used in {AcState.ModeNames[st.Mode]}" : on ? "Target" : "Off · target";
            dialCaption.Foreground = Palette.B(p.Text2);
            dial.ArcBrush = Palette.B(on ? p.Accent : p.Text3);
            dial.TrackBrush = Palette.B(p.Track);
            dial.KnobBrush = Palette.B(p.ThumbRing);
            dial.KnobStrokeBrush = Palette.B(p.CtrlStroke);
            dial.TickBrush = Palette.B(p.CtrlStroke);
            dial.TickActiveBrush = Palette.B(p.Text2);
            dial.LabelBrush = Palette.B(p.Text3);

            // Mode
            for (int m = 0; m < modeCards.Count; m++) ShowMode(modeCards[m], m, st?.Mode == m, p);

            // Fan speed
            int speed = st?.FanSpeed ?? 3;
            bool fanAuto = speed == 0;
            int level = fanAuto ? (lastFan.TryGetValue(unit.Config.Mac, out var lf) ? lf : 3) : speed;
            if (!fanAuto) lastFan[unit.Config.Mac] = speed;
            fanPanel.Status.Text = st == null ? "" : fanAuto ? "Automatic: the unit sets the speed" : FanLevelNames[speed - 1];
            fanPanel.Arc.Show(p, level - 1, false, known && !fanAuto);
            fanScene.Show(p, on, fanAuto, level);
            ShowToggle(fanPanel.Auto, fanPanel.AutoIcon, fanAuto && known, p);
            AutomationProperties.SetName(fanScene, "Fan, " + (fanAuto ? "automatic speed" : FanLevelNames[level - 1].ToLowerInvariant() + " speed"));

            // Up and down, left and right
            var (vPos, vSwing, vStatus) = st != null ? Louvre(st, "SwUpDn", VerticalNames) : (-1, false, "");
            var (hPos, hSwing, hStatus) = st != null ? Louvre(st, "SwingLfRig", HorizontalNames) : (-1, false, "");
            ShowLouvre(vPanel, vPos, vSwing, vStatus, st?.Get("SwUpDn") == 0 && known, p);
            ShowLouvre(hPanel, hPos, hSwing, hStatus, st?.Get("SwingLfRig") == 0 && known, p);
            sideScene.Show(p, vPos, vSwing);
            int vi = vSwing || vPos < 0 ? 2 : vPos;
            frontScene.Show(p, hPos, hSwing, vi, on, st?.SetTemp ?? 0);

            foreach (var panel in new[] { fanPanel, vPanel, hPanel })
            {
                panel.Status.Foreground = Palette.B(p.Text3);
                panel.Well.Background = Palette.B(p.Well);
                Colour(panel.Icon, p.Text);
            }

            // More options
            foreach (var t in tiles) ShowTile(t, st, p);
        }
        finally
        {
            syncing = false;
        }
    }

    void ShowMode(ModeCard c, int m, bool selected, Palette p)
    {
        var (_, _, deepHex, lightHex) = Modes[m];
        Color deep = Paint.Hex(deepHex), light = Paint.Hex(lightHex);
        Point from = new(0.18, 0.11), to = new(0.82, 0.89); // 140°
        if (selected)
        {
            c.Face.Background = Draw.Gradient(from, to, (0, deep), (1, light));
            c.Tint.Background = null;
            c.Face.BorderBrush = Palette.B(deep);
            c.Badge.Background = Palette.B(Paint.Hex(0xFFFFFF, 0x3D));
            Colour(c.Icon, Paint.Hex(0xFFFFFF));
            Colour(c.Mark, Paint.Hex(0xFFFFFF, 0x47));
            c.Name.Foreground = Palette.B(Paint.Hex(0xFFFFFF));
            c.Desc.Foreground = Palette.B(Paint.Hex(0xFFFFFF, 0xE0));
        }
        else
        {
            c.Face.Background = Palette.B(p.Ctrl);
            c.Tint.Background = Draw.Gradient(from, to, (0, Paint.WithAlpha(light, 0.18)), (0.65, Paint.WithAlpha(light, 0)));
            c.Face.BorderBrush = Palette.B(p.CtrlStroke);
            c.Badge.Background = Palette.B(Paint.WithAlpha(light, 0.2));
            Colour(c.Icon, deep);
            Colour(c.Mark, Paint.WithAlpha(light, 0.25));
            c.Name.Foreground = Palette.B(p.Text);
            c.Desc.Foreground = Palette.B(p.Text3);
        }
        AutomationProperties.SetItemStatus(c.Button, selected ? "Selected" : "");
    }

    static void ShowToggle(ToggleButton b, IconParts icon, bool on, Palette p)
    {
        b.IsChecked = on;
        Colour(icon, on ? p.OnAccent : p.Text);
    }

    void ShowLouvre(FlowPanel panel, int pos, bool swing, string status, bool auto, Palette p)
    {
        panel.Status.Text = status;
        panel.Arc.Show(p, pos, swing, !swing && pos >= 0);
        ShowToggle(panel.Auto, panel.AutoIcon, auto, p);
        ShowToggle(panel.Swing!, panel.SwingIcon!, swing, p);
        // The sweep is dashed while swing is off and solid while it's on (dashes are in stroke widths).
        panel.SwingIcon!.Strokes[0].StrokeDashArray = swing ? null : [2.4 / 1.8, 2.2 / 1.8];
    }

    void ShowTile(OptionCard t, AcState? st, Palette p)
    {
        var (key, name, desc, _, _) = Extras[t.Index];
        bool on = st?.Is(key) == true;
        // Turbo only works in Cool and Heat: its row is greyed out, as in Windows Settings, but keeps its ⓘ.
        bool disabled = st == null || (key == "Tur" && st.Mode is not (AcState.Cool or AcState.Heat));
        t.Toggle.IsEnabled = !disabled;
        t.Toggle.IsOn = on && !disabled;
        // After a theme change or switching units the switch can keep drawing its old state: snap it to the right one.
        VisualStateManager.GoToState(t.Toggle, t.Toggle.IsOn ? "Off" : "On", false);
        VisualStateManager.GoToState(t.Toggle, t.Toggle.IsOn ? "On" : "Off", false);
        t.Desc.Text = key == "Tur" && disabled && st != null ? "Only in Cool and Heat" : desc;
        var fg = Palette.B(disabled ? p.Text3 : p.Text);
        t.Name.Foreground = t.Icon.Foreground = fg;
        t.Desc.Foreground = Palette.B(p.Text2);
        t.Info.Foreground = Palette.B(p.Text2);
        AutomationProperties.SetItemStatus(t.Toggle, disabled && st != null ? "Only in Cool and Heat" : "");
    }

    // ------------------------------------------------------------ changes

    void Power_Toggled(object sender, RoutedEventArgs e)
    {
        // Only when it differs: the switch can report a change made by Sync (for another unit) a moment later.
        if (!syncing && unit?.Shown is { } st && PowerSwitch.IsOn != st.On) unit.Set(("Pow", PowerSwitch.IsOn ? 1 : 0));
    }

    void ChangeTemp(int delta)
    {
        if (unit?.Shown is not { } st) return;
        unit.Set(("SetTem", Math.Clamp(st.SetTemp + delta, AcState.MinTemp, AcState.MaxTemp)), ("TemUn", 0));
    }

    void SetFan(int speed)
    {
        if (unit == null) return;
        lastFan[unit.Config.Mac] = speed;
        unit.Set(("WdSpd", speed));
    }

    void ToggleFanAuto()
    {
        if (unit?.Shown is not { } st) return;
        if (st.FanSpeed == 0) unit.Set(("WdSpd", lastFan.TryGetValue(unit.Config.Mac, out var last) ? last : 3));
        else
        {
            lastFan[unit.Config.Mac] = st.FanSpeed;
            unit.Set(("WdSpd", 0));
        }
    }

    void ToggleSwing(string key)
    {
        if (unit?.Shown is not { } st) return;
        bool swinging = st.Get(key) == 1 || (key == "SwUpDn" && st.Get(key) is >= 7 and <= 11);
        // Turning swing off goes back to the last fixed direction, or lets the unit choose.
        int off = lastPosition.TryGetValue(unit.Config.Mac + key, out var last) ? last : 0;
        unit.Set((key, swinging ? off : 1));
    }

    void ToggleExtra(int i)
    {
        if (unit?.Shown is not { } st) return;
        string key = Extras[i].Key;
        bool turnOn = !st.Is(key);
        // Quiet uses 2 for "on" (1 is an automatic mode on some units). Turbo and Quiet exclude each other.
        if (key == "Tur" && turnOn) unit.Set(("Tur", 1), ("Quiet", 0));
        else if (key == "Quiet" && turnOn) unit.Set(("Quiet", 2), ("Tur", 0));
        else unit.Set((key, turnOn ? 1 : 0));
    }

    void Retry_Click(object sender, RoutedEventArgs e) => _ = unit?.RefreshAsync();

    void RenameFlyout_Opening(object sender, object e)
    {
        RenameBox.Text = unit?.Config.Name ?? "";
        RenameBox.SelectAll();
    }

    void RenameSave_Click(object sender, RoutedEventArgs e) => SaveName();

    void RenameBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter) SaveName();
    }

    void SaveName()
    {
        if (unit != null && RenameBox.Text.Trim().Length > 0) Hub.Rename(unit, RenameBox.Text);
        RenameFlyout.Hide();
    }

    async void Find_Click(object sender, RoutedEventArgs e)
    {
        FindButton.IsEnabled = false;
        FindButton.Content = "Searching…";
        try
        {
            SetupBar.Message = await Hub.FindAsync();
        }
        catch (Exception ex)
        {
            SetupBar.Message = "Search failed: " + ex.Message;
        }
        finally
        {
            FindButton.IsEnabled = true;
            FindButton.Content = "Find air conditioners";
        }
    }
}
