using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LedControl.Core;

public enum CommandKind { Power, White, Colour }

/// <summary>A change to make. Brightness/Temp/S/V are 0-100, H is 0-360.</summary>
public sealed record LightCommand(CommandKind Kind, bool On = true, int Brightness = 100, int Temp = 50,
                                  int H = 0, int S = 100, int V = 100)
{
    public static LightCommand Power(bool on) => new(CommandKind.Power, On: on);
    public static LightCommand White(int brightness, int temp) => new(CommandKind.White, Brightness: brightness, Temp: temp);
    public static LightCommand Colour(int h, int s, int v) => new(CommandKind.Colour, H: h, S: s, V: v);
}

public sealed record LightState
{
    public bool On { get; init; }
    public string Mode { get; init; } = "white"; // white | colour | scene | music
    public int Brightness { get; init; } = 100;
    public int Temp { get; init; } = 50;
    public int H { get; init; }
    public int S { get; init; } = 100;
    public int V { get; init; } = 100;
    public bool HasColour { get; init; } = true;
    public bool HasTemp { get; init; } = true;
    public string Via { get; init; } = "";

    public bool IsColour => Mode == "colour";

    // ---- Tuya "type B" data points (this light): 20 switch, 21 mode, 22 brightness 10-1000,
    //      23 colour temp 0-1000, 24 colour "hhhhssssvvvv" (h 0-360, s/v 0-1000), 28 music.
    public static LightState FromLocal(IReadOnlyDictionary<string, JsonElement> dps)
    {
        var st = new LightState
        {
            On = dps.TryGetValue("20", out var on) && on.ValueKind == JsonValueKind.True,
            Mode = dps.TryGetValue("21", out var mode) && mode.ValueKind == JsonValueKind.String ? mode.GetString()! : "white",
            HasColour = dps.ContainsKey("24"),
            HasTemp = dps.ContainsKey("23"),
        };
        if (dps.TryGetValue("22", out var b) && b.TryGetInt32(out var bi))
            st = st with { Brightness = Math.Clamp((int)Math.Round(bi / 10.0), 1, 100) };
        if (dps.TryGetValue("23", out var t) && t.TryGetInt32(out var ti))
            st = st with { Temp = Math.Clamp((int)Math.Round(ti / 10.0), 0, 100) };
        if (dps.TryGetValue("24", out var c) && c.ValueKind == JsonValueKind.String &&
            c.GetString() is { Length: 12 } hex &&
            int.TryParse(hex[..4], NumberStyles.HexNumber, null, out var h) &&
            int.TryParse(hex[4..8], NumberStyles.HexNumber, null, out var s) &&
            int.TryParse(hex[8..], NumberStyles.HexNumber, null, out var v))
            st = st with { H = Math.Clamp(h, 0, 360), S = Math.Clamp((int)Math.Round(s / 10.0), 0, 100),
                           V = Math.Clamp((int)Math.Round(v / 10.0), 1, 100) };
        return st;
    }

    public static Dictionary<string, object> ToLocalDps(LightCommand c, bool hasTemp = true) => c.Kind switch
    {
        CommandKind.Power => new() { ["20"] = c.On },
        CommandKind.White => hasTemp
            ? new() { ["20"] = true, ["21"] = "white", ["22"] = Math.Clamp(c.Brightness, 1, 100) * 10,
                      ["23"] = Math.Clamp(c.Temp, 0, 100) * 10 }
            : new() { ["20"] = true, ["21"] = "white", ["22"] = Math.Clamp(c.Brightness, 1, 100) * 10 },
        _ => new() { ["20"] = true, ["21"] = "colour", ["24"] = HsvHex(c.H, c.S, c.V) },
    };

    /// <summary>"hhhhssssvvvv" with h 0-360, s and v 0-1000.</summary>
    public static string HsvHex(double h, double s, double v) =>
        $"{(int)Math.Clamp(h, 0, 360):x4}{(int)(Math.Clamp(s, 0, 100) * 10):x4}{(int)(Math.Clamp(v, 1, 100) * 10):x4}";

    // ---- Tuya Cloud standard instruction codes
    static string? Pick(IReadOnlyDictionary<string, JsonNode?> codes, params string[] names) =>
        names.FirstOrDefault(codes.ContainsKey);

    public static LightState FromCloud(IReadOnlyDictionary<string, JsonNode?> codes)
    {
        string? sw = Pick(codes, "switch_led", "switch_1", "switch");
        string? bright = Pick(codes, "bright_value_v2", "bright_value");
        string? temp = Pick(codes, "temp_value_v2", "temp_value");
        string? col = Pick(codes, "colour_data_v2", "colour_data");
        double bmax = bright?.EndsWith("_v2") == true ? 1000 : 255;
        var st = new LightState
        {
            On = sw != null && codes[sw] is JsonValue sv && sv.TryGetValue<bool>(out var on) && on,
            Mode = codes.TryGetValue("work_mode", out var m) && m is JsonValue mv && mv.TryGetValue<string>(out var ms) ? ms : "white",
            HasColour = col != null,
            HasTemp = temp != null,
        };
        if (bright != null && Num(codes[bright]) is { } bv)
            st = st with { Brightness = Math.Clamp((int)Math.Round(bv / bmax * 100), 1, 100) };
        if (temp != null && Num(codes[temp]) is { } tv)
            st = st with { Temp = Math.Clamp((int)Math.Round(tv / bmax * 100), 0, 100) };
        if (col != null)
        {
            JsonNode? c = codes[col];
            if (c is JsonValue cs && cs.TryGetValue<string>(out var text))
            {
                try { c = JsonNode.Parse(text); } catch (JsonException) { c = null; }
            }
            double cmax = col.EndsWith("_v2") ? 1000 : 255;
            if (c is JsonObject o)
                st = st with
                {
                    H = (int)(Num(o["h"]) ?? 0),
                    S = (int)Math.Round((Num(o["s"]) ?? cmax) / cmax * 100),
                    V = Math.Max(1, (int)Math.Round((Num(o["v"]) ?? cmax) / cmax * 100)),
                };
        }
        return st;
    }

