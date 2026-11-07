using LedControl.Core;
using LedControl.Music;
using Microsoft.UI.Dispatching;

namespace LedControl;

public sealed record Scene(string Name, string Glyph, LightCommand Command);

/// <summary>
/// UI-thread view of the light, shared by the pages and the tray menu.
///   Confirmed = what the light is known to be; Shown = including changes still being sent.
/// Rapid changes (dragging a slider) are coalesced: one command in flight, the newest waits.
/// Also starts/stops music sync and puts the light back afterwards.
/// </summary>
public sealed class LightSession
{
    public static readonly Scene[] Scenes =
    [
        new("Bright", "", LightCommand.White(100, 70)),
        new("Reading", "", LightCommand.White(80, 45)),
        new("Relax", "", LightCommand.White(35, 0)),
        new("Night", "", LightCommand.Colour(25, 95, 4)),
        new("Movie", "", LightCommand.Colour(230, 85, 15)),
        new("Party", "", LightCommand.Colour(300, 90, 100)),
    ];

    readonly LightController controller;
    readonly MusicEngine music;
    readonly DispatcherQueue ui;
    DispatcherQueueTimer? poll;
    LightCommand? pending;
    bool pumping;
    MusicFrame? latestFrame;
    int framePosted;
    LightState? beforeMusic;
    bool restoreAfterMusic;

    public LightState? Confirmed { get; private set; }
    public LightState? Shown { get; private set; }
    /// <summary>ok | busy | error | setup</summary>
    public string StatusKind { get; private set; } = "busy";
    public string StatusText { get; private set; } = "Connecting…";
    public bool Busy => pumping;

    public string MusicKind { get; private set; } = "stopped";
    public string MusicText { get; private set; } = "Not running";
    public bool MusicRunning => music.Running && MusicKind != "stopped";

    /// <summary>Light state or connection status changed (UI thread).</summary>
    public event Action? Changed;
    /// <summary>Music sync started, stopped or reported something (UI thread).</summary>
    public event Action? MusicChanged;
    /// <summary>Newest music frame (UI thread, at most one queued at a time).</summary>
    public event Action<MusicFrame>? MusicFrame;

    public LightSession(LightController controller, MusicEngine music, DispatcherQueue ui)
    {
        this.controller = controller;
        this.music = music;
        this.ui = ui;
        controller.Reported += st => ui.TryEnqueue(() => OnReported(st));
        music.Status += (kind, text) => ui.TryEnqueue(() => OnMusicStatus(kind, text));
        music.Frame += f =>
        {
            latestFrame = f;
            if (Interlocked.Exchange(ref framePosted, 1) == 0)
                ui.TryEnqueue(() =>
                {
                    framePosted = 0;
                    if (latestFrame is { } lf) MusicFrame?.Invoke(lf);
                });
        };
    }

    public void Start()
    {
        poll = ui.CreateTimer();
        poll.Interval = TimeSpan.FromSeconds(30); // the light also pushes its changes, so polling is a backup
        poll.Tick += (_, _) => _ = RefreshAsync();
        poll.Start();
        _ = RefreshAsync();
    }

    static string ViaText(string via) => via == "cloud" ? "Connected · Tuya Cloud" : "Connected · Home network";

    void SetStatus(string kind, string text)
    {
        StatusKind = kind;
        StatusText = text;
    }

    public async Task RefreshAsync()
    {
        if (MusicRunning || pumping || pending != null) return;
        if (controller.Config.LoadError is { } loadError)
        {
            SetStatus("error", $"Couldn't read the settings file ({loadError}). Quit Home Control and open it again.");
            Changed?.Invoke();
            return;
        }
        if (!controller.IsConfigured)
        {
            SetStatus("setup", "Not set up yet. Open Settings to connect your light.");
            Changed?.Invoke();
            return;
        }
        try
        {
            var st = await Task.Run(() => controller.GetStateAsync());
            if (pumping || pending != null) return; // a newer change is on its way
            Confirmed = Shown = st;
            SetStatus("ok", ViaText(st.Via));
        }
        catch (Exception e)
        {
            SetStatus("error", LightController.Friendly(e));
        }
        Changed?.Invoke();
    }

