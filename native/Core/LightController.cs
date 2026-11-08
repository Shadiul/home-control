using System.Text.Json;
using System.Text.Json.Nodes;

namespace HomeControl.Core;

/// <summary>
/// Talks to the light over the home network (persistent encrypted link) or Tuya Cloud,
/// following the "connection" setting. In automatic mode the home network is tried first;
/// if it fails, the cloud is used and the home network is retried after a minute.
/// </summary>
public sealed class LightController : IDisposable
{
    const int LocalRetryMs = 60_000;

    public AppConfig Config { get; }

    readonly SemaphoreSlim gate = new(1, 1);
    readonly object dpsLock = new();
    readonly Dictionary<string, JsonElement> dps = new();
    Dictionary<string, JsonNode?> cloudCodes = new();
    TuyaLink? link;
    TuyaCloud? cloud;
    long localDownUntil;

    /// <summary>Live state reported by the light itself (raised on a background thread).</summary>
    public event Action<LightState>? Reported;

    public LightController(AppConfig config) => Config = config;

    public bool IsConfigured =>
        Config.Device.Key.Length > 0 || (Config.Cloud.ApiKey.Length > 0 && Config.Cloud.ApiSecret.Length > 0);

    /// <summary>Forget connections after settings change.</summary>
    public void Reset()
    {
        var l = Interlocked.Exchange(ref link, null);
        l?.Dispose();
        cloud = null;
        cloudCodes = new();
        localDownUntil = 0;
        lock (dpsLock) dps.Clear();
    }

    // ------------------------------------------------------------ connections

    async Task<TuyaLink> LinkAsync(CancellationToken ct)
    {
        if (link is { IsOpen: true } open) return open;
        var d = Config.Device;
        if (string.IsNullOrWhiteSpace(d.Key))
            throw new LedException("Home network control needs the light's local key (see Settings).");
        var l = new TuyaLink(d.Id, d.Ip, d.Key);
        l.Reported += OnReported;
        await l.OpenAsync(ct);
        var old = Interlocked.Exchange(ref link, l);
        old?.Dispose();
        return l;
    }

    void OnReported(Dictionary<string, JsonElement> update)
    {
        LightState st;
        lock (dpsLock)
        {
            foreach (var kv in update) dps[kv.Key] = kv.Value;
            if (!dps.ContainsKey("20")) return;
            st = LightState.FromLocal(dps) with { Via = "local" };
        }
        Reported?.Invoke(st);
    }

    TuyaCloud Cloud() => cloud ??= new TuyaCloud(Config.Cloud);

    async Task<(T Result, string Via)> RunAsync<T>(Func<TuyaLink, Task<T>> localOp,
                                                   Func<TuyaCloud, Task<T>> cloudOp, CancellationToken ct)
    {
        string mode = Config.Connection;
        string[] order = mode switch { "local" => ["local"], "cloud" => ["cloud"], _ => ["local", "cloud"] };
        var errors = new List<string>();
        foreach (var route in order)
        {
            if (route == "local" && mode == "auto" &&
                (Environment.TickCount64 < localDownUntil || string.IsNullOrWhiteSpace(Config.Device.Key)))
                continue;
            try
            {
                if (route == "local")
                {
                    bool reused = link is { IsOpen: true };
                    T r;
                    try
                    {
                        r = await localOp(await LinkAsync(ct));
                    }
                    catch (LedException) when (reused)
                    {
                        // The kept-open link had gone stale (light restarted, Wi-Fi blip): reconnect once.
                        Interlocked.Exchange(ref link, null)?.Dispose();
                        r = await localOp(await LinkAsync(ct));
                    }
                    localDownUntil = 0;
                    return (r, "local");
                }
                return (await cloudOp(Cloud()), "cloud");
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                errors.Add((route == "local" ? "Home network: " : "Tuya Cloud: ") + Friendly(e));
                if (route == "local")
                {
                    localDownUntil = Environment.TickCount64 + LocalRetryMs;
                    Interlocked.Exchange(ref link, null)?.Dispose();
                }
                else
                {
                    cloud = null;
                }
            }
        }
        if (errors.Count == 0)
            errors.Add("Not set up yet. Add the local key or your Tuya Cloud details in Settings.");
        throw new LedException(string.Join("\n", errors));
    }

    public static string Friendly(Exception e) => e switch
    {
        LedException => e.Message,
        System.Security.Cryptography.CryptographicException => "The light's reply couldn't be decrypted (wrong local key?).",
        _ => e.Message,
    };

    // ------------------------------------------------------------ actions

