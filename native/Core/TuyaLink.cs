using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LedControl.Core;

/// <summary>
/// One persistent, encrypted connection to the light (protocol 3.5, TCP port 6668).
/// A background reader receives replies and the status reports the light pushes when it
/// changes (also when changed from the phone app). Heartbeats keep the link alive.
/// </summary>
public sealed class TuyaLink : IDisposable
{
    public const int Port = 6668;

    public string DeviceId { get; }
    public string Ip { get; }

    readonly byte[] realKey;
    readonly object writeGate = new();
    readonly ConcurrentDictionary<int, TaskCompletionSource<TuyaFrame>> waiting = new();
    Socket? sock;
    byte[] key = [];
    uint seq = 1;
    CancellationTokenSource? life;
    Timer? heartbeat;
    long lastRx, lastTx;
    int closed;

    /// <summary>Raised on the reader thread with every data-point report.</summary>
    public event Action<Dictionary<string, JsonElement>>? Reported;
    public event Action<TuyaLink>? Closed;

    public bool IsOpen => sock != null && Volatile.Read(ref closed) == 0;

    public TuyaLink(string deviceId, string ip, string localKey)
    {
        DeviceId = deviceId;
        Ip = ip;
        realKey = Encoding.UTF8.GetBytes(localKey);
        if (realKey.Length != 16)
            throw new LedException("The local key should be 16 characters. Get it again in Settings.");
    }

