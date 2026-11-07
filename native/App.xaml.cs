using System.Text.Json;
using LedControl.Core;
using LedControl.Music;
using LedControl.Shell;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace LedControl;

public partial class App : Application
{
    public static App? Instance { get; private set; }

    public AppConfig Config { get; }
    public LightController Controller { get; }
    public MusicEngine Music { get; }
    public LightSession Session { get; private set; } = null!;
    public AcHub Acs { get; private set; } = null!;
    public MainWindow Window { get; private set; } = null!;
    public DispatcherQueue UI { get; private set; } = null!;
    public bool TrayAvailable => tray?.IsCreated == true;

    readonly string[] args;
    Tray? tray;
    bool quitting;

    public App(string[] args)
    {
        Instance = this;
        this.args = args;
        InitializeComponent();
        Config = AppConfig.Load();
        Controller = new LightController(Config);
        Music = new MusicEngine(Controller);
        ApplyMusicPrefs();
        UnhandledException += (_, e) =>
        {
            Log("Unhandled: " + e.Exception);
            e.Handled = true; // keep running; errors are shown where they happen
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log("Unobserved: " + e.Exception);
            e.SetObserved();
        };
    }

    public void ApplyMusicPrefs()
    {
        var m = Config.Music;
        Music.Style = m.Style;
        Music.Sensitivity = m.Sensitivity;
        Music.Hue = m.Hue;
        Music.Fps = m.Fps;
        Music.Source = m.Source;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs e)
    {
        UI = DispatcherQueue.GetForCurrentThread();
        Session = new LightSession(Controller, Music, UI);
        Acs = new AcHub(Config, UI);
        Window = new MainWindow();

        string? selfTest = ArgValue("--self-test"), shots = ArgValue("--screenshots");
        if (shots != null)
        {
            _ = Window.CaptureScreenshotsAsync(shots).ContinueWith(t =>
            {
                if (t.Exception != null) Log("Screenshots failed: " + t.Exception);
                UI.TryEnqueue(Exit);
            });
            Session.Start();
            Acs.Start();
            return;
        }

        try
        {
            tray = new Tray(this);
        }
        catch (Exception ex)
        {
            Log("Tray icon failed: " + ex);
        }
        Session.Start();
        Acs.Start();
        if (!args.Contains("--minimized") || !TrayAvailable) Window.ShowAndFocus();
        if (selfTest != null) _ = RunSelfTestAsync(selfTest);
    }

    string? ArgValue(string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    public void OnRedirectedActivation() => UI?.TryEnqueue(() => Window?.ShowAndFocus());

    QuickPanel? quickPanel;

    /// <summary>The notification-area icon was clicked: open or close the quick panel.</summary>
    public void ToggleQuickPanel()
    {
        quickPanel ??= new QuickPanel();
        quickPanel.Toggle();
    }

    public void ToggleWindow()
    {
        if (Window.AppWindow.IsVisible && Window.IsForeground) Window.AppWindow.Hide();
        else Window.ShowAndFocus();
    }

    /// <summary>The window's close button: hide to the tray, or quit.</summary>
    public bool OnWindowClosing()
    {
        if (quitting) return false;
        if (Config.CloseToTray && TrayAvailable)
        {
            Window.AppWindow.Hide();
            return true; // cancel the close
        }
        Quit();
        return false;
    }

    public void Quit()
    {
        if (quitting) return;
        quitting = true;
        Session.Shutdown(); // stops music sync and restores the light
        Acs.Stop();
        tray?.Dispose();
        Controller.Dispose();
        Exit();
    }

    async Task RunSelfTestAsync(string outPath)
    {
        var report = new Dictionary<string, object?> { ["version"] = AppInfo.Version };
        for (int i = 0; i < 80 && Session.StatusKind == "busy"; i++) await Task.Delay(100);
        report["status_kind"] = Session.StatusKind;
        report["status"] = Session.StatusText;
        report["state"] = Session.Shown?.Describe();
        report["tray_available"] = TrayAvailable;
        report["window_visible"] = Window.AppWindow.IsVisible;
        for (int i = 0; i < 80 && Acs.Units.Any(u => u.StatusKind == "busy"); i++) await Task.Delay(100);
        report["acs"] = Acs.Units.Select(u => $"{u.Config.DisplayName} ({u.Config.Ip}): {u.StatusKind}, " +
                                              $"{u.Shown?.Describe()}, room {u.Shown?.RoomTemp} °C").ToList();
        try
        {
            var sources = AudioCapture.ListSources();
            report["audio_sources"] = sources.Count;
            int blocks = await Task.Run(() =>
            {
                using var cap = new AudioCapture(MusicPrefs.SystemSource);
                cap.Start();
                int n = 0;
                var until = Environment.TickCount64 + 800;
                while (Environment.TickCount64 < until) if (cap.Take(100) != null) n++;
                return n;
            });
            report["audio_capture_blocks"] = blocks;
        }
        catch (Exception ex)
        {
            report["audio_error"] = ex.Message;
        }
        File.WriteAllText(outPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Quit();
    }

    public static void Log(string text)
    {
        try
        {
            Directory.CreateDirectory(AppInfo.ConfigDir);
            File.AppendAllText(Path.Combine(AppInfo.ConfigDir, "log.txt"), $"{DateTime.Now:s} {text}\n");
        }
        catch
        {
            // logging must never crash the app
        }
    }
}
