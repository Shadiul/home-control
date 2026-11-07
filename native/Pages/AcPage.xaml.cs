using LedControl.Core;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace LedControl.Pages;

public sealed partial class AcPage : Page
{
    const string FanPath =
        "M8,8 C6.08,5.28 5.92,1.76 8.48,1.28 C11.04,0.8 11.52,4.32 8,8 Z M8,8 C11.32,7.7 14.44,9.32 13.58,11.78 " +
        "C12.72,14.23 9.43,12.89 8,8 Z M8,8 C6.6,11.02 3.64,12.92 1.94,10.94 C0.24,8.97 3.05,6.79 8,8 Z " +
        "M6.8,8 A1.2,1.2 0 1 0 9.2,8 A1.2,1.2 0 1 0 6.8,8 Z";

    public const string SnowPath =
        "F1 M8.62,8 L8.62,0.8 L7.38,0.8 L7.38,8 Z M8.4,3.22 L6.41,1.55 L5.61,2.51 L7.6,4.18 Z M8.4,4.18 L10.39,2.51 L9.59,1.55 L7.6,3.22 Z M8.31,8.54 L14.55,4.94 L13.92,3.86 L7.69,7.46 Z M12.34,5.96 L12.79,3.4 L11.56,3.18 L11.11,5.74 Z M11.51,6.44 L13.95,7.33 L14.38,6.15 L11.94,5.26 Z M7.69,8.54 L13.92,12.14 L14.55,11.06 L8.31,7.46 Z M11.94,10.74 L14.38,9.85 L13.95,8.67 L11.51,9.56 Z M11.11,10.26 L11.56,12.82 L12.79,12.6 L12.34,10.04 Z M7.38,8 L7.38,15.2 L8.62,15.2 L8.62,8 Z M7.6,12.78 L9.59,14.45 L10.39,13.49 L8.4,11.82 Z M7.6,11.82 L5.61,13.49 L6.41,14.45 L8.4,12.78 Z M7.69,7.46 L1.45,11.06 L2.08,12.14 L8.31,8.54 Z M3.66,10.04 L3.21,12.6 L4.44,12.82 L4.89,10.26 Z M4.49,9.56 L2.05,8.67 L1.62,9.85 L4.06,10.74 Z M8.31,7.46 L2.08,3.86 L1.45,4.94 L7.69,8.54 Z M4.06,5.26 L1.62,6.15 L2.05,7.33 L4.49,6.44 Z M4.89,5.74 L4.44,3.18 L3.21,3.4 L3.66,5.96 Z";

    static readonly Color[] ModeColours =
    [
        Color.FromArgb(255, 0x7B, 0x5C, 0xD6), // auto
        Color.FromArgb(255, 0x1F, 0x7A, 0xE8), // cool
        Color.FromArgb(255, 0x0E, 0x95, 0x95), // dry
        Color.FromArgb(255, 0x4E, 0x8A, 0x2E), // fan
        Color.FromArgb(255, 0xE0, 0x5A, 0x12), // heat
    ];

    // Louvre positions: Gree values 2-6 are fixed positions, 1 is a full swing, 0 lets the unit decide.
    // Up and down also has 7-11, a swing over part of the range (set from the remote).
    static readonly string[] VerticalNames = ["Straight ahead", "Slightly down", "Halfway down", "Mostly down", "Straight down"];
    static readonly string[] HorizontalNames = ["Far left", "Left", "Centre", "Right", "Far right"];
    static readonly string[] PartSwingNames = ["lower part", "lower middle", "middle", "upper middle", "upper part"];

    sealed record Louvre(string Key, ToggleButton[] Positions, ToggleButton Swing, SettingsCard Card, string[] Names);
    readonly Louvre vertical, horizontal;
    readonly Dictionary<string, int> lastPosition = []; // per unit and louvre, for turning swing off

    readonly List<SelectorBarItem> modeItems = [];
    readonly Dictionary<string, ToggleSwitch> extras;
    AcUnit? unit;
    bool syncing;

    static AcHub Hub => App.Instance!.Acs;