    public static async Task<bool> ReachableAsync(string ip, int timeoutMs = 1500)
    {
        if (!IPAddress.TryParse(ip, out var addr)) return false;
        using var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await s.ConnectAsync(addr, Port, cts.Token);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task OpenAsync(CancellationToken ct = default)
    {
        if (!IPAddress.TryParse(Ip, out var addr))
            throw new LedException($"\"{Ip}\" isn't a valid IP address.");
        var s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(2500);
                try
                {
                    await s.ConnectAsync(addr, Port, cts.Token);
                }
                catch (Exception e) when (e is OperationCanceledException or SocketException && !ct.IsCancellationRequested)
                {
                    throw new LedException($"Can't reach the light at {Ip} on your home network.");
                }
            }

            // Session key negotiation (TinyTuya _negotiate_session_key).
            var localNonce = RandomNumberGenerator.GetBytes(16);
            await s.SendAsync(TuyaProtocol.Pack(seq++, TuyaCmd.SessKeyNegStart, localNonce, realKey), ct);
            TuyaFrame resp;
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(3000);
                resp = TuyaProtocol.Unpack(await ReadFrameAsync(s, cts.Token), realKey);
            }
            catch (AuthenticationTagMismatchException)
            {
                throw new LedException("The light rejected the local key. Get a fresh key in Settings.");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new LedException("The light accepted the connection but didn't answer.");
            }
            if (resp.Cmd != TuyaCmd.SessKeyNegResp || resp.Payload.Length < 48)
                throw new LedException("The light refused the secure connection.");
            var remoteNonce = resp.Payload[..16];
            var expected = HMACSHA256.HashData(realKey, localNonce);
            if (!CryptographicOperations.FixedTimeEquals(expected, resp.Payload.AsSpan(16, 32)))
                throw new LedException("The light's reply failed the security check (wrong local key?).");
            await s.SendAsync(TuyaProtocol.Pack(seq++, TuyaCmd.SessKeyNegFinish,
                                                HMACSHA256.HashData(realKey, remoteNonce), realKey), ct);
            key = TuyaProtocol.SessionKey(realKey, localNonce, remoteNonce);
        }
        catch
        {
            s.Dispose();
            throw;
        }

        sock = s;
        life = new CancellationTokenSource();
        lastRx = lastTx = Environment.TickCount64;
        _ = Task.Run(() => ReadLoopAsync(s, life.Token));
        heartbeat = new Timer(_ => Beat(), null, 5000, 5000);
    }

    static async Task<byte[]> ReadFrameAsync(Socket s, CancellationToken ct)
    {
        var header = new byte[TuyaProtocol.HeaderLen];
        await ReadExactAsync(s, header, ct);
        int rest = TuyaProtocol.RemainingAfterHeader(header);
        var frame = new byte[TuyaProtocol.HeaderLen + rest];
        header.CopyTo(frame, 0);
        await ReadExactAsync(s, frame.AsMemory(TuyaProtocol.HeaderLen), ct);
        return frame;
    }

    static async Task ReadExactAsync(Socket s, Memory<byte> buf, CancellationToken ct)
    {
        int got = 0;
        while (got < buf.Length)
        {
            int n = await s.ReceiveAsync(buf[got..], SocketFlags.None, ct);
            if (n == 0) throw new LedException("The light closed the connection.");
            got += n;
        }
    }

    async Task ReadLoopAsync(Socket s, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var raw = await ReadFrameAsync(s, ct);
                Interlocked.Exchange(ref lastRx, Environment.TickCount64);
                TuyaFrame f;
                try
                {
                    f = TuyaProtocol.Unpack(raw, key);
                }
                catch (CryptographicException)
                {
                    continue;
                }
                if (waiting.TryRemove(f.Cmd, out var tcs)) tcs.TrySetResult(f);
                var dps = TuyaProtocol.ParseDps(f.Payload);
                if (dps is { Count: > 0 })
                {
                    try { Reported?.Invoke(dps); } catch { /* a listener bug must not kill the link */ }
                }
            }
        }
        catch (Exception)
        {
            // Connection ended; Close() below tells everyone.
        }
        finally
        {
            Close();
        }
    }

    void Beat()
    {
        if (!IsOpen) return;
        long now = Environment.TickCount64;
        if (now - Interlocked.Read(ref lastRx) > 25_000)
        {
            Close(); // no sign of life for 25 s
            return;
        }
        if (now - Interlocked.Read(ref lastTx) >= 9_000)
        {
            try
            {
                Send(TuyaCmd.HeartBeat, JsonSerializer.SerializeToUtf8Bytes(
                    new Dictionary<string, string> { ["gwId"] = DeviceId, ["devId"] = DeviceId }));
            }
            catch (Exception)
            {
                // Send() already closed the link.
            }
        }
    }

    /// <summary>Fire-and-forget send.</summary>
    public void Send(int cmd, byte[] payload)
    {
        var s = sock;
        if (s == null || !IsOpen) throw new LedException("Not connected to the light.");
        try
        {
            lock (writeGate)
            {
                s.Send(TuyaProtocol.Pack(seq++, cmd, payload, key));
            }
            Interlocked.Exchange(ref lastTx, Environment.TickCount64);
        }
        catch (Exception e) when (e is SocketException or ObjectDisposedException)
        {
            Close();
            throw new LedException("Lost the connection to the light.");
        }
    }

    /// <summary>Sends and waits for a reply with the given command number.</summary>
    public async Task<TuyaFrame> RequestAsync(int cmd, byte[] payload, int replyCmd, int timeoutMs = 3000,
                                              CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<TuyaFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (waiting.TryRemove(replyCmd, out var old)) old.TrySetCanceled();
        waiting[replyCmd] = tcs;
        Send(cmd, payload);
        try
        {
            return await tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs), ct);
        }
        catch (TimeoutException)
        {
            waiting.TryRemove(new KeyValuePair<int, TaskCompletionSource<TuyaFrame>>(replyCmd, tcs));
            Close(); // a link that stops answering is not worth keeping
            throw new LedException("The light didn't answer in time.");
        }
    }

    public Task<TuyaFrame> QueryAsync(CancellationToken ct = default) =>
        RequestAsync(TuyaCmd.DpQueryNew, "{}"u8.ToArray(), TuyaCmd.DpQueryNew, 3000, ct);

    public Task<TuyaFrame> SetAsync(IReadOnlyDictionary<string, object> dps, CancellationToken ct = default) =>
        RequestAsync(TuyaCmd.ControlNew, TuyaProtocol.ControlPayload(dps), TuyaCmd.ControlNew, 3000, ct);

    public void SetNoWait(IReadOnlyDictionary<string, object> dps) =>
        Send(TuyaCmd.ControlNew, TuyaProtocol.ControlPayload(dps));

    void Close()
    {
        if (Interlocked.Exchange(ref closed, 1) != 0) return;
        heartbeat?.Dispose();
        try { life?.Cancel(); } catch (ObjectDisposedException) { }
        try { sock?.Shutdown(SocketShutdown.Both); } catch { }
        sock?.Dispose();
        foreach (var w in waiting.Values) w.TrySetException(new LedException("Lost the connection to the light."));
        waiting.Clear();
        try { Closed?.Invoke(this); } catch { }
    }

    public void Dispose() => Close();
}
