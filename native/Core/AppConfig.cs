using System.Text.Json;
using System.Text.Json.Nodes;

namespace HomeControl.Core;

public static class AppInfo
{
    public const string Name = "Home Control";
    public const string ExeName = "HomeControl.exe";
    /// <summary>The app's name before version 2.2.</summary>
    public const string OldName = "LED Control";

    public static string Version
    {
        get
        {
            var v = typeof(AppInfo).Assembly.GetName().Version;
            return v == null ? "2.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Name);
    public static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    /// <summary>Moves settings from %APPDATA%\LED Control (the old name) the first time the renamed app runs.</summary>
    public static void MoveOldSettings()
    {
        string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), OldName);
        if (!Directory.Exists(old) || File.Exists(ConfigPath)) return;
        try
        {
            if (!Directory.Exists(ConfigDir))
            {
                Directory.Move(old, ConfigDir);
                return;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // the folder is in use: copy the settings file instead
        }
        try
        {
            Directory.CreateDirectory(ConfigDir);
            File.Copy(Path.Combine(old, "config.json"), ConfigPath);
        }
        catch (Exception)
        {
            // nothing to move
        }
    }
}

public sealed class DeviceConfig
{
    public string Id { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Version { get; set; } = "3.5";
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public DeviceConfig Clone() => (DeviceConfig)MemberwiseClone();
}

public sealed class CloudConfig
{
    public string Region { get; set; } = "eu";
    public string ApiKey { get; set; } = "";
    public string ApiSecret { get; set; } = "";
    public CloudConfig Clone() => (CloudConfig)MemberwiseClone();
}

/// <summary>A paired Gree air conditioner.</summary>
public sealed class AcConfig
{
    public string Name { get; set; } = "";
    public string Ip { get; set; } = "";
    public string Mac { get; set; } = "";
    public string Key { get; set; } = "";
    public bool Gcm { get; set; }
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "Air conditioner" : Name;
    public AcConfig Clone() => (AcConfig)MemberwiseClone();
}

public sealed class MusicPrefs
{
    public static readonly string[] Styles = ["pulse", "spectrum", "beat", "party"];
    public static readonly int[] Speeds = [10, 15, 20, 25];
    public const string SystemSource = "system";

    public string Style { get; set; } = "spectrum";
    public int Sensitivity { get; set; } = 55;
    public int Hue { get; set; } = 280;
    public int Fps { get; set; } = 20;
    public string Source { get; set; } = SystemSource;
}

/// <summary>
/// Settings in %APPDATA%\Home Control\config.json (the same layout earlier versions used under "LED Control").
/// Values are read defensively, and keys this version doesn't know are kept when saving.
/// </summary>
public sealed class AppConfig
{
    public string Connection { get; set; } = "auto"; // auto | local | cloud
    public DeviceConfig Device { get; set; } = new();
    public CloudConfig Cloud { get; set; } = new();
    public string Theme { get; set; } = "auto"; // auto | light | dark
    public bool CloseToTray { get; set; } = true;
    public MusicPrefs Music { get; set; } = new();
    public List<AcConfig> Acs { get; set; } = [];

    JsonObject root = new();
    static readonly object FileGate = new();

    /// <summary>Why the settings file couldn't be read at start-up, or null. Saving is refused while it's set.</summary>
    public string? LoadError { get; private set; }

    /// <summary>The settings came from the file (false on a first run, before anything is saved).</summary>
    public bool LoadedFromFile { get; private set; }

    public static AppConfig Load()
    {
        var cfg = new AppConfig();
        // A file that's briefly busy (antivirus, a sync tool, another copy saving) gets a few more tries.
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (!File.Exists(AppInfo.ConfigPath)) break; // first run: start from defaults
                if (JsonNode.Parse(File.ReadAllText(AppInfo.ConfigPath)) is JsonObject r)
                {
                    cfg.root = r;
                    cfg.LoadedFromFile = true;
                    cfg.LoadError = null;
                }
                else cfg.LoadError = "the file isn't a settings object";
                break;
            }
            catch (System.Text.Json.JsonException e)
            {
                cfg.LoadError = "the file isn't valid JSON: " + e.Message;
                break;
            }
            catch (Exception e)
            {
                cfg.LoadError = $"{e.GetType().Name}: {e.Message}";
                Thread.Sleep(200);
            }
        }
        var r0 = cfg.root;
        cfg.Connection = OneOf(Str(r0, "connection", "auto"), "auto", "local", "cloud");