    public AcPage()
    {
        syncing = true; // controls raise change events while the XAML loads
        InitializeComponent();
        for (int m = 0; m < AcState.ModeNames.Length; m++)
        {
            var item = new SelectorBarItem { Text = AcState.ModeNames[m], Icon = ModeIcon(m), Tag = m };
            modeItems.Add(item);
            ModeBar.Items.Add(item);
        }
        for (int i = 0; i < AcState.FanNames.Length; i++) FanBox.Items.Add(new ComboBoxItem { Content = AcState.FanNames[i], Tag = i });
        VSwingIcon.Content = AirIcon(AirflowIcons.VerticalSwing, 22);
        HSwingIcon.Content = AirIcon(AirflowIcons.HorizontalSwing, 22);
        vertical = new Louvre("SwUpDn", MakePositions(VPositions, AirflowIcons.Vertical, VerticalNames, "Air direction: "),
                              VSwingButton, VCard, VerticalNames);
        horizontal = new Louvre("SwingLfRig", MakePositions(HPositions, AirflowIcons.Horizontal, HorizontalNames, "Air direction: "),
                                HSwingButton, HCard, HorizontalNames);
        VSwingButton.Tag = vertical;
        HSwingButton.Tag = horizontal;
        extras = new[] { TurboSwitch, QuietSwitch, SleepSwitch, LightSwitch, HealthSwitch, XFanSwitch }
            .ToDictionary(s => (string)s.Tag, s => s);
        syncing = false;

        Hub.ListChanged += BuildUnits;
        Hub.Changed += Sync;
        BuildUnits();
    }

    public static IconElement ModeIcon(int mode) => mode switch
    {
        AcState.Cool => Path(SnowPath),
        AcState.Dry => new FontIcon { Glyph = "" },
        AcState.Fan => Path(FanPath),
        AcState.Heat => new FontIcon { Glyph = "" },
        _ => new FontIcon { Glyph = "" },
    };

