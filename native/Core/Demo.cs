using System.Text.Json;

namespace HomeControl.Core;

/// <summary>
/// Demo mode (<c>--demo</c>): a sample light and two air conditioners, simulated in memory.
/// Nothing is sent over the network, and the settings file is neither read nor written,
/// so it's safe for trying the app without devices and for making screenshots.
/// </summary>
public static class Demo
{
    /// <summary>Sample settings. Saving them does nothing.</summary>
    public static AppConfig Config() => new()
    {
        IsDemo = true,
        Connection = "local",
        Device = new DeviceConfig
        {
            Id = "demo-light", Ip = "192.168.1.50", Version = "3.5", Key = "demo-local-key00", Name = "Living room light",
        },
        Acs =
        [
            new AcConfig { Name = "Living room", Ip = "192.168.1.60", Mac = "020000000001", Key = "demo-ac-key-0001", Gcm = true },
            new AcConfig { Name = "Bedroom", Ip = "192.168.1.61", Mac = "020000000002", Key = "demo-ac-key-0002", Gcm = true },
        ],
    };

    /// <summary>The sample air conditioners' starting state, by MAC address.</summary>
    internal static Dictionary<string, int> AcStart(string mac) => mac.EndsWith('1')
        ? new() { ["Pow"] = 1, ["Mod"] = AcState.Cool, ["SetTem"] = 24, ["TemSen"] = 66, ["WdSpd"] = 3, ["SwUpDn"] = 4,
                  ["SwingLfRig"] = 1, ["Lig"] = 1, ["Health"] = 1 }
        : new() { ["Pow"] = 0, ["Mod"] = AcState.Cool, ["SetTem"] = 26, ["TemSen"] = 67, ["WdSpd"] = 0, ["SwUpDn"] = 0,
                  ["SwingLfRig"] = 0, ["Lig"] = 1, ["SwhSlp"] = 1 };
}

/// <summary>What an air conditioner page needs from a unit: read its state and change it.</summary>
public interface IAcClient
{
    /// <summary>The unit's key changed (it was re-paired): save the config.</summary>
    event Action? KeyChanged;
    Task<AcState> StatusAsync();
    Task SetAsync(IReadOnlyDictionary<string, int> values);
}

/// <summary>A simulated air conditioner for demo mode.</summary>
public sealed class DemoAcClient(AcConfig ac) : IAcClient
{
    readonly object gate = new();
    readonly Dictionary<string, int> values = Demo.AcStart(ac.Mac);

    public event Action? KeyChanged { add { } remove { } }

    public async Task<AcState> StatusAsync()
    {
        await Task.Delay(80);
        lock (gate) return new AcState(new Dictionary<string, int>(values));
    }

    public async Task SetAsync(IReadOnlyDictionary<string, int> changes)
    {
        await Task.Delay(80);
        lock (gate)
            foreach (var (k, v) in changes) values[k] = v;
    }
}

/// <summary>A simulated Tuya light for demo mode: keeps its data points like the real one.</summary>
public sealed class DemoLight
{
    readonly object gate = new();
    readonly Dictionary<string, JsonElement> dps = new()
    {
        ["20"] = Json(true), ["21"] = Json("white"), ["22"] = Json(700), ["23"] = Json(350),
        ["24"] = Json(LightState.HsvHex(30, 80, 100)),
    };

    public LightState State()
    {
        lock (gate) return LightState.FromLocal(dps);
    }

    public LightState Apply(IReadOnlyDictionary<string, object> values)
    {
        lock (gate)
        {
            foreach (var (k, v) in values) dps[k] = Json(v);
            return LightState.FromLocal(dps);
        }
    }

    static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
}
