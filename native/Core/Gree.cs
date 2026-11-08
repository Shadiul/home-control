using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HomeControl.Core;

/// <summary>
/// Gree air conditioner local protocol (JSON over UDP port 7000), as used by the Gree+ / EWPE Smart apps.
/// Messages carry an encrypted "pack": AES-128-ECB on older modules, AES-128-GCM on newer ones.
/// Pairing ("bind") with a shared key returns the unit's own key, used for everything after.
/// </summary>
public static class GreeProtocol
{
    public const int Port = 7000;
    public static readonly byte[] EcbKey = "a3K8Bx%2r8Y7#xDh"u8.ToArray();
    public static readonly byte[] GcmKey = "{yxAHAY_Lm6pbC/<"u8.ToArray();
    static readonly byte[] GcmIv = [0x54, 0x40, 0x78, 0x44, 0x49, 0x67, 0x5A, 0x51, 0x6C, 0x5E, 0x63, 0x13];
    static readonly byte[] GcmAad = "qualcomm-test"u8.ToArray();

    public static string Request(string mac, JsonObject payload, byte[] key, bool gcm, int i)
    {
        string text = payload.ToJsonString();
        var msg = new JsonObject { ["cid"] = "app", ["i"] = i, ["t"] = "pack", ["tcid"] = mac, ["uid"] = 0 };
        if (gcm)
        {
            var (pack, tag) = EncryptGcm(key, text);
            msg["pack"] = pack;
            msg["tag"] = tag;
        }
        else
        {
            msg["pack"] = EncryptEcb(key, text);
        }
        return msg.ToJsonString();
    }

    /// <summary>Decrypts a reply's "pack" (GCM when it has a "tag").</summary>
    public static JsonObject Open(JsonObject msg, byte[] key)
    {
        if (msg["pack"] is not JsonValue p || !p.TryGetValue<string>(out var pack))
            throw new LedException("The air conditioner sent an unexpected reply.");
        string text = msg["tag"] is JsonValue t && t.TryGetValue<string>(out var tag)
            ? DecryptGcm(key, pack, tag)
            : DecryptEcb(key, pack);
        return JsonNode.Parse(text) as JsonObject ?? throw new LedException("The air conditioner sent an unexpected reply.");
    }

    public static bool IsGcm(JsonObject msg) => msg.ContainsKey("tag");

    static string EncryptEcb(byte[] key, string text)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return Convert.ToBase64String(aes.EncryptEcb(Encoding.UTF8.GetBytes(text), PaddingMode.PKCS7));
    }

    static string DecryptEcb(byte[] key, string pack)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        var raw = aes.DecryptEcb(Convert.FromBase64String(pack), PaddingMode.None);
        // Padding varies between firmware versions (PKCS7 or zeros): the JSON ends at the last brace.
        int end = Array.LastIndexOf(raw, (byte)'}');
        if (end < 0) throw new CryptographicException("Wrong key");
        return Encoding.UTF8.GetString(raw, 0, end + 1);
    }

    static (string Pack, string Tag) EncryptGcm(byte[] key, string text)
    {
        var plain = Encoding.UTF8.GetBytes(text);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var gcm = new AesGcm(key, 16);
        gcm.Encrypt(GcmIv, plain, cipher, tag, GcmAad);
        return (Convert.ToBase64String(cipher), Convert.ToBase64String(tag));
    }

    static string DecryptGcm(byte[] key, string pack, string tag)
    {
        var cipher = Convert.FromBase64String(pack);
        var plain = new byte[cipher.Length];
        using var gcm = new AesGcm(key, 16);
        gcm.Decrypt(GcmIv, cipher, Convert.FromBase64String(tag), plain, GcmAad);
        return Encoding.UTF8.GetString(plain);
    }

    // ------------------------------------------------------------ transport

    internal static Socket NewSocket()
    {
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true };
        // Windows otherwise fails the next receive when any target answers "port unreachable".
        const int SIO_UDP_CONNRESET = unchecked((int)0x9800000C);
        try { s.IOControl(SIO_UDP_CONNRESET, [0, 0, 0, 0], null); } catch (Exception) { }
        s.Bind(new IPEndPoint(IPAddress.Any, 0));
        return s;
    }

    /// <summary>Sends one request and waits for the reply from that address.</summary>
    public static async Task<JsonObject> ExchangeAsync(string ip, string request, int timeoutMs)
    {
        using var s = NewSocket();
        var target = new IPEndPoint(IPAddress.Parse(ip), Port);
        await s.SendToAsync(Encoding.UTF8.GetBytes(request), target);
        var buf = new byte[65535];
        using var cts = new CancellationTokenSource(timeoutMs);
        while (true)
        {
            SocketReceiveFromResult r;
            try
            {
                r = await s.ReceiveFromAsync(buf, new IPEndPoint(IPAddress.Any, 0), cts.Token);
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException();
            }
            if (!((IPEndPoint)r.RemoteEndPoint).Address.Equals(target.Address)) continue;
            if (JsonNode.Parse(buf.AsSpan(0, r.ReceivedBytes)) is JsonObject o) return o;
        }
    }
}