    static Geometry Geometry(string data) => (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), data);
    static PathIcon Path(string data) => new() { Data = Geometry(data) };

    /// <summary>A 20x20 airflow icon shown at <paramref name="size"/> pixels (same scale for every icon).</summary>
    /// The shape itself is scaled, so the icon sits directly in the button and takes its text colour
    /// (white on the accent colour when selected).
    static PathIcon AirIcon(string data, double size)
    {
        var g = Geometry(data);
        g.Transform = new ScaleTransform { ScaleX = size / 20, ScaleY = size / 20 };
        return new PathIcon { Data = g, Width = size, Height = size };
    }

    /// <summary>Five toggle buttons showing where the air goes, used like option buttons.</summary>
    ToggleButton[] MakePositions(Panel host, string[] icons, string[] names, string prefix)
    {
        var buttons = new ToggleButton[icons.Length];
        for (int i = 0; i < icons.Length; i++)
        {
            var b = new ToggleButton { Width = 46, Height = 42, Padding = new Thickness(0), Content = AirIcon(icons[i], 26), Tag = i };
            ToolTipService.SetToolTip(b, names[i]);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(b, prefix + names[i]);
            b.Click += Position_Click;
            host.Children.Add(b);
            buttons[i] = b;
        }
        return buttons;
    }

    /// <summary>Shows a specific unit (from the tray menu, for example).</summary>
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
        UnitBar.Visibility = Hub.Units.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        SetupBar.IsOpen = Hub.Units.Count == 0;
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

    void Sync()
    {
        if (unit == null) return;
        syncing = true;
        try
        {
            NameText.Text = unit.Config.DisplayName;
            string kind = unit.StatusKind;
            VisualStateManager.GoToState(this, kind switch { "ok" => "StatusOk", "error" => "StatusError", _ => "StatusBusy" }, true);
            StatusText.Text = kind == "error" ? "Not connected" : unit.StatusText;
            ErrorBar.IsOpen = kind == "error";
            ErrorBar.Message = unit.StatusText;

            var st = unit.Shown;
            foreach (var c in new Control[] { PowerSwitch, MinusButton, PlusButton, FanBox, VSwingButton, HSwingButton })
                c.IsEnabled = st != null;
            foreach (var b in vertical.Positions.Concat(horizontal.Positions)) b.IsEnabled = st != null;
            ModeBar.IsEnabled = st != null;
            foreach (var s in extras.Values) s.IsEnabled = st != null;
            if (st == null)
            {
                StateText.Text = kind == "busy" ? "Connecting…" : "Unknown";
                RoomText.Text = "";
                TempText.Text = "– °C";
                UpdateBadge(null);
                return;
            }

            PowerSwitch.IsOn = st.On;
            StateText.Text = st.Describe();
            TempText.Text = $"{st.SetTemp} °C";
            MinusButton.IsEnabled = st.UsesSetTemp && st.SetTemp > AcState.MinTemp;
            PlusButton.IsEnabled = st.UsesSetTemp && st.SetTemp < AcState.MaxTemp;
            string room = st.RoomTemp is int r ? $"Room {r} °C" : "";
            RoomText.Text = st.UsesSetTemp ? room
                          : (room.Length > 0 ? room + " · " : "") + $"Not used in {AcState.ModeNames[st.Mode]} mode";

            if (ModeBar.SelectedItem != modeItems[st.Mode]) ModeBar.SelectedItem = modeItems[st.Mode];
            FanBox.SelectedIndex = st.FanSpeed;
            SyncLouvre(vertical, st);
            SyncLouvre(horizontal, st);
            foreach (var (key, sw) in extras) sw.IsOn = st.Is(key);
            TurboSwitch.IsEnabled = st.Mode is AcState.Cool or AcState.Heat;
            UpdateBadge(st);
        }
        finally
        {
            syncing = false;
        }
    }

    void SyncLouvre(Louvre l, AcState st)
    {
        int v = st.Get(l.Key);
        int fixedAt = v is >= 2 and <= 6 ? v - 2 : -1;
        if (fixedAt >= 0) lastPosition[unit!.Config.Mac + l.Key] = v;
        for (int i = 0; i < l.Positions.Length; i++) l.Positions[i].IsChecked = i == fixedAt;
        bool partSwing = l == vertical && v is >= 7 and <= 11;
        l.Swing.IsChecked = v == 1 || partSwing;
        l.Card.Description = v switch
        {
            0 => "Automatic: the unit chooses",
            1 => "Swinging",
            _ when fixedAt >= 0 => "Fixed: " + l.Names[fixedAt].ToLowerInvariant(),
            _ when partSwing => "Swinging over the " + PartSwingNames[v - 7],
            _ => $"Set from the remote (setting {v})",
        };
    }

    void Position_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: int index } b) return;
        var l = vertical.Positions.Contains(b) ? vertical : horizontal;
        unit?.Set((l.Key, index + 2)); // picking a direction stops the swing
        Sync(); // keeps the clicked button checked even when it already was
    }

    void Swing_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton { Tag: Louvre l } b || unit == null) return;
        // Turning swing off goes back to the last fixed direction, or lets the unit choose.
        int off = lastPosition.TryGetValue(unit.Config.Mac + l.Key, out var last) ? last : 0;
        unit.Set((l.Key, b.IsChecked == true ? 1 : off));
    }

    void UpdateBadge(AcState? st)
    {
        bool on = st?.On == true;
        int mode = st?.Mode ?? AcState.Cool;
        BadgeBrush.Color = ModeColours[mode];
        Badge.Opacity = on ? 1 : 0;
        var icon = ModeIcon(mode);
        if (on) icon.Foreground = new SolidColorBrush(Colors.White);
        BadgeIcon.Child = icon;
    }

    void Badge_Click(object sender, RoutedEventArgs e)
    {
        if (unit?.Shown != null) unit.TogglePower();
    }

    void Power_Toggled(object sender, RoutedEventArgs e)
    {
        if (!syncing) unit?.Set(("Pow", PowerSwitch.IsOn ? 1 : 0));
    }

    void ChangeTemp(int delta)
    {
        if (unit?.Shown is not { } st) return;
        int t = Math.Clamp(st.SetTemp + delta, AcState.MinTemp, AcState.MaxTemp);
        unit.Set(("SetTem", t), ("TemUn", 0));
    }

    void Minus_Click(object sender, RoutedEventArgs e) => ChangeTemp(-1);
    void Plus_Click(object sender, RoutedEventArgs e) => ChangeTemp(+1);

    void Mode_Changed(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!syncing && sender.SelectedItem?.Tag is int mode) unit?.Set(("Mod", mode));
    }

    void Fan_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!syncing && FanBox.SelectedItem is ComboBoxItem { Tag: int speed }) unit?.Set(("WdSpd", speed));
    }

    void Extra_Toggled(object sender, RoutedEventArgs e)
    {
        if (syncing || sender is not ToggleSwitch { Tag: string key } sw) return;
        // Quiet uses 2 for "on" (1 is an automatic mode on some units).
        unit?.Set((key, sw.IsOn ? (key == "Quiet" ? 2 : 1) : 0));
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
