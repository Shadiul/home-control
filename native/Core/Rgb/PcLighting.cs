using System.Diagnostics;

namespace LedControl.Core.Rgb;

/// <summary>The PC's own lighting as one device: on/off, one colour and a brightness, plus fast colour frames for music sync.</summary>
public sealed record PcLightingState(bool On, byte R, byte G, byte B, int Brightness)
{
    public static readonly PcLightingState Default = new(true, 255, 196, 126, 80);
    public (byte R, byte G, byte B) Output => On
        ? ((byte)(R * Brightness / 100), (byte)(G * Brightness / 100), (byte)(B * Brightness / 100))
        : ((byte)0, (byte)0, (byte)0);
}

/// <summary>What the app found for one controller.</summary>
public sealed record PcLightingDevice(string Kind, string Name, string Detail, bool Ok, string Error = "");

/// <summary>
/// Runs the PC's RGB controllers (NZXT, MSI Mystic Light, Corsair Commander CORE) from one background thread.
/// - Colour changes are coalesced: only the newest colour is sent, at most about 30 times a second.
/// - While the Corsair hub is held in software mode, its cooling is refreshed every second (see <see cref="CorsairCommanderCore"/>).
/// - <see cref="Stop"/> gives the Corsair hub back to hardware mode and puts the MSI board back as it was found.
/// Nothing here blocks the caller; failures are reported through <see cref="Changed"/> and <see cref="Devices"/>.
/// </summary>
public sealed class PcLighting : IDisposable
{
    NzxtSmartDevice? nzxt;
    MsiMysticLight? msi;
    CorsairCommanderCore? corsair;
    Thread? worker;
    volatile bool running;
    readonly AutoResetEvent wake = new(false);
    readonly object gate = new();
    (byte R, byte G, byte B)? pending;
    (byte R, byte G, byte B)? applied;
    long corsairRetryAt, nextConflictCheck;

    public PcLightingState State { get; private set; } = PcLightingState.Default;
    public IReadOnlyList<PcLightingDevice> Devices { get; private set; } = [];
    /// <summary>"off" (not started), "busy", "ok", "error", or "conflict" (another RGB app is running).</summary>
    public string StatusKind { get; private set; } = "off";
    public string StatusText { get; private set; } = "";
    /// <summary>Coolant temperature and pump/fan duty while the Corsair hub is held, for the UI.</summary>
    public string CoolingText { get; private set; } = "";
    public event Action? Changed;

    /// <summary>Lighting apps that also drive these controllers. Two programs on one controller make it flicker.</summary>
    // (SignalRgbService keeps running after SignalRGB is closed and doesn't drive the lights, so only the app counts.)
    static readonly string[] OtherRgbApps = ["SignalRgb", "OpenRGB", "iCUE", "NZXT CAM", "MSI Center", "LightKeeperService"];

    public static string? ConflictingApp()
    {
        foreach (var name in OtherRgbApps)
        {
            var p = Process.GetProcessesByName(name);
            bool any = p.Length > 0;
            foreach (var x in p) x.Dispose();
            if (any) return name;
        }
        return null;
    }

    public void Start(PcLightingState state)
    {
        if (running) return;
        worker?.Join(3000); // a previous Stop may still be handing the controllers back
        State = state;
        running = true;
        SetStatus("busy", "Looking for the PC's lighting controllers…");
        worker = new Thread(Run) { IsBackground = true, Name = "PC lighting" };
        worker.Start();
    }

    /// <summary>Sets on/off, colour and brightness. Returns at once; the worker sends it.</summary>
    public void Set(PcLightingState state)
    {
        State = state;
        Push(state.Output);
        Changed?.Invoke();
    }

    /// <summary>A colour frame from music sync (already at the brightness to show).</summary>
    public void Frame(byte r, byte g, byte b) => Push((r, g, b));

    /// <summary>Goes back to the steady colour after music sync.</summary>
    public void EndFrames() => Push(State.Output);