/// <summary>An air conditioner that answered a scan.</summary>
public sealed record GreeFound(string Ip, string Mac, string Name, string Firmware);

/// <summary>Talks to one paired air conditioner. Thread-safe for one caller at a time.</summary>
public sealed class GreeClient(AcConfig ac)
{
    /// <summary>The unit's key changed (it was re-paired): save the config.</summary>
    public event Action? KeyChanged;

    public AcConfig Config => ac;

    public static readonly string[] StatusCols =
        ["Pow", "Mod", "SetTem", "TemUn", "TemSen", "WdSpd", "Tur", "Quiet", "SwUpDn", "SwingLfRig",
         "Lig", "SwhSlp", "Health", "Blo", "Air", "SvSt", "StHt", "HeatCoolType"];

    public async Task<AcState> StatusAsync()
    {
        var cols = new JsonArray(StatusCols.Select(c => (JsonNode)c).ToArray());
        var reply = await CallAsync(new JsonObject { ["cols"] = cols, ["mac"] = ac.Mac, ["t"] = "status" });
        if (reply["cols"] is not JsonArray rc || reply["dat"] is not JsonArray rd)
            throw new LedException("The air conditioner sent an unexpected reply.");
        var values = new Dictionary<string, int>();
        for (int i = 0; i < Math.Min(rc.Count, rd.Count); i++)
        {
            if (rc[i] is JsonValue k && k.TryGetValue<string>(out var key) && rd[i] is JsonValue v && v.TryGetValue<int>(out var val))
                values[key] = val;
        }
        return new AcState(values);
    }

    public async Task SetAsync(IReadOnlyDictionary<string, int> values)
    {
        var opt = new JsonArray(values.Keys.Select(k => (JsonNode)k).ToArray());
        var p = new JsonArray(values.Values.Select(v => (JsonNode)v).ToArray());
        var reply = await CallAsync(new JsonObject { ["opt"] = opt, ["p"] = p, ["t"] = "cmd" });
        if (reply["r"] is JsonValue r && r.TryGetValue<int>(out var code) && code != 200)
            throw new LedException($"The air conditioner refused the change (code {code}).");
    }

    async Task<JsonObject> CallAsync(JsonObject payload)
    {
        if (ac.Key.Length != 16) await RebindAsync();
        bool rebound = false;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                var key = Encoding.ASCII.GetBytes(ac.Key);
                var reply = await GreeProtocol.ExchangeAsync(ac.Ip, GreeProtocol.Request(ac.Mac, payload, key, ac.Gcm, 0),
                                                             attempt == 0 ? 1200 : 2000);
                return GreeProtocol.Open(reply, key);
            }
            catch (TimeoutException)
            {
                if (attempt == 0) continue;
                // A unit paired again from the phone app has a new key and ignores the old one: pair again.
                if (rebound) throw NoReply();
                rebound = true;
                try
                {
                    await RebindAsync(rounds: 1);
                }
                catch (LedException)
                {
                    throw NoReply();
                }
            }
            catch (Exception e) when (e is CryptographicException or JsonException or FormatException && !rebound)
            {
                rebound = true;
                await RebindAsync();
            }
        }
    }

    LedException NoReply() =>
        new($"No reply from {ac.DisplayName} at {ac.Ip}. Check it has power and Wi-Fi, then try Settings > Find air conditioners.");

    async Task RebindAsync(int rounds = 2)
    {
        var (key, gcm) = await Gree.BindAsync(ac.Ip, ac.Mac, rounds);
        ac.Key = key;
        ac.Gcm = gcm;
        KeyChanged?.Invoke();
    }
}

public static class Gree
{
    /// <summary>
    /// Asks the home network for Gree units: a broadcast, plus every address on the PC's local subnets
    /// (some routers don't pass broadcasts on to Wi-Fi devices).
    /// </summary>
    public static async Task<List<GreeFound>> ScanAsync(IEnumerable<string> knownIps, int listenMs = 3000)
    {
        using var s = GreeProtocol.NewSocket();
        var scan = "{\"t\":\"scan\"}"u8.ToArray();
        var targets = new List<IPAddress> { IPAddress.Broadcast };
        foreach (var ip in knownIps)
            if (IPAddress.TryParse(ip, out var a)) targets.Add(a);
        foreach (var (addr, mask) in LocalSubnets())
        {
            uint net = ToUInt(addr) & ToUInt(mask), size = ~ToUInt(mask);
            if (size > 1024) continue; // only small home networks
            targets.Add(FromUInt(net | size));
            for (uint h = 1; h < size; h++) targets.Add(FromUInt(net | h));
        }

        var found = new Dictionary<string, GreeFound>();
        var buf = new byte[65535];
        using var cts = new CancellationTokenSource(listenMs + 2000);
        var receive = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                SocketReceiveFromResult r;
                try
                {
                    r = await s.ReceiveFromAsync(buf, new IPEndPoint(IPAddress.Any, 0), cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (SocketException)
                {
                    continue;
                }
                try
                {
                    var ip = ((IPEndPoint)r.RemoteEndPoint).Address.ToString();
                    if (JsonNode.Parse(buf.AsSpan(0, r.ReceivedBytes)) is not JsonObject msg) continue;
                    var dev = GreeProtocol.Open(msg, GreeProtocol.IsGcm(msg) ? GreeProtocol.GcmKey : GreeProtocol.EcbKey);
                    string mac = (string?)dev["mac"] ?? (string?)dev["cid"] ?? "";
                    if (mac.Length == 0) continue;
                    lock (found)
                        found[mac] = new GreeFound(ip, mac, (string?)dev["name"] ?? "", (string?)dev["ver"] ?? "");
                }
                catch (Exception)
                {
                    // not a Gree reply
                }
            }
        });