    void OnReported(LightState st)
    {
        if (MusicRunning || pumping || pending != null) return;
        Confirmed = Shown = st;
        SetStatus("ok", ViaText("local"));
        Changed?.Invoke();
    }

    public void Submit(LightCommand cmd)
    {
        if (MusicRunning) StopMusic(restore: false); // the user is taking over
        Shown = (Shown ?? Confirmed ?? new LightState()).Apply(cmd);
        pending = cmd;
        Changed?.Invoke();
        if (!pumping) _ = PumpAsync();
    }

    /// <summary>Shows a state without sending it to the light (for the design screenshots). null puts back the real one.</summary>
    internal void Preview(LightState? state)
    {
        Shown = state ?? Confirmed;
        Changed?.Invoke();
    }

    public void TogglePower() => Submit(LightCommand.Power(!(Shown?.On ?? false)));

    async Task PumpAsync()
    {
        pumping = true;
        try
        {
            while (pending is { } cmd)
            {
                pending = null;
                try
                {
                    string via = await Task.Run(() => controller.ExecuteAsync(cmd));
                    Confirmed = (Confirmed ?? Shown ?? new LightState()).Apply(cmd) with { Via = via };
                    SetStatus("ok", ViaText(via));
                }
                catch (Exception e)
                {
                    SetStatus("error", LightController.Friendly(e));
                    if (pending == null) Shown = Confirmed; // show what the light really is
                }
                Changed?.Invoke();
            }
        }
        finally
        {
            pumping = false;
            Changed?.Invoke();
        }
    }

    // ------------------------------------------------------------ music sync

    public void StartMusic()
    {
        if (music.Running) return;
        beforeMusic = Confirmed;
        restoreAfterMusic = true;
        MusicKind = "busy";
        MusicText = "Starting…";
        music.Start();
        MusicChanged?.Invoke();
    }

    public void StopMusic(bool restore = true)
    {
        if (!music.Running) return;
        restoreAfterMusic = restore;
        music.Stop(wait: false);
    }

    public void ToggleMusic()
    {
        if (MusicRunning) StopMusic();
        else StartMusic();
    }

    void OnMusicStatus(string kind, string text)
    {
        MusicKind = kind;
        MusicText = text;
        if (kind == "stopped")
        {
            if (restoreAfterMusic && beforeMusic is { } before && music.Streamed) _ = RestoreAsync(before);
            else _ = RefreshAsync();
            beforeMusic = null;
        }
        MusicChanged?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Puts the light back the way it was before music sync.</summary>
    public async Task RestoreAsync(LightState before)
    {
        try
        {
            await Task.Run(async () =>
            {
                await controller.ExecuteAsync(before.IsColour
                    ? LightCommand.Colour(before.H, before.S, before.V)
                    : LightCommand.White(before.Brightness, before.Temp));
                if (!before.On) await controller.ExecuteAsync(LightCommand.Power(false));
            });
        }
        catch (Exception e)
        {
            SetStatus("error", LightController.Friendly(e));
        }
        await RefreshAsync();
    }

    /// <summary>On quit: stop music and restore the light, waiting at most a few seconds.</summary>
    public void Shutdown()
    {
        poll?.Stop();
        if (!music.Running) return;
        var before = beforeMusic;
        bool streamed = music.Streamed;
        restoreAfterMusic = false;
        music.Stop(wait: true);
        if (before == null || !streamed) return;
        var restore = Task.Run(async () =>
        {
            await controller.ExecuteAsync(before.IsColour
                ? LightCommand.Colour(before.H, before.S, before.V)
                : LightCommand.White(before.Brightness, before.Temp));
            if (!before.On) await controller.ExecuteAsync(LightCommand.Power(false));
        });
        try { restore.Wait(4000); } catch { }
    }
}