        var d = r0["device"] as JsonObject;
        cfg.Device.Id = Str(d, "id", cfg.Device.Id);
        cfg.Device.Ip = Str(d, "ip", cfg.Device.Ip);
        cfg.Device.Version = Str(d, "version", cfg.Device.Version);
        cfg.Device.Key = Str(d, "key", "");
        cfg.Device.Name = Str(d, "name", "");

        var c = r0["cloud"] as JsonObject;
        cfg.Cloud.Region = Str(c, "region", "eu");
        cfg.Cloud.ApiKey = Str(c, "api_key", "");
        cfg.Cloud.ApiSecret = Str(c, "api_secret", "");

        var a = r0["app"] as JsonObject;
        cfg.Theme = OneOf(Str(a, "theme", "auto"), "auto", "light", "dark");
        cfg.CloseToTray = Bool(a, "close_to_tray", true);

        var m = a?["music"] as JsonObject;
        cfg.Music.Style = OneOf(Str(m, "style", "spectrum"), "spectrum", MusicPrefs.Styles);
        cfg.Music.Sensitivity = Math.Clamp(Int(m, "sensitivity", 55), 0, 100);
        cfg.Music.Hue = ((Int(m, "hue", 280) % 360) + 360) % 360;
        int fps = Int(m, "fps", 20);
        cfg.Music.Fps = MusicPrefs.Speeds.Contains(fps) ? fps : 20;
        cfg.Music.Source = Str(m, "source", MusicPrefs.SystemSource);

        if (r0["acs"] is JsonArray acs)
            foreach (var item in acs.OfType<JsonObject>())
            {
                var ac = new AcConfig
                {
                    Name = Str(item, "name", ""), Ip = Str(item, "ip", ""), Mac = Str(item, "mac", ""),
                    Key = Str(item, "key", ""), Gcm = Str(item, "enc", "ecb") == "gcm",
                };
                if (ac.Mac.Length > 0 && ac.Ip.Length > 0 && cfg.Acs.All(x => x.Mac != ac.Mac)) cfg.Acs.Add(ac);
            }
        return cfg;
    }

    public void Save()
    {
        // Never replace a settings file this copy couldn't read: it would lose the keys in it.
        if (LoadError != null)
            throw new InvalidOperationException(
                $"Not saved: the settings file couldn't be read when Home Control started ({LoadError}). Quit Home Control and open it again.");
        if (!LoadedFromFile && File.Exists(AppInfo.ConfigPath))
            throw new InvalidOperationException(
                "Not saved: a settings file appeared after Home Control started without one. Quit Home Control and open it again.");

        root["connection"] = Connection;
        var d = Section(root, "device");
        d["id"] = Device.Id; d["ip"] = Device.Ip; d["version"] = Device.Version;
        d["key"] = Device.Key; d["name"] = Device.Name;
        var c = Section(root, "cloud");
        c["region"] = Cloud.Region; c["api_key"] = Cloud.ApiKey; c["api_secret"] = Cloud.ApiSecret;
        var a = Section(root, "app");
        a["theme"] = Theme; a["close_to_tray"] = CloseToTray;
        var m = Section(a, "music");
        m["style"] = Music.Style; m["sensitivity"] = Music.Sensitivity; m["hue"] = Music.Hue;
        m["fps"] = Music.Fps; m["source"] = Music.Source;
        var acs = new JsonArray();
        foreach (var ac in Acs)
            acs.Add(new JsonObject
            {
                ["name"] = ac.Name, ["ip"] = ac.Ip, ["mac"] = ac.Mac, ["key"] = ac.Key, ["enc"] = ac.Gcm ? "gcm" : "ecb",
            });
        root["acs"] = acs;

        lock (FileGate)
        {
            Directory.CreateDirectory(AppInfo.ConfigDir);
            var tmp = AppInfo.ConfigPath + ".tmp";
            File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, AppInfo.ConfigPath, overwrite: true); // atomic replace: never half a file
        }
        LoadedFromFile = true;
    }

    static JsonObject Section(JsonObject parent, string name)
    {
        if (parent[name] is JsonObject o) return o;
        var created = new JsonObject();
        parent[name] = created;
        return created;
    }

    static string Str(JsonObject? o, string k, string def) =>
        o?[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : def;

    static int Int(JsonObject? o, string k, int def)
    {
        if (o?[k] is not JsonValue v) return def;
        if (v.TryGetValue<int>(out var i)) return i;
        if (v.TryGetValue<double>(out var f)) return (int)Math.Round(f);
        return def;
    }

    static bool Bool(JsonObject? o, string k, bool def) =>
        o?[k] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : def;

    static string OneOf(string value, string def, params string[] allowed) =>
        allowed.Contains(value) ? value : def;
}