    static double? Num(JsonNode? n) =>
        n is JsonValue v ? (v.TryGetValue<double>(out var d) ? d : v.TryGetValue<int>(out var i) ? i : null) : null;

    public static List<(string, JsonNode)> ToCloudCommands(LightCommand c, IReadOnlyDictionary<string, JsonNode?> codes)
    {
        string sw = Pick(codes, "switch_led", "switch_1", "switch") ?? "switch_led";
        switch (c.Kind)
        {
            case CommandKind.Power:
                return [(sw, JsonValue.Create(c.On))];
            case CommandKind.White:
            {
                string bright = Pick(codes, "bright_value_v2", "bright_value") ?? "bright_value_v2";
                string? temp = Pick(codes, "temp_value_v2", "temp_value");
                (int lo, int hi) = bright.EndsWith("_v2") ? (10, 1000) : (25, 255);
                var list = new List<(string, JsonNode)>
                {
                    (sw, JsonValue.Create(true)), ("work_mode", JsonValue.Create("white")),
                    (bright, JsonValue.Create((int)(lo + (hi - lo) * Math.Clamp(c.Brightness, 1, 100) / 100.0))),
                };
                if (temp != null)
                    list.Add((temp, JsonValue.Create((int)((temp.EndsWith("_v2") ? 1000 : 255) * Math.Clamp(c.Temp, 0, 100) / 100.0))));
                return list;
            }
            default:
            {
                string col = Pick(codes, "colour_data_v2", "colour_data") ?? "colour_data_v2";
                double cmax = col.EndsWith("_v2") ? 1000 : 255;
                var value = new JsonObject
                {
                    ["h"] = Math.Clamp(c.H, 0, 360),
                    ["s"] = (int)(cmax * Math.Clamp(c.S, 0, 100) / 100),
                    ["v"] = (int)(cmax * Math.Clamp(c.V, 1, 100) / 100),
                };
                return [(sw, JsonValue.Create(true)), ("work_mode", JsonValue.Create("colour")), (col, value)];
            }
        }
    }

    /// <summary>What the light will look like after a command (shown immediately, confirmed later).</summary>
    public LightState Apply(LightCommand c) => c.Kind switch
    {
        CommandKind.Power => this with { On = c.On },
        CommandKind.White => this with { On = true, Mode = "white", Brightness = c.Brightness, Temp = c.Temp },
        _ => this with { On = true, Mode = "colour", H = c.H, S = c.S, V = c.V },
    };

    /// <summary>Approximate on-screen colour of the light (r, g, b 0-255) at full intensity.</summary>
    public (byte R, byte G, byte B) Rgb
    {
        get
        {
            if (IsColour) return ColorMath.HsvToRgb(H, S / 100.0, 1.0);
            // Warm (0) to cool (100) white.
            double t = Temp / 100.0;
            return ((byte)(255 - 30 * t), (byte)(176 + 66 * t), (byte)(100 + 155 * t));
        }
    }

    /// <summary>0-1 intensity, for the preview glow.</summary>
    public double Level => !On ? 0 : (IsColour ? V : Brightness) / 100.0;

    public string Describe()
    {
        if (!On) return "Off";
        return Mode switch
        {
            "colour" => $"{ColorMath.HueName(H, S)} · {V}%",
            "music" => "Music mode",
            "scene" => "Scene",
            _ => $"{(Temp < 25 ? "Warm white" : Temp > 75 ? "Cool white" : "White")} · {Brightness}%",
        };
    }
}

public static class ColorMath
{
    /// <summary>h 0-360, s and v 0-1.</summary>
    public static (byte R, byte G, byte B) HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360 / 60.0;
        int i = (int)Math.Floor(h);
        double f = h - i, p = v * (1 - s), q = v * (1 - s * f), t = v * (1 - s * (1 - f));
        (double r, double g, double b) = i switch
        {
            0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t), 3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q),
        };
        return ((byte)Math.Round(r * 255), (byte)Math.Round(g * 255), (byte)Math.Round(b * 255));
    }

    /// <summary>Returns h 0-360, s and v 0-1.</summary>
    public static (double H, double S, double V) RgbToHsv(byte r8, byte g8, byte b8)
    {
        double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r) h = 60 * (((g - b) / d) % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;
        return (h, max == 0 ? 0 : d / max, max);
    }

    public static string HueName(int h, int s)
    {
        if (s < 15) return "White";
        string[] names = ["Red", "Orange", "Yellow", "Lime", "Green", "Teal", "Cyan", "Sky blue", "Blue", "Violet", "Purple", "Pink", "Red"];
        int[] edges = [10, 40, 65, 85, 150, 170, 190, 210, 245, 270, 295, 335, 361];
        for (int i = 0; i < edges.Length; i++)
            if (h < edges[i]) return names[i];
        return "Red";
    }
}
