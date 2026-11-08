using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HomeControl.Core;

/// <summary>Errors worth showing to the user as-is.</summary>
public sealed class LedException(string message) : Exception(message);

public static class TuyaCmd
{
    public const int SessKeyNegStart = 3, SessKeyNegResp = 4, SessKeyNegFinish = 5,
        Control = 7, Status = 8, HeartBeat = 9, DpQuery = 10, ControlNew = 13,
        DpQueryNew = 16, UpdateDps = 18, LanExtStream = 64;
}

public sealed record TuyaFrame(uint Seq, int Cmd, uint RetCode, byte[] Payload);

/// <summary>
/// Tuya local protocol 3.5 framing ("6699" frames, AES-GCM). Ported from TinyTuya.
///   header  = prefix 00006699 | 2 zero bytes | seq | cmd | length     (18 bytes, big endian)
///   body    = 12-byte IV | ciphertext | 16-byte GCM tag                (length bytes)
///   suffix  = 00009966
/// The GCM additional data is header[4..18].
/// </summary>
public static class TuyaProtocol
{
    public const int HeaderLen = 18;
    public const uint Prefix6699 = 0x00006699, Suffix6699 = 0x00009966;
    public const uint Prefix55AA = 0x000055AA, Suffix55AA = 0x0000AA55;
    const int MaxBody = 64 * 1024;

    static readonly byte[] Version35Header = [.. "3.5"u8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    // Commands sent without the "3.5" + 12 zero bytes version header.
    static readonly HashSet<int> NoVersionHeader =
    [
        TuyaCmd.DpQuery, TuyaCmd.DpQueryNew, TuyaCmd.UpdateDps, TuyaCmd.HeartBeat,
        TuyaCmd.SessKeyNegStart, TuyaCmd.SessKeyNegResp, TuyaCmd.SessKeyNegFinish, TuyaCmd.LanExtStream,
    ];

    public static byte[] Pack(uint seq, int cmd, ReadOnlySpan<byte> payload, byte[] key)
    {
        byte[] plain = NoVersionHeader.Contains(cmd) ? payload.ToArray() : [.. Version35Header, .. payload];
        int len = 12 + plain.Length + 16;
        var frame = new byte[HeaderLen + len + 4];
        var s = frame.AsSpan();
        BinaryPrimitives.WriteUInt32BigEndian(s, Prefix6699);
        BinaryPrimitives.WriteUInt32BigEndian(s[6..], seq);
        BinaryPrimitives.WriteUInt32BigEndian(s[10..], (uint)cmd);
        BinaryPrimitives.WriteUInt32BigEndian(s[14..], (uint)len);
        var iv = s.Slice(HeaderLen, 12);
        RandomNumberGenerator.Fill(iv);
        using var gcm = new AesGcm(key, 16);
        gcm.Encrypt(iv, plain, s.Slice(HeaderLen + 12, plain.Length),
                    s.Slice(HeaderLen + 12 + plain.Length, 16), s[4..HeaderLen]);
        BinaryPrimitives.WriteUInt32BigEndian(s[^4..], Suffix6699);
        return frame;
    }

    /// <summary>Validates an 18-byte header and returns how many more bytes the frame needs.</summary>
    public static int RemainingAfterHeader(ReadOnlySpan<byte> header)
    {
        if (BinaryPrimitives.ReadUInt32BigEndian(header) != Prefix6699)
            throw new LedException("The light sent data in an unexpected format.");
        int len = (int)BinaryPrimitives.ReadUInt32BigEndian(header[14..]);
        if (len < 28 || len > MaxBody)
            throw new LedException("The light sent a corrupt message.");
        return len + 4;
    }

    /// <param name="stripRetcode">true: always strip the 4-byte return code (TCP replies);
    /// null: strip only if the JSON starts right after it (UDP broadcasts).</param>
    public static TuyaFrame Unpack(ReadOnlySpan<byte> frame, byte[] key, bool? stripRetcode = true)
    {
        uint seq = BinaryPrimitives.ReadUInt32BigEndian(frame[6..]);
        int cmd = (int)BinaryPrimitives.ReadUInt32BigEndian(frame[10..]);
        int len = (int)BinaryPrimitives.ReadUInt32BigEndian(frame[14..]);
        int ctLen = len - 28;
        if (ctLen < 0 || frame.Length < HeaderLen + len)
            throw new LedException("The light sent a truncated message.");
        var plain = new byte[ctLen];
        using (var gcm = new AesGcm(key, 16))
        {
            gcm.Decrypt(frame.Slice(HeaderLen, 12), frame.Slice(HeaderLen + 12, ctLen),
                        frame.Slice(HeaderLen + 12 + ctLen, 16), plain, frame[4..HeaderLen]);
        }
        uint rc = 0;
        int off = 0;
        bool strip = stripRetcode ?? (plain.Length > 4 && plain[0] != (byte)'{' && plain[4] == (byte)'{');
        if (strip && plain.Length >= 4)
        {
            rc = BinaryPrimitives.ReadUInt32BigEndian(plain);
            off = 4;
        }
        var body = plain.AsSpan(off);
        if (cmd != TuyaCmd.SessKeyNegResp && body.Length >= 15 && body.StartsWith("3.5"u8))
            body = body[15..];
        return new TuyaFrame(seq, cmd, rc, body.ToArray());
    }

    /// <summary>3.5 session key: (local nonce XOR remote nonce) encrypted with AES-GCM, IV = local nonce[0..12].</summary>
    public static byte[] SessionKey(byte[] realKey, byte[] localNonce, byte[] remoteNonce)
    {
        var x = new byte[16];
        for (int i = 0; i < 16; i++) x[i] = (byte)(localNonce[i] ^ remoteNonce[i]);
        var ct = new byte[16];
        var tag = new byte[16];
        using var gcm = new AesGcm(realKey, 16);
        gcm.Encrypt(localNonce.AsSpan(0, 12), x, ct, tag);
        return ct;
    }

    public static byte[] ControlPayload(IReadOnlyDictionary<string, object> dps) =>
        JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["protocol"] = 5,
            ["t"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["data"] = new Dictionary<string, object> { ["dps"] = dps },
        });

    /// <summary>Pulls the "dps" object out of a reply: {"dps":{..}} or {"data":{"dps":{..}}}.</summary>
    public static Dictionary<string, JsonElement>? ParseDps(byte[] payload)
    {
        if (payload.Length == 0 || payload[0] != (byte)'{') return null;
        try
        {
            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            if (!root.TryGetProperty("dps", out var dps) &&
                !(root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
                  data.TryGetProperty("dps", out dps)))
                return null;
            if (dps.ValueKind != JsonValueKind.Object) return null;
            var result = new Dictionary<string, JsonElement>();
            foreach (var p in dps.EnumerateObject()) result[p.Name] = p.Value.Clone();
            return result;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string Hex(ReadOnlySpan<byte> b) => Convert.ToHexString(b).ToLowerInvariant();
    public static string Utf8(byte[] b) => Encoding.UTF8.GetString(b);
}
