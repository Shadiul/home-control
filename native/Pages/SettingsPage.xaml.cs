using System.Net;
using LedControl.Core;
using LedControl.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace LedControl.Pages;

public sealed partial class SettingsPage : Page
{
    bool syncing;

    static App A => App.Instance!;

    public SettingsPage()
    {
        syncing = true; // controls raise change events while the XAML loads

        InitializeComponent();

        syncing = false;
        foreach (var (code, label) in TuyaCloud.Regions)
            CloudRegion.Items.Add(new ComboBoxItem { Content = label, Tag = code });
        VersionText.Text = $"Version {AppInfo.Version}";
        ConfigCard.Description = AppInfo.ConfigPath;
        Loaded += (_, _) => Load();
        A.Acs.ListChanged += LoadAcs;
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
        string name = string.IsNullOrWhiteSpace(c.Device.Name) ? "Light" : c.Device.Name;
        DeviceExpander.Description = $"{name} · {c.Device.Ip} · " + (c.Device.Key.Length > 0 ? "local key saved" : "no local key");

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

    void LoadAcs()
    {
        var units = A.Acs.Units;
        AcExpander.Description = units.Count switch
        {
            0 => "None yet. Use Find to add them.",
            1 => "1 air conditioner · Home network",
            _ => $"{units.Count} air conditioners · Home network",
        };
        AcExpander.Items.Clear();
        foreach (var u in units)
        {
            var remove = new Button { Content = "Remove" };
            var target = u;
            remove.Click += (_, _) =>
            {
                A.Acs.Remove(target);
                Notify(InfoBarSeverity.Informational, "Removed",
                       $"{target.Config.DisplayName} was removed. Use Find to add it again.");
            };
            string mac = string.Join(":", Enumerable.Range(0, u.Config.Mac.Length / 2).Select(i => u.Config.Mac.Substring(i * 2, 2)));
            AcExpander.Items.Add(new CommunityToolkit.WinUI.Controls.SettingsCard
            {
                Header = u.Config.DisplayName,
                Description = $"{u.Config.Ip} · {mac} · paired",
                Content = remove,
            });
        }
    }

    async void FindAc_Click(object sender, RoutedEventArgs e)
    {
        FindAcButton.IsEnabled = false;
        FindBusy.IsActive = true;
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
            FindBusy.IsActive = false;
        }
    }

    void Notify(InfoBarSeverity severity, string title, string message)
    {
        Notice.Severity = severity;
        Notice.Title = title;
        Notice.Message = message;
        Notice.IsOpen = true;
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
        CheckBusy.IsActive = true;
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
            CheckBusy.IsActive = false;
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
            Notify(InfoBarSeverity.Warning, "Check the IP address", $"\"{ip}\" isn't a valid IP address, like 192.168.0.167.");
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
        AcExpander.IsExpanded = true;
        AboutExpander.IsExpanded = true;
    }
}