        for (int round = 0; round < 2; round++)
        {
            foreach (var t in targets)
            {
                try { await s.SendToAsync(scan, new IPEndPoint(t, GreeProtocol.Port)); } catch (SocketException) { }
            }
            await Task.Delay(listenMs / 2);
        }
        cts.CancelAfter(300);
        await receive;
        lock (found) return found.Values.OrderBy(f => ToUInt(IPAddress.Parse(f.Ip))).ToList();
    }

    public sealed record DiscoverResult(List<AcConfig> Acs, int Added, int Moved, List<string> Failed);

    /// <summary>
    /// Scans and pairs with any new units. Returns the updated list: known units keep their names,
    /// and get their new address if the router moved them.
    /// </summary>
    public static async Task<DiscoverResult> DiscoverAsync(IReadOnlyList<AcConfig> known)
    {
        var found = await ScanAsync(known.Select(k => k.Ip));
        var list = known.Select(k => k.Clone()).ToList();
        int added = 0, moved = 0;
        var failed = new List<string>();
        foreach (var f in found)
        {
            var ac = list.FirstOrDefault(a => a.Mac == f.Mac);
            try
            {
                if (ac != null)
                {
                    if (ac.Ip != f.Ip)
                    {
                        ac.Ip = f.Ip;
                        moved++;
                    }
                    if (ac.Key.Length != 16) (ac.Key, ac.Gcm) = await BindAsync(f.Ip, f.Mac);
                    continue;
                }
                var (key, gcm) = await BindAsync(f.Ip, f.Mac);
                string name = string.IsNullOrWhiteSpace(f.Name) ? $"Air conditioner {list.Count + 1}" : f.Name;
                list.Add(new AcConfig { Name = name, Ip = f.Ip, Mac = f.Mac, Key = key, Gcm = gcm });
                added++;
            }
            catch (LedException)
            {
                failed.Add(f.Ip);
            }
        }
        return new DiscoverResult(list, added, moved, failed);
    }

    /// <summary>Pairs with a unit and returns its own key. Newer modules only pair with GCM, older with ECB.</summary>
    public static async Task<(string Key, bool Gcm)> BindAsync(string ip, string mac, int rounds = 2)
    {
        var payload = new JsonObject { ["cid"] = mac, ["mac"] = mac, ["t"] = "bind", ["uid"] = 0 };
        for (int round = 0; round < rounds; round++)
        {
            foreach (bool gcm in new[] { true, false })
            {
                var shared = gcm ? GreeProtocol.GcmKey : GreeProtocol.EcbKey;
                try
                {
                    var reply = await GreeProtocol.ExchangeAsync(ip, GreeProtocol.Request(mac, payload.DeepClone().AsObject(), shared, gcm, 1), 1500);
                    var body = GreeProtocol.Open(reply, shared);
                    if ((string?)body["t"] == "bindok" && (string?)body["key"] is { Length: 16 } key)
                        return (key, GreeProtocol.IsGcm(reply));
                }
                catch (TimeoutException)
                {
                }
                catch (CryptographicException)
                {
                }
            }
        }
        throw new LedException($"The air conditioner at {ip} didn't accept pairing.");
    }

    static IEnumerable<(IPAddress Addr, IPAddress Mask)> LocalSubnets()
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var props = nic.GetIPProperties();
            if (props.GatewayAddresses.Count == 0) continue; // virtual switches, VPN adapters without a route out
            foreach (var u in props.UnicastAddresses)
                if (u.Address.AddressFamily == AddressFamily.InterNetwork && u.IPv4Mask != null)
                    yield return (u.Address, u.IPv4Mask);
        }
    }

    static uint ToUInt(IPAddress a)
    {
        var b = a.GetAddressBytes();
        return (uint)(b[0] << 24 | b[1] << 16 | b[2] << 8 | b[3]);
    }

    static IPAddress FromUInt(uint v) => new([(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v]);
}