    void Push((byte, byte, byte) c)
    {
        lock (gate) pending = c;
        wake.Set();
    }

    void Run()
    {
        try
        {
            Open();
            Push(State.Output);
            long nextTick = 0, nextReopen = Environment.TickCount64 + 30_000;
            while (running)
            {
                wake.WaitOne(250);
                if (!running) break;
                (byte R, byte G, byte B)? c;
                lock (gate) { c = pending; pending = null; }
                if (c == null && applied == null) c = State.Output;
                if (c is { } col && col != applied) Apply(col);
                if (Environment.TickCount64 >= nextTick)
                {
                    nextTick = Environment.TickCount64 + 1000;
                    TickCorsair();
                }
                if (Environment.TickCount64 >= nextReopen)
                {
                    nextReopen = Environment.TickCount64 + 30_000;
                    ReopenMissing();
                }
                Thread.Sleep(30); // at most about 30 updates a second
            }
        }
        catch (Exception e)
        {
            SetStatus("error", "PC lighting stopped: " + e.Message);
        }
        finally
        {
            Close();
        }
    }

    void Open()
    {
        var conflict = ConflictingApp();
        var list = new List<PcLightingDevice>();

        try
        {
            if (NzxtSmartDevice.Find() is { } i)
            {
                nzxt = NzxtSmartDevice.Open(i);
                string parts = string.Join(" · ", nzxt.Channels.Select((c, n) => c.Count == 0 ? $"channel {n + 1} empty" : string.Join(", ", c.Select(a => a.Name))));
                list.Add(new("nzxt", "NZXT controller", parts, true));
            }
        }
        catch (Exception e) { list.Add(new("nzxt", "NZXT controller", "", false, e.Message)); nzxt = null; }

        try
        {
            if (MsiMysticLight.Find() is { } i)
            {
                msi = MsiMysticLight.Open(i);
                list.Add(new("msi", "Motherboard (MSI Mystic Light)", "RGB and ARGB headers", true));
            }
        }
        catch (Exception e) { list.Add(new("msi", "Motherboard (MSI Mystic Light)", "", false, e.Message)); msi = null; }

        try
        {
            if (CorsairCommanderCore.Find() is { } i)
            {
                corsair = CorsairCommanderCore.Open(i);
                list.Add(new("corsair", "Corsair Commander CORE", "Cooler pump and fans", true));
            }
        }
        catch (Exception e) { list.Add(new("corsair", "Corsair Commander CORE", "", false, e.Message)); corsair = null; }

        Devices = list;
        if (conflict != null)
            SetStatus("conflict", $"{conflict} is running and also controls these lights. Close it so they don't flicker.");
        else if (list.Count == 0)
            SetStatus("error", "No supported lighting controllers found.");
        else
            SetStatus(list.All(d => d.Ok) ? "ok" : "error", $"{list.Count(d => d.Ok)} of {list.Count} controllers ready");
    }

    void Apply((byte R, byte G, byte B) c)
    {
        if (StatusKind == "conflict" && Environment.TickCount64 >= nextConflictCheck)
        {
            nextConflictCheck = Environment.TickCount64 + 5000;
            if (ConflictingApp() == null) SetStatus(Devices.All(d => d.Ok) ? "ok" : "error", $"{Devices.Count(d => d.Ok)} of {Devices.Count} controllers ready");
        }
        try { nzxt?.SetAll(c.R, c.G, c.B); }
        catch (Exception e) { Fail("nzxt", e); nzxt?.Dispose(); nzxt = null; }
        try { msi?.SetAll(c.R, c.G, c.B); }
        catch (Exception e) { Fail("msi", e); msi?.Dispose(); msi = null; }
        if (corsair != null && Environment.TickCount64 >= corsairRetryAt)
        {
            try
            {
                if (!corsair.InSoftwareMode) corsair.Acquire();
                corsair.SetAll(c.R, c.G, c.B);
            }
            catch (Exception e)
            {
                // Acquire/SetAll already put the hub back in hardware mode; reopen it and try again in a minute.
                Fail("corsair", e);
                DropCorsair();
            }
        }
        applied = c;
    }

