using System.Net;
using CommunityToolkit.WinUI.Controls;
using HomeControl.Art;
using HomeControl.Controls;
using HomeControl.Core;
using HomeControl.Shell;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace HomeControl.Pages;

public sealed partial class SettingsPage : Page
{
    const string SnowflakeData = "F1 M8.62,8 L8.62,0.8 L7.38,0.8 L7.38,8 Z M8.4,3.22 L6.41,1.55 L5.61,2.51 L7.6,4.18 Z M8.4,4.18 L10.39,2.51 L9.59,1.55 L7.6,3.22 Z M8.31,8.54 L14.55,4.94 L13.92,3.86 L7.69,7.46 Z M12.34,5.96 L12.79,3.4 L11.56,3.18 L11.11,5.74 Z M11.51,6.44 L13.95,7.33 L14.38,6.15 L11.94,5.26 Z M7.69,8.54 L13.92,12.14 L14.55,11.06 L8.31,7.46 Z M11.94,10.74 L14.38,9.85 L13.95,8.67 L11.51,9.56 Z M11.11,10.26 L11.56,12.82 L12.79,12.6 L12.34,10.04 Z M7.38,8 L7.38,15.2 L8.62,15.2 L8.62,8 Z M7.6,12.78 L9.59,14.45 L10.39,13.49 L8.4,11.82 Z M7.6,11.82 L5.61,13.49 L6.41,14.45 L8.4,12.78 Z M7.69,7.46 L1.45,11.06 L2.08,12.14 L8.31,8.54 Z M3.66,10.04 L3.21,12.6 L4.44,12.82 L4.89,10.26 Z M4.49,9.56 L2.05,8.67 L1.62,9.85 L4.06,10.74 Z M8.31,7.46 L2.08,3.86 L1.45,4.94 L7.69,8.54 Z M4.06,5.26 L1.62,6.15 L2.05,7.33 L4.49,6.44 Z M4.89,5.74 L4.44,3.18 L3.21,3.4 L3.66,5.96 Z";

    bool syncing;
    readonly List<(AcUnit Unit, SettingsExpander Row, Ellipse Dot, TextBlock Status)> acRows = [];

    static App A => App.Instance!;

    public SettingsPage()
    {
        syncing = true; // controls raise change events while the XAML loads

        InitializeComponent();

        syncing = false;
        foreach (var (code, label) in TuyaCloud.Regions)
            CloudRegion.Items.Add(new ComboBoxItem { Content = label, Tag = code });
        AboutExpander.Description = $"Version {AppInfo.Version} · WinUI 3 and the Windows App SDK";
        ConfigCard.Description = AppInfo.ConfigPath;
        Notice.Closed += (_, _) => Notice.Visibility = Visibility.Collapsed;
        Loaded += (_, _) => Load();
        A.Acs.ListChanged += LoadAcs;
        A.Acs.Changed += UpdateStatus;
        A.Session.Changed += UpdateStatus;
        ActualThemeChanged += (_, _) => UpdateStatus();
        Load();
    }

