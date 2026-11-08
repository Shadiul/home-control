namespace HomeControl.Core;

/// <summary>
/// An air conditioner's settings, as Gree property values:
///   Pow 0/1 · Mod 0 auto, 1 cool, 2 dry, 3 fan, 4 heat · SetTem °C · TemSen room temperature (+40 on most units)
///   WdSpd 0 auto, 1 low … 5 high · Tur turbo · Quiet 0 off, 1-2 on · SwUpDn / SwingLfRig 0 default, 1 swing, 2-6 fixed
///   Lig display light · SwhSlp sleep · Health ionizer · Blo X-Fan (dries the coil after turning off)
/// </summary>
public sealed record AcState(IReadOnlyDictionary<string, int> Values)
{
    public const int MinTemp = 16, MaxTemp = 30;
    public const int Auto = 0, Cool = 1, Dry = 2, Fan = 3, Heat = 4;

    public static readonly string[] ModeNames = ["Auto", "Cool", "Dry", "Fan", "Heat"];
    public static readonly string[] FanNames = ["Auto", "Low", "Medium-low", "Medium", "Medium-high", "High"];

    public int Get(string key, int fallback = 0) => Values.TryGetValue(key, out var v) ? v : fallback;
    public bool Is(string key) => Get(key) != 0;

    public bool On => Get("Pow") == 1;
    public int Mode => Math.Clamp(Get("Mod", Cool), 0, 4);
    public int SetTemp => Math.Clamp(Get("SetTem", 24), MinTemp, MaxTemp);
    public int FanSpeed => Math.Clamp(Get("WdSpd"), 0, 5);
    public bool UsesSetTemp => Mode is Auto or Cool or Heat;

    /// <summary>Room temperature in °C, or null when the unit doesn't report one.</summary>
    public int? RoomTemp
    {
        get
        {
            int t = Get("TemSen");
            if (t <= 0) return null;
            return t >= 40 ? t - 40 : t; // most firmware adds 40 so the value never goes negative
        }
    }

    public AcState With(IReadOnlyDictionary<string, int> changes)
    {
        var d = new Dictionary<string, int>(Values);
        foreach (var (k, v) in changes) d[k] = v;
        return new AcState(d);
    }

    public string Describe()
    {
        if (!On) return "Off";
        return Mode switch
        {
            Cool => $"Cooling to {SetTemp} °C",
            Heat => $"Heating to {SetTemp} °C",
            Dry => "Drying the air",
            Fan => "Fan only",
            _ => $"Auto · {SetTemp} °C",
        };
    }
}