    public async Task<LightState> GetStateAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var (st, via) = await RunAsync(
                async l =>
                {
                    var f = await l.QueryAsync(ct);
                    var reply = TuyaProtocol.ParseDps(f.Payload) ?? throw new LedException("The light sent an empty status.");
                    lock (dpsLock)
                    {
                        foreach (var kv in reply) dps[kv.Key] = kv.Value;
                        return LightState.FromLocal(dps);
                    }
                },
                async c =>
                {
                    cloudCodes = await c.StatusAsync(Config.Device.Id, ct);
                    return LightState.FromCloud(cloudCodes);
                }, ct);
            return st with { Via = via };
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Runs a command; returns which route was used ("local" or "cloud").</summary>
    public async Task<string> ExecuteAsync(LightCommand cmd, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var (_, via) = await RunAsync(
                async l =>
                {
                    bool hasTemp;
                    lock (dpsLock) hasTemp = dps.Count == 0 || dps.ContainsKey("23");
                    await l.SetAsync(LightState.ToLocalDps(cmd, hasTemp), ct);
                    return true;
                },
                async c =>
                {
                    if (cloudCodes.Count == 0) cloudCodes = await c.StatusAsync(Config.Device.Id, ct);
                    await c.SendAsync(Config.Device.Id, LightState.ToCloudCommands(cmd, cloudCodes), ct);
                    return true;
                }, ct);
            return via;
        }
        finally
        {
            gate.Release();
        }
    }

    // ------------------------------------------------------------ music streaming

    /// <summary>Opens (or reuses) the home-network link and switches the light to music mode.</summary>
    public async Task MusicBeginAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Config.Device.Key))
            throw new LedException("Music sync needs the light's local key (see Settings).");
        await gate.WaitAsync(ct);
        try
        {
            var l = await LinkAsync(ct);
            await l.SetAsync(new Dictionary<string, object> { ["20"] = true, ["21"] = "music" }, ct);
            localDownUntil = 0;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Fire-and-forget music colour (h 0-360, s and v 0-1). Throws if the link dropped.</summary>
    public void MusicSend(double h, double s, double v)
    {
        var l = link;
        if (l is not { IsOpen: true }) throw new LedException("Lost the connection to the light.");
        // DP 28: transition (0 = jump) + hhhhssssvvvv + white brightness/temp (unused) as 0000 0000.
        string value = "0" + LightState.HsvHex(((h % 360) + 360) % 360, s * 100, Math.Max(0.01, v) * 100) + "00000000";
        l.SetNoWait(new Dictionary<string, object> { ["28"] = value });
    }

    // ------------------------------------------------------------ setup helpers

    public async Task<string> FetchKeyFromCloudAsync(CancellationToken ct = default)
    {
        var info = await new TuyaCloud(Config.Cloud).DeviceInfoAsync(Config.Device.Id, ct);
        if (string.IsNullOrEmpty(info.LocalKey))
            throw new LedException("Tuya Cloud didn't return a local key for this light.");
        Config.Device.Key = info.LocalKey;
        if (!string.IsNullOrEmpty(info.Name)) Config.Device.Name = info.Name;
        Config.Save();
        Reset();
        return info.Name;
    }

    public sealed record CheckStep(string Title, bool? Ok, string Detail);

    /// <summary>Connection check: discovery broadcast, TCP reachability, home network and cloud routes.</summary>
    public async Task<List<CheckStep>> DiagnoseAsync(CancellationToken ct = default)
    {
        var d = Config.Device;
        var steps = new List<CheckStep>();

        var found = await Discovery.FindAsync(d.Id, 7000, ct);
        if (found != null && found.Ip != d.Ip)
        {
            string old = d.Ip;
            d.Ip = found.Ip;
            if (!string.IsNullOrEmpty(found.Version)) d.Version = found.Version;
            Config.Save();
            Reset();
            steps.Add(new("Found the light on your network", true, $"Its address changed from {old} to {found.Ip}; updated."));
        }
        else if (found != null)
            steps.Add(new("Found the light on your network", true, $"It announces itself at {found.Ip}."));
        else
            steps.Add(new("Light's network announcements", null, "Not heard (normal on some routers)."));

        bool tcp = await TuyaLink.ReachableAsync(d.Ip);
        steps.Add(new("Reach the light at " + d.Ip, tcp,
                      tcp ? "The light accepts connections." : "No answer. It may be off, or its IP changed."));

        if (d.Key.Length > 0 && tcp)
        {
            try
            {
                await gate.WaitAsync(ct);
                try
                {
                    var l = await LinkAsync(ct);
                    await l.QueryAsync(ct);
                }
                finally
                {
                    gate.Release();
                }
                localDownUntil = 0;
                steps.Add(new("Home network control", true, "Working: encrypted connection and status read."));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Interlocked.Exchange(ref link, null)?.Dispose();
                steps.Add(new("Home network control", false, Friendly(e)));
            }
        }
        else if (d.Key.Length == 0)
            steps.Add(new("Home network control", false, "No local key yet. Use Tuya Cloud → Get local key."));

        var c = Config.Cloud;
        if (c.ApiKey.Length > 0 && c.ApiSecret.Length > 0)
        {
            try
            {
                await new TuyaCloud(c).StatusAsync(d.Id, ct);
                steps.Add(new("Tuya Cloud control", true, "Working."));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                steps.Add(new("Tuya Cloud control", false, Friendly(e)));
            }
        }
        else
            steps.Add(new("Tuya Cloud control", null, "Not set up."));
        return steps;
    }

    public void Dispose() => Reset();
}