    static void Select(ComboBox box, string tag) =>
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag);

    static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    void Load()
    {
        syncing = true;
        var c = A.Config;
        Select(ConnBox, c.Connection);

        DeviceId.Text = c.Device.Id;
        DeviceIp.Text = c.Device.Ip;
        if (!DeviceVersion.Items.Contains(c.Device.Version)) DeviceVersion.Items.Add(c.Device.Version);
        DeviceVersion.SelectedItem = c.Device.Version;
        DeviceKey.Password = "";
        DeviceKey.PlaceholderText = c.Device.Key.Length > 0 ? "Saved. Leave blank to keep it." : "Not set";
        bool cloud = c.Cloud.ApiKey.Length > 0 && c.Cloud.ApiSecret.Length > 0;
        DeviceExpander.Header = string.IsNullOrWhiteSpace(c.Device.Name) ? "Light" : c.Device.Name;
        DeviceExpander.Description = (c.Device.Ip.Length > 0 ? $"Tuya · {c.Device.Ip} · " : "Tuya · no address yet · ") + (c.Device.Key.Length > 0 ? "local key saved" : "no local key")
                                     + (cloud ? " · cloud backup on" : "");
        string region = TuyaCloud.Regions.FirstOrDefault(r => r.Item1 == c.Cloud.Region).Item2 ?? c.Cloud.Region;
        CloudExpander.Description = cloud ? $"{region} data centre · Access ID and secret saved" : "Not set up. Optional: a backup when the home network fails, and the way to get the light's local key.";

        Select(CloudRegion, c.Cloud.Region);
        CloudKey.Text = c.Cloud.ApiKey;
        CloudSecret.Password = "";
        CloudSecret.PlaceholderText = c.Cloud.ApiSecret.Length > 0 ? "Saved. Leave blank to keep it." : "Not set";

        Select(ThemeBox, c.Theme);
        TrayToggle.IsOn = c.CloseToTray;
        StartupToggle.IsOn = WinIntegration.StartupEnabled;
        syncing = false;
        LoadAcs();
    }

    /// <summary>One expandable row per air conditioner: name, address and status, with Rename and Remove inside.</summary>
    void LoadAcs()
    {
        AcList.Children.Clear();
        acRows.Clear();
        foreach (var u in A.Acs.Units)
        {
            var unit = u;
            string mac = string.Join(":", Enumerable.Range(0, u.Config.Mac.Length / 2).Select(i => u.Config.Mac.Substring(i * 2, 2)));
            var dot = new Ellipse { Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center };
            var status = new TextBlock { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            status.SetValue(TextBlock.ForegroundProperty, Resources.TryGetValue("TextFillColorSecondaryBrush", out var b) ? b : null);

            var nameBox = new TextBox { Width = 220, Text = u.Config.Name, PlaceholderText = "For example, Bedroom" };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(nameBox, "Name");
            var rename = new Button { Content = "Save", MinWidth = 80 };
            rename.Click += (_, _) =>
            {
                if (nameBox.Text.Trim().Length == 0) return;
                A.Acs.Rename(unit, nameBox.Text);
                Notify(InfoBarSeverity.Success, "Renamed", $"It's now called {unit.Config.DisplayName}.");
            };
            var remove = new Button { Content = "Remove" };
            remove.Click += (_, _) =>
            {
                A.Acs.Remove(unit);
                Notify(InfoBarSeverity.Informational, "Removed", $"{unit.Config.DisplayName} was removed. Use Find devices to add it again.");
            };
            var row = new SettingsExpander
            {
                Header = u.Config.DisplayName,
                Description = $"Gree · {u.Config.Ip} · {mac}",
                HeaderIcon = new PathIcon { Data = Draw.Geo(SnowflakeData) },
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { dot, status } },
                Items =
                {
                    new SettingsCard { Header = "Name", Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { nameBox, rename } } },
                    new SettingsCard { Header = "Remove", Description = "Unpairs this unit. Find devices adds it back.", Content = remove },
                },
            };
            AcList.Children.Add(row);
            acRows.Add((unit, row, dot, status));
        }
        NoAcCard.Visibility = A.Acs.Units.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus();
    }

    /// <summary>The status dot and text on each device row.</summary>
    void UpdateStatus()
    {
        var p = Palette.For(this);
        Color Dot(string kind) => kind switch
        {
            "ok" => p.Dark ? Paint.Hex(0x6CCB5F) : Paint.Hex(0x0F7B0F),
            "error" => p.Dark ? Paint.Hex(0xFF99A4) : Paint.Hex(0xC42B1C),
            "busy" => p.Dark ? Paint.Hex(0xFCE100) : Paint.Hex(0x9D5D00),
            _ => p.Text3,
        };
        string kind = A.Session.StatusKind;
        LightDot.Fill = Palette.B(Dot(kind));
        LightStatus.Text = kind switch
        {
            "ok" => A.Session.Shown?.Via == "cloud" ? "Connected · Tuya Cloud" : "Connected",
            "busy" => "Connecting…",
            "setup" => "Not set up",
            _ => "Not connected",
        };
        foreach (var (unit, row, dot, status) in acRows)
        {
            dot.Fill = Palette.B(Dot(unit.StatusKind));
            status.Text = unit.StatusKind switch { "ok" => "Connected", "busy" => "Connecting…", _ => "Not connected" };
            status.Foreground = Palette.B(p.Text2);
            row.Header = unit.Config.DisplayName;
        }
        LightStatus.Foreground = Palette.B(p.Text2);
    }

    async void FindAc_Click(object sender, RoutedEventArgs e)
    {
        FindAcButton.IsEnabled = false;
        Busy.IsActive = true;
        try
        {
            string result = await A.Acs.FindAsync();
            Notify(A.Acs.Units.Count > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning, "Air conditioners", result);
        }
        catch (Exception ex)
        {
            Notify(InfoBarSeverity.Error, "Search failed", ex.Message);
        }
        finally
        {
            FindAcButton.IsEnabled = true;
            Busy.IsActive = false;
        }
    }

    void Notify(InfoBarSeverity severity, string title, string message)
    {
        Notice.Severity = severity;
        Notice.Title = title;
        Notice.Message = message;
        Notice.IsOpen = true;
        Notice.Visibility = Visibility.Visible;
    }

    bool Save()
    {
        try
        {
            A.Config.Save();
            return true;
        }
        catch (Exception e)
        {
            Notify(InfoBarSeverity.Error, "Couldn't save settings", e.Message);
            return false;
        }
    }

    void Reconnect()
    {
        A.Controller.Reset();
        _ = A.Session.RefreshAsync();
    }

    void Conn_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (syncing || SelectedTag(ConnBox) is not { } mode) return;
        A.Config.Connection = mode;
        if (Save()) Reconnect();
    }

    async void Check_Click(object sender, RoutedEventArgs e)
    {
        CheckButton.IsEnabled = false;
        Busy.IsActive = true;
        try
        {
            var steps = await Task.Run(() => A.Controller.DiagnoseAsync());
            Load(); // the IP may have been updated
            _ = A.Session.RefreshAsync();
            await ShowCheckAsync(steps);
        }
        catch (Exception ex)
        {
            Notify(InfoBarSeverity.Error, "Check failed", LightController.Friendly(ex));
        }
        finally
        {
            CheckButton.IsEnabled = true;
            Busy.IsActive = false;
        }
    }

    async Task ShowCheckAsync(List<LightController.CheckStep> steps)
    {
        var list = new StackPanel { Spacing = 14, MinWidth = 380 };
        foreach (var s in steps)
        {
            var row = new Grid { ColumnSpacing = 12 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = new FontIcon
            {
                FontSize = 16,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 0, 0),
                Glyph = s.Ok switch { true => "", false => "", _ => "" },
                Foreground = (Brush)Application.Current.Resources[s.Ok switch
                {
                    true => "SystemFillColorSuccessBrush",
                    false => "SystemFillColorCriticalBrush",
                    _ => "TextFillColorSecondaryBrush",
                }],
            };
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = s.Title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"] });
            text.Children.Add(new TextBlock { Text = s.Detail, TextWrapping = TextWrapping.Wrap,
                                              Style = (Style)Application.Current.Resources["SecondaryText"] });
            Grid.SetColumn(text, 1);
            row.Children.Add(icon);
            row.Children.Add(text);
            list.Children.Add(row);
        }
        var dialog = new ContentDialog
        {
            Title = "Connection check",
            Content = list,
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ActualTheme,
        };
        await dialog.ShowAsync();
    }

    void SaveDevice_Click(object sender, RoutedEventArgs e)
    {
        string id = DeviceId.Text.Trim(), ip = DeviceIp.Text.Trim(), key = DeviceKey.Password.Trim();
        if (id.Length == 0)
        {
            Notify(InfoBarSeverity.Warning, "Device ID is empty", "Enter the light's device ID.");
            return;
        }
        if (!IPAddress.TryParse(ip, out _))
        {
            Notify(InfoBarSeverity.Warning, "Check the IP address", $"\"{ip}\" isn't a valid IP address, like 192.168.1.50.");
            return;
        }
        if (key.Length > 0 && key.Length != 16)
        {
            Notify(InfoBarSeverity.Warning, "Check the local key", "A Tuya local key is 16 characters long.");
            return;
        }
        var d = A.Config.Device;
        d.Id = id;
        d.Ip = ip;
        d.Version = DeviceVersion.SelectedItem as string ?? "3.5";
        if (key.Length > 0) d.Key = key;
        if (!Save()) return;
        Reconnect();
        Load();
        Notify(InfoBarSeverity.Success, "Saved", "Reconnecting with the new details.");
    }

    bool StoreCloud()
    {
        var c = A.Config.Cloud;
        c.Region = SelectedTag(CloudRegion) ?? "eu";
        c.ApiKey = CloudKey.Text.Trim();
        if (CloudSecret.Password.Trim().Length > 0) c.ApiSecret = CloudSecret.Password.Trim();
        return Save();
    }

    void SaveCloud_Click(object sender, RoutedEventArgs e)
    {
        if (!StoreCloud()) return;
        Reconnect();
        Load();
        Notify(InfoBarSeverity.Success, "Saved", "Tuya Cloud details saved.");
    }

    async void FetchKey_Click(object sender, RoutedEventArgs e)
    {
        if (!StoreCloud()) return;
        CloudBusy.IsActive = true;
        try
        {
            string name = await Task.Run(() => A.Controller.FetchKeyFromCloudAsync());
            Load();
            _ = A.Session.RefreshAsync();
            Notify(InfoBarSeverity.Success, "Got the local key",
                   $"Saved the key for {(string.IsNullOrEmpty(name) ? "your light" : name)}. Home network control is ready.");
        }
        catch (Exception ex)
        {
            Notify(InfoBarSeverity.Error, "Couldn't get the local key", LightController.Friendly(ex));
        }
        finally
        {
            CloudBusy.IsActive = false;
        }
    }

    void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (syncing || SelectedTag(ThemeBox) is not { } theme) return;
        A.Config.Theme = theme;
        Save();
        A.Window.ApplyTheme(theme);
    }

    void Tray_Toggled(object sender, RoutedEventArgs e)
    {
        if (syncing) return;
        A.Config.CloseToTray = TrayToggle.IsOn;
        Save();
    }

    void Startup_Toggled(object sender, RoutedEventArgs e)
    {
        if (syncing) return;
        try
        {
            WinIntegration.SetStartup(StartupToggle.IsOn);
        }
        catch (Exception ex)
        {
            Notify(InfoBarSeverity.Error, "Couldn't change the startup setting", ex.Message);
            syncing = true;
            StartupToggle.IsOn = WinIntegration.StartupEnabled;
            syncing = false;
        }
    }

    void OpenConfig_Click(object sender, RoutedEventArgs e) => WinIntegration.OpenFolder(AppInfo.ConfigDir);

    public void ExpandAllForScreenshot()
    {
        DeviceExpander.IsExpanded = true;
        CloudExpander.IsExpanded = true;
        foreach (var r in acRows) r.Row.IsExpanded = true;
        AboutExpander.IsExpanded = true;
    }
}