    void DropCorsair()
    {
        try { corsair?.Dispose(); } catch { }
        corsair = null;
        corsairRetryAt = Environment.TickCount64 + 60_000;
        applied = null; // send the colour again once it's back
    }

    /// <summary>
    /// Reopens controllers that failed or disappeared (after sleep, or a USB reset). Called every 30 seconds.
    /// </summary>
    void ReopenMissing()
    {
        bool reopened = false;
        try
        {
            if (nzxt == null && Devices.Any(d => d.Kind == "nzxt") && NzxtSmartDevice.Find() is { } n) { nzxt = NzxtSmartDevice.Open(n); reopened = true; }
        }
        catch { nzxt = null; }
        try
        {
            if (msi == null && Devices.Any(d => d.Kind == "msi") && MsiMysticLight.Find() is { } m) { msi = MsiMysticLight.Open(m); reopened = true; }
        }
        catch { msi = null; }
        try
        {
            if (corsair == null && Environment.TickCount64 >= corsairRetryAt && Devices.Any(d => d.Kind == "corsair") && CorsairCommanderCore.Find() is { } c)
            { corsair = CorsairCommanderCore.Open(c); reopened = true; }
        }
        catch { corsair = null; }
        if (!reopened) return;
        Devices = Devices.Select(d => d with
        {
            Ok = d.Kind switch { "nzxt" => nzxt != null, "msi" => msi != null, _ => corsair != null },
            Error = "",
        }).ToList();
        applied = null;
        lock (gate) pending ??= State.Output;
        SetStatus(Devices.All(d => d.Ok) ? "ok" : "error", $"{Devices.Count(d => d.Ok)} of {Devices.Count} controllers ready");
    }

    void TickCorsair()
    {
        if (corsair is not { InSoftwareMode: true }) { CoolingText = ""; return; }
        try
        {
            corsair.Tick();
            var t = corsair.CoolantTemp;
            CoolingText = $"Coolant {(t is double v ? $"{v:0.0} °C" : "unknown")} · pump {corsair.PumpDuty}% · fans {corsair.FanDutyNow}%";
            Changed?.Invoke();
        }
        catch (Exception e)
        {
            Fail("corsair", e);
            DropCorsair();
        }
    }

    void Fail(string kind, Exception e)
    {
        Devices = Devices.Select(d => d.Kind == kind ? d with { Ok = false, Error = e.Message } : d).ToList();
        SetStatus("error", $"{Devices.First(d => d.Kind == kind).Name}: {e.Message}");
    }

    void SetStatus(string kind, string text)
    {
        StatusKind = kind;
        StatusText = text;
        Changed?.Invoke();
    }

    void Close()
    {
        // The Corsair hub must go back to hardware mode so its own pump and fan curves run again.
        try { corsair?.Dispose(); } catch { }
        try { msi?.Restore(); } catch { }
        try { msi?.Dispose(); } catch { }
        try { nzxt?.Dispose(); } catch { }
        corsair = null; msi = null; nzxt = null;
        CoolingText = "";
    }

    /// <summary>For screenshots: shows sample controllers without touching any hardware.</summary>
    public void Preview(IReadOnlyList<PcLightingDevice> devices, string cooling)
    {
        if (running) return;
        Devices = devices;
        CoolingText = cooling;
        StatusKind = "ok";
        StatusText = $"{devices.Count} of {devices.Count} controllers ready";
        Changed?.Invoke();
    }

    /// <summary>Stops the worker and hands every controller back. Blocks for up to 3 seconds.</summary>
    public void Stop()
    {
        if (!running) return;
        running = false;
        wake.Set();
        worker?.Join(3000);
        applied = null;
        SetStatus("off", "");
    }

    public void Dispose() => Stop();
}
