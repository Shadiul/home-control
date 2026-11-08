using HomeControl.Core;
using Microsoft.UI.Dispatching;

namespace HomeControl;

/// <summary>
/// UI-thread view of one air conditioner. Changes show at once (Shown) and are sent in the background;
/// quick changes in a row (tapping + several times) are merged into one command.
/// </summary>
public sealed class AcUnit
{
    readonly IAcClient client;
    readonly DispatcherQueue ui;
    readonly Dictionary<string, int> pending = [];
    bool pumping;

    public AcConfig Config { get; }
    public AcState? Confirmed { get; private set; }
    public AcState? Shown { get; private set; }
    /// <summary>ok | busy | error</summary>
    public string StatusKind { get; private set; } = "busy";
    public string StatusText { get; private set; } = "Connecting…";

    public event Action? Changed;
    internal event Action? KeyChanged;

    public AcUnit(AcConfig config, DispatcherQueue ui)
    {
        Config = config;
        this.ui = ui;
        client = AppInfo.Demo ? new DemoAcClient(config) : new GreeClient(config);
        client.KeyChanged += () => ui.TryEnqueue(() => KeyChanged?.Invoke());
    }

    public async Task RefreshAsync()
    {
        if (pumping || pending.Count > 0) return;
        try
        {
            var st = await Task.Run(() => client.StatusAsync());
            if (pumping || pending.Count > 0) return; // a newer change is on its way
            Confirmed = Shown = st;
            StatusKind = "ok";
            StatusText = "Connected · Home network";
        }
        catch (Exception e)
        {
            StatusKind = "error";
            StatusText = LightController.Friendly(e);
        }
        Changed?.Invoke();
    }

    public void Set(params (string Key, int Value)[] changes)
    {
        var d = changes.ToDictionary(c => c.Key, c => c.Value);
        Shown = (Shown ?? Confirmed ?? new AcState(new Dictionary<string, int>())).With(d);
        foreach (var (k, v) in d) pending[k] = v;
        Changed?.Invoke();
        if (!pumping) _ = PumpAsync();
    }

    public void TogglePower() => Set(("Pow", Shown?.On == true ? 0 : 1));

    /// <summary>Screenshot mode only: shows a sample state when the unit can't be reached, to check the design.</summary>
    internal void Preview(AcState state)
    {
        Shown = Confirmed = state;
        StatusKind = "ok";
        StatusText = "Preview";
        Changed?.Invoke();
    }

    async Task PumpAsync()
    {
        pumping = true;
        bool ok = true;
        try
        {
            while (pending.Count > 0)
            {
                var batch = new Dictionary<string, int>(pending);
                pending.Clear();
                try
                {
                    await Task.Run(() => client.SetAsync(batch));
                    Confirmed = (Confirmed ?? Shown)?.With(batch);
                    StatusKind = "ok";
                    StatusText = "Connected · Home network";
                }
                catch (Exception e)
                {
                    ok = false;
                    StatusKind = "error";
                    StatusText = LightController.Friendly(e);
                    if (pending.Count == 0) Shown = Confirmed; // show what the unit really is
                }
                Changed?.Invoke();
            }
        }
        finally
        {
            pumping = false;
        }
        // The unit can adjust related settings itself (turbo off in fan mode, for example): read them back.
        if (ok)
        {
            await Task.Delay(400);
            await RefreshAsync();
        }
    }
}

/// <summary>All paired air conditioners, refreshed every 20 seconds.</summary>
public sealed class AcHub
{
    readonly AppConfig config;
    readonly DispatcherQueue ui;
    DispatcherQueueTimer? poll;

    public List<AcUnit> Units { get; } = [];

    /// <summary>A unit was added, removed or renamed.</summary>
    public event Action? ListChanged;
    /// <summary>Any unit's state changed.</summary>
    public event Action? Changed;

    public AcHub(AppConfig config, DispatcherQueue ui)
    {
        this.config = config;
        this.ui = ui;
        Rebuild();
    }

    public void Start()
    {
        poll = ui.CreateTimer();
        poll.Interval = TimeSpan.FromSeconds(20);
        poll.Tick += (_, _) => RefreshAll();
        poll.Start();
        RefreshAll();
    }

    public void Stop() => poll?.Stop();

    public void RefreshAll()
    {
        foreach (var u in Units) _ = u.RefreshAsync();
    }

    void Rebuild()
    {
        Units.Clear();
        foreach (var ac in config.Acs)
        {
            var unit = new AcUnit(ac, ui);
            unit.Changed += () => Changed?.Invoke();
            unit.KeyChanged += Save;
            Units.Add(unit);
        }
    }

    void Save()
    {
        try
        {
            config.Save();
        }
        catch (Exception e)
        {
            App.Log("Saving AC settings failed: " + e.Message);
        }
    }

    /// <summary>Finds and pairs units on the network. Returns a sentence for the user.</summary>
    public async Task<string> FindAsync()
    {
        if (AppInfo.Demo)
        {
            await Task.Delay(600);
            return "This is the demo, so it doesn't search the network. The sample air conditioners are all there is.";
        }
        var known = config.Acs.Select(a => a.Clone()).ToList();
        var r = await Task.Run(() => Gree.DiscoverAsync(known));
        config.Acs = r.Acs;
        Save();
        Rebuild();
        ListChanged?.Invoke();
        RefreshAll();

        var parts = new List<string>();
        if (r.Added > 0) parts.Add(r.Added == 1 ? "Added 1 air conditioner." : $"Added {r.Added} air conditioners.");
        if (r.Moved > 0) parts.Add(r.Moved == 1 ? "Updated 1 address." : $"Updated {r.Moved} addresses.");
        if (r.Failed.Count > 0) parts.Add($"Couldn't pair with {string.Join(", ", r.Failed)}.");
        if (parts.Count == 0)
            parts.Add(r.Acs.Count == 0
                ? "No Gree air conditioners answered. Check they're on the same Wi-Fi network as this PC."
                : "No new air conditioners. Everything is up to date.");
        return string.Join(" ", parts);
    }

    public void Rename(AcUnit unit, string name)
    {
        unit.Config.Name = name.Trim();
        Save();
        ListChanged?.Invoke();
    }

    public void Remove(AcUnit unit)
    {
        config.Acs.RemoveAll(a => a.Mac == unit.Config.Mac);
        Save();
        Rebuild();
        ListChanged?.Invoke();
        RefreshAll();
    }
}
