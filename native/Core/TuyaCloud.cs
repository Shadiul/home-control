using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LedControl.Core;

/// <summary>
/// Minimal Tuya Cloud (IoT platform OpenAPI) client with the HMAC-SHA256 request signing
/// described at developer.tuya.com ("new" signing algorithm, as TinyTuya uses).
/// </summary>
public sealed class TuyaCloud
{
    public static readonly (string Code, string Label)[] Regions =
    [
        ("eu", "Central Europe"), ("eu-w", "Western Europe"), ("us", "Western America"),
        ("us-e", "Eastern America"), ("in", "India"), ("cn", "China"), ("sg", "Singapore"),
    ];

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    readonly string host, apiKey, apiSecret;
    string? token;
    long tokenExpires;

    public TuyaCloud(CloudConfig c)
    {
        if (string.IsNullOrWhiteSpace(c.ApiKey) || string.IsNullOrWhiteSpace(c.ApiSecret))
            throw new LedException("Cloud control needs your Tuya IoT Access ID and Secret (see Settings).");
        apiKey = c.ApiKey.Trim();
        apiSecret = c.ApiSecret.Trim();
        host = c.Region switch
        {
            "us" or "az" => "openapi.tuyaus.com",
            "us-e" or "ue" => "openapi-ueaz.tuyaus.com",
            "eu" => "openapi.tuyaeu.com",
            "eu-w" or "we" => "openapi-weaz.tuyaeu.com",
            "in" => "openapi.tuyain.com",
            "sg" => "openapi-sg.iotbing.com",
            _ => "openapi.tuyacn.com",
        };
    }

    static string Sha256Hex(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();

    async Task<JsonNode> CallAsync(HttpMethod method, string path, JsonNode? body, bool withToken, CancellationToken ct)
    {
        string bodyText = body?.ToJsonString() ?? "";
        string t = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var req = new HttpRequestMessage(method, $"https://{host}{path}");
        string signedHeaders = "";
        if (body != null)
        {
            req.Content = new StringContent(bodyText, Encoding.UTF8);
            req.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            req.Headers.TryAddWithoutValidation("Signature-Headers", "Content-type");
            signedHeaders = "Content-type:application/json\n";
        }
        string stringToSign = $"{method.Method}\n{Sha256Hex(bodyText)}\n{signedHeaders}\n{path}";
        string payload = apiKey + (withToken ? token : "") + t + stringToSign;
        string sign = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(apiSecret),
                                                               Encoding.UTF8.GetBytes(payload)));
        req.Headers.TryAddWithoutValidation("client_id", apiKey);
        req.Headers.TryAddWithoutValidation("sign", sign);
        req.Headers.TryAddWithoutValidation("t", t);
        req.Headers.TryAddWithoutValidation("sign_method", "HMAC-SHA256");
        if (withToken) req.Headers.TryAddWithoutValidation("access_token", token);

        HttpResponseMessage resp;
        try
        {
            resp = await Http.SendAsync(req, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new LedException("Can't reach Tuya Cloud (check the internet connection).");
        }
        var text = await resp.Content.ReadAsStringAsync(ct);
        JsonNode? json;
        try { json = JsonNode.Parse(text); } catch (JsonException) { json = null; }
        if (json is not JsonObject obj)
            throw new LedException($"Tuya Cloud sent an unexpected reply (HTTP {(int)resp.StatusCode}).");
        return obj;
    }

    async Task EnsureTokenAsync(CancellationToken ct)
    {
        if (token != null && Environment.TickCount64 < tokenExpires) return;
        var r = await CallAsync(HttpMethod.Get, "/v1.0/token?grant_type=1", null, false, ct);
        if (r["success"]?.GetValue<bool>() != true)
            throw new LedException($"Tuya Cloud login failed: {r["msg"] ?? "no reason given"}");
        token = r["result"]?["access_token"]?.GetValue<string>();
        int secs = r["result"]?["expire_time"]?.GetValue<int>() ?? 3600;
        tokenExpires = Environment.TickCount64 + Math.Max(60, secs - 120) * 1000L;
    }

    async Task<JsonNode?> ApiAsync(HttpMethod method, string path, JsonNode? body, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            await EnsureTokenAsync(ct);
            var r = await CallAsync(method, path, body, true, ct);
            if (r["success"]?.GetValue<bool>() == true) return r["result"];
            int code = r["code"] is JsonValue v && v.TryGetValue<int>(out var c) ? c : 0;
            if (attempt == 0 && code is 1010 or 1011) // token expired or invalid: log in again once
            {
                token = null;
                continue;
            }
            throw new LedException($"Tuya Cloud: {r["msg"] ?? "request failed"} (code {code})");
        }
    }

    /// <summary>Status as code -> value, e.g. switch_led -> true.</summary>
    public async Task<Dictionary<string, JsonNode?>> StatusAsync(string deviceId, CancellationToken ct = default)
    {
        var result = await ApiAsync(HttpMethod.Get, $"/v1.0/iot-03/devices/{deviceId}/status", null, ct);
        var map = new Dictionary<string, JsonNode?>();
        if (result is JsonArray arr)
            foreach (var item in arr)
                if (item?["code"]?.GetValue<string>() is { } code)
                    map[code] = item["value"]?.DeepClone();
        return map;
    }

    public Task SendAsync(string deviceId, IEnumerable<(string Code, JsonNode Value)> commands, CancellationToken ct = default)
    {
        var list = new JsonArray();
        foreach (var (code, value) in commands)
            list.Add(new JsonObject { ["code"] = code, ["value"] = value.DeepClone() });
        return ApiAsync(HttpMethod.Post, $"/v1.0/iot-03/devices/{deviceId}/commands",
                        new JsonObject { ["commands"] = list }, ct);
    }

    /// <summary>Device details, including its name, local key and LAN IP.</summary>
    public async Task<(string Name, string LocalKey, string Ip)> DeviceInfoAsync(string deviceId, CancellationToken ct = default)
    {
        var r = await ApiAsync(HttpMethod.Get, $"/v1.0/devices/{deviceId}", null, ct)
                ?? throw new LedException("Tuya Cloud returned no device details.");
        return (r["name"]?.GetValue<string>() ?? "", r["local_key"]?.GetValue<string>() ?? "",
                r["ip"]?.GetValue<string>() ?? "");
    }
}
