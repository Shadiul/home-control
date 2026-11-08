using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HomeControl.Core;

/// <summary>
/// Finds the light by listening for the broadcasts Tuya devices send every few seconds
/// (UDP 6666 plain, 6667 encrypted, 7000 for newer firmware). Used to notice a changed IP.
/// </summary>
public static class Discovery
{
    static readonly byte[] UdpKey = MD5.HashData("yGAdlopoPVldABfn"u8);

    public sealed record Found(string Ip, string Version);

    public static async Task<Found?> FindAsync(string deviceId, int timeoutMs = 7000, CancellationToken ct = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        var sockets = new List<UdpClient>();
        foreach (var port in new[] { 6666, 6667, 7000 })
        {
            try
            {
                var u = new UdpClient();
                u.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                u.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                sockets.Add(u);
            }
            catch (SocketException)
            {
                // Port busy (another Tuya tool running); listen on the others.
            }
        }
        try
        {
            var listeners = sockets.Select(u => ListenAsync(u, deviceId, cts.Token)).ToList();
            while (listeners.Count > 0)
            {
                var done = await Task.WhenAny(listeners);
                listeners.Remove(done);
                if (await done is { } found)
                {
                    cts.Cancel();
                    return found;
                }
            }
            return null;
        }
        finally
        {
            foreach (var u in sockets) u.Dispose();
        }
    }

    static async Task<Found?> ListenAsync(UdpClient u, string deviceId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult r;
            try
            {
                r = await u.ReceiveAsync(ct);
            }
            catch (Exception)
            {
                return null;
            }
            var json = Decode(r.Buffer);
            if (json == null) continue;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                string? id = root.TryGetProperty("gwId", out var g) ? g.GetString()
                           : root.TryGetProperty("devId", out var dv) ? dv.GetString() : null;
                if (id != deviceId) continue;
                string ip = root.TryGetProperty("ip", out var ipEl) ? ipEl.GetString() ?? r.RemoteEndPoint.Address.ToString()
                                                                     : r.RemoteEndPoint.Address.ToString();
                string ver = root.TryGetProperty("version", out var v) ? v.ToString() : "";
                return new Found(ip, ver);
            }
            catch (Exception)
            {
                // not JSON we understand
            }
        }
        return null;
    }

    static string? Decode(byte[] data)
    {
        try
        {
            if (data.Length >= 4)
            {
                uint prefix = BinaryPrimitives.ReadUInt32BigEndian(data);
                if (prefix == TuyaProtocol.Prefix6699)
                    return TuyaProtocol.Utf8(TuyaProtocol.Unpack(data, UdpKey, stripRetcode: null).Payload).TrimEnd('\0');
                if (prefix == TuyaProtocol.Prefix55AA && data.Length >= 28)
                {
                    int len = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(12));
                    // header(16) | retcode(4) | payload | crc(4) | suffix(4)
                    var payload = data.AsSpan(20, Math.Min(len, data.Length - 16) - 12);
                    if (payload.Length > 0 && payload[0] == (byte)'{') return Encoding.UTF8.GetString(payload);
                    return EcbDecrypt(payload.ToArray());
                }
            }
            return EcbDecrypt(data);
        }
        catch (Exception)
        {
            return null;
        }
    }

    static string EcbDecrypt(byte[] data)
    {
        using var aes = Aes.Create();
        aes.Key = UdpKey;
        return Encoding.UTF8.GetString(aes.DecryptEcb(data, PaddingMode.PKCS7));
    }
}
