using System.Runtime.InteropServices.WindowsRuntime;
using LedControl.Core;
using LedControl.Pages;
using LedControl.Shell;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.UI;

namespace LedControl;

public sealed partial class MainWindow : Window
{
    readonly IntPtr hwnd;

    public MainWindow()
    {
        InitializeComponent();
        hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"));
        AppWindow.Title = AppInfo.Name;

        double scale = WinIntegration.GetDpiForWindow(hwnd) / 96.0;
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.PreferredMinimumWidth = (int)(520 * scale);
            p.PreferredMinimumHeight = (int)(560 * scale);
        }
        var size = new SizeInt32((int)(1040 * scale), (int)(800 * scale));
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        size.Width = Math.Min(size.Width, area.Width);
        size.Height = Math.Min(size.Height, area.Height);
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - size.Width) / 2,
                                              area.Y + (area.Height - size.Height) / 2, size.Width, size.Height));

        AppWindow.Closing += (_, e) => e.Cancel = App.Instance!.OnWindowClosing();
        Root.ActualThemeChanged += (_, _) => UpdateCaptionButtons();

        App.Instance!.Session.MusicChanged += () =>
            MusicBadge.Visibility = App.Instance.Session.MusicRunning ? Visibility.Visible : Visibility.Collapsed;

        ApplyTheme(App.Instance.Config.Theme);
        Nav.SelectedItem = HomeNav;
    }

    public bool IsForeground => GetForegroundWindow() == hwnd;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    public void ShowAndFocus()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } p) p.Restore();
        AppWindow.Show();
        Activate();
        WinIntegration.SetForegroundWindow(hwnd);
    }

    public void ApplyTheme(string theme)
    {
        Root.RequestedTheme = theme switch
        {
            "light" => ElementTheme.Light,
            "dark" => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        UpdateCaptionButtons();
    }

    /// <summary>Caption buttons (minimise/maximise/close) don't follow an app-chosen theme by themselves.</summary>
    void UpdateCaptionButtons()
    {
        var tb = AppWindow.TitleBar;
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        tb.ButtonBackgroundColor = Colors.Transparent;
        tb.ButtonInactiveBackgroundColor = Colors.Transparent;
        tb.ButtonForegroundColor = dark ? Colors.White : Colors.Black;
        tb.ButtonInactiveForegroundColor = dark ? Color.FromArgb(0xFF, 0x9E, 0x9E, 0x9E) : Color.FromArgb(0xFF, 0x80, 0x80, 0x80);
        tb.ButtonHoverBackgroundColor = dark ? Color.FromArgb(0x15, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0F, 0x00, 0x00, 0x00);
        tb.ButtonHoverForegroundColor = tb.ButtonForegroundColor;
        tb.ButtonPressedBackgroundColor = dark ? Color.FromArgb(0x0B, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0A, 0x00, 0x00, 0x00);
        tb.ButtonPressedForegroundColor = tb.ButtonForegroundColor;
    }

    void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        Type page = args.IsSettingsSelected ? typeof(SettingsPage)
                  : ((args.SelectedItem as NavigationViewItem)?.Tag as string) switch
                  {
                      "light" => typeof(LightPage),
                      "music" => typeof(MusicPage),
                      "ac" => typeof(AcPage),
                      _ => typeof(HomePage),
                  };
        if (ContentFrame.CurrentSourcePageType != page)
            ContentFrame.Navigate(page, null, args.RecommendedNavigationTransitionInfo ?? new EntranceNavigationTransitionInfo());
    }

    public void ShowPage(string tag)
    {
        Nav.SelectedItem = tag switch
        {
            "settings" => Nav.SettingsItem,
            "light" => LightNav,
            "music" => MusicNav,
            "ac" => AcNav,
            _ => HomeNav,
        };
    }

    /// <summary>Opens the Air conditioners page on a specific unit.</summary>
    public void ShowAc(AcUnit unit)
    {
        ShowPage("ac");
        if (ContentFrame.Content is AcPage page) page.Select(unit);
        ShowAndFocus();
    }

    // ------------------------------------------------------------ screenshots (for checking the design)

    public async Task CaptureScreenshotsAsync(string dir)
    {
        Directory.CreateDirectory(dir);
        // Off screen, so nothing flashes up on the desktop.
        AppWindow.Move(new PointInt32(-30000, -30000));
        Activate();
        await Task.Delay(2500);
        foreach (var theme in new[] { "light", "dark" })
        {
            ApplyTheme(theme);
            // Mica isn't captured by RenderTargetBitmap: paint its typical base colour instead.
            Root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(
                theme == "dark" ? Color.FromArgb(255, 0x20, 0x20, 0x20) : Color.FromArgb(255, 0xF3, 0xF3, 0xF3));
            foreach (var tag in new[] { "home", "light", "music", "ac", "settings" })
            {
                ShowPage(tag);
                await Task.Delay(900);
                await SaveAsync(Root, Path.Combine(dir, $"{tag}-{theme}.png"));
                if (tag == "settings" && ContentFrame.Content is SettingsPage sp)
                {
                    sp.ExpandAllForScreenshot();
                    await Task.Delay(700);
                }
                // The whole page, including what's below the fold.
                if (ContentFrame.Content is Page { Content: ScrollViewer { Content: Panel body } })
                {
                    body.Background = Root.Background;
                    await SaveAsync(body, Path.Combine(dir, $"{tag}-full-{theme}.png"));
                    body.Background = null;
                }
                // The second air conditioner too, when there is one (shows other louvre and switch states).
                if (tag == "ac" && App.Instance!.Acs.Units.Count > 1 && ContentFrame.Content is AcPage acPage)
                {
                    acPage.Select(App.Instance.Acs.Units[1]);
                    await Task.Delay(700);
                    if (acPage.Content is ScrollViewer { Content: Panel acBody })
                    {
                        acBody.Background = Root.Background;
                        await SaveAsync(acBody, Path.Combine(dir, $"ac2-full-{theme}.png"));
                        acBody.Background = null;
                    }
                    acPage.Select(App.Instance.Acs.Units[0]);
                }
            }
        }
        // A narrow window too, to check the layout adapts.
        AppWindow.Resize(new SizeInt32((int)(600 * WinIntegration.GetDpiForWindow(hwnd) / 96.0), AppWindow.Size.Height));
        ApplyTheme("light");
        Root.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Color.FromArgb(255, 0xF3, 0xF3, 0xF3));
        ShowPage("home");
        await Task.Delay(1200);
        await SaveAsync(Root, Path.Combine(dir, "home-narrow.png"));
        if (ContentFrame.Content is Page { Content: ScrollViewer { Content: Panel narrowBody } })
        {
            narrowBody.Background = Root.Background;
            await SaveAsync(narrowBody, Path.Combine(dir, "home-narrow-full.png"));
            narrowBody.Background = null;
        }
        ShowPage("light");
        await Task.Delay(1200);
        await SaveAsync(Root, Path.Combine(dir, "light-narrow.png"));
        ShowPage("ac");
        await Task.Delay(1200);
        await SaveAsync(Root, Path.Combine(dir, "ac-narrow.png"));
    }

    static async Task SaveAsync(UIElement element, string path)
    {
        var rtb = new RenderTargetBitmap();
        await rtb.RenderAsync(element);
        var pixels = (await rtb.GetPixelsAsync()).ToArray();
        using var fs = File.Create(path);
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, fs.AsRandomAccessStream());
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                             (uint)rtb.PixelWidth, (uint)rtb.PixelHeight, 96, 96, pixels);
        await encoder.FlushAsync();
    }
}
