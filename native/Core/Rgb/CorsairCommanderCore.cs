namespace LedControl.Core.Rgb;

/// <summary>
/// Corsair iCUE Commander CORE (1B1C:0C1C), the hub of Elite Capellix coolers.
///
/// Custom colours need "software mode", and in software mode the hub stops running its stored pump and fan curves.
/// So whenever this class holds software mode it also runs the cooling itself:
/// - the pump is set to 100%;
/// - the fans follow the coolant temperature (<see cref="FanDuty"/>), never below 20%, and 100% if the temperature can't be read;
/// - speeds are re-sent every second by <see cref="Tick"/>, so the hub never falls back to its software default;
/// - <see cref="Release"/> (also called on any error and on Dispose) puts the hub back in hardware mode,
///   where its own curves and lighting take over again. If the app dies, the hub returns to hardware mode by itself
///   after about 30 seconds without traffic.
/// Nothing is ever written to the hub's stored settings (the 0x6D endpoints and the port configuration 0x1E are never touched).
///
/// Requests are [0x00 report ID, 0x08, command, args...]; replies (without the report ID) are
/// [0x00, command echo, status, data type lo, data type hi, data...]. Firmware 2.x carries 96 bytes per packet.
/// Protocol as documented by liquidctl (commander_core), OpenRGB, FanControl.CorsairLink and OpenLinkHub.
/// </summary>
public sealed class CorsairCommanderCore : IDisposable
{
    public const ushort Vid = 0x1B1C, Pid = 0x0C1C;
    // Bytes per packet after the report ID. Windows reports the size: 1024 on this hub even with firmware 2.6
    // (sending a frame in 96-byte pieces there fails, because the zero padding completes the first packet).
    int Payload => Math.Max(64, dev.Info.OutputLength - 1);
    const byte LightingHandle = 0x00, DataHandle = 0x01;
    const byte EpSpeeds = 0x17, EpDuty = 0x18, EpLedCounts = 0x20, EpTemps = 0x21, EpColours = 0x22;

    readonly HidDevice dev;
    readonly object gate = new();
    public bool InSoftwareMode { get; private set; }
    /// <summary>LEDs per channel: 0 = pump, 1-6 = RGB ports (0 when nothing is connected).</summary>
    public int[] LedCounts { get; private set; } = new int[7];
    public int TotalLeds => LedCounts.Sum();
    public double? CoolantTemp { get; private set; }
    public int[] Rpm { get; private set; } = [];
    public int PumpDuty { get; private set; }
    public int FanDutyNow { get; private set; }
    byte[]? frame;
    long lastFrameSent;

    CorsairCommanderCore(HidDevice d) { dev = d; }

    /// <summary>The lighting/control interface: MI_00, usage page 0xFF42, usage 1.</summary>
    public static HidInfo? Find() =>
        HidDevice.List(Vid).FirstOrDefault(i => i.Pid == Pid && i.UsagePage == 0xFF42 && i.Usage == 0x0001);

    public static CorsairCommanderCore Open(HidInfo info) => new(HidDevice.Open(info));

    // ------------------------------------------------------------ transport

    byte[] Tx(params byte[] cmd)
    {
        dev.FlushInput();
        var req = new byte[2 + cmd.Length];
        req[1] = 0x08;
        cmd.CopyTo(req, 2);
        dev.Write(req);
        var r = dev.ReadUntil(x => x.Length > 3 && x[1] == 0x00 && x[2] == cmd[0], 1000)
                ?? throw new TimeoutException($"No reply from the Commander CORE to command 0x{cmd[0]:X2}");
        return r[1..];
    }

    byte[] TxOk(params byte[] cmd)
    {
        var r = Tx(cmd);
        if (r[2] != 0) throw new InvalidOperationException($"Commander CORE command 0x{cmd[0]:X2} failed with status {r[2]}");
        return r;
    }

    void CloseHandle(byte h) => Tx(0x05, 0x01, h); // status 3 just means it wasn't open

    byte[] ReadEndpoint(byte h, byte ep)
    {
        CloseHandle(h);
        TxOk(0x0D, h, ep);
        try { return TxOk(0x08, h, 0x01); }
        finally { CloseHandle(h); }
    }

    void WriteEndpoint(byte h, byte ep, byte dt0, byte dt1, byte[] data)
    {
        CloseHandle(h);
        TxOk(0x0D, h, ep);
        try
        {
            int len = data.Length + 2;
            int first = Math.Min(data.Length, Payload - 9);
            var p = new byte[8 + first];
            p[0] = 0x06; p[1] = h; p[2] = (byte)len; p[3] = (byte)(len >> 8); p[6] = dt0; p[7] = dt1;
            Array.Copy(data, 0, p, 8, first);
            TxOk(p);
            for (int at = first; at < data.Length; at += Payload - 3)
            {
                int n = Math.Min(Payload - 3, data.Length - at);
                var c = new byte[2 + n];
                c[0] = 0x07; c[1] = h;
                Array.Copy(data, at, c, 2, n);
                TxOk(c);
            }
        }
        finally { CloseHandle(h); }
    }

    // ------------------------------------------------------------ read-only properties

    public string Firmware()
    {
        var r = TxOk(0x02, 0x13);
        return $"{r[3]}.{r[4]}.{r[5] | r[6] << 8}";
    }

    /// <summary>1 = hardware mode (stored curves and lighting), 2 = software mode.</summary>
    public int Mode() => TxOk(0x02, 0x03)[3];

    // ------------------------------------------------------------ cooling

    /// <summary>Fan duty for a coolant temperature: 30% up to 30 °C, rising to 100% at 40 °C. Unknown temperature → 100%.</summary>
    public static int FanDuty(double? coolant) => coolant switch
    {
        null => 100,
        <= 30 => 30,
        >= 40 => 100,
        double t => (int)Math.Round(30 + (t - 30) / 10 * 70),
    };

    void ReadSensors()
    {
        // Temperatures: data type 10 00, then count, then per sensor: status (0 = OK), value (LE16, tenths of °C).
        var t = ReadEndpoint(DataHandle, EpTemps);
        CoolantTemp = null;
        if (t[3] == 0x10 && t[4] == 0x00 && t[5] >= 1 && t[6] == 0x00)
        {
            double c = (t[7] | t[8] << 8) / 10.0;
            if (c is > 5 and < 80) CoolantTemp = c;
        }
        // Speeds: data type 06 00, then count, then RPM (LE16) per channel.
        var s = ReadEndpoint(DataHandle, EpSpeeds);
        if (s[3] == 0x06 && s[4] == 0x00)
        {
            int n = Math.Min((int)s[5], 7);
            Rpm = Enumerable.Range(0, n).Select(i => s[6 + 2 * i] | s[7 + 2 * i] << 8).ToArray();
        }
    }

    void WriteSpeeds()
    {
        PumpDuty = 100;
        FanDutyNow = Math.Max(20, FanDuty(CoolantTemp));
        var d = new byte[1 + 7 * 4];
        d[0] = 7;
        for (int ch = 0; ch < 7; ch++)
        {
            d[1 + 4 * ch] = (byte)ch;
            d[3 + 4 * ch] = (byte)(ch == 0 ? PumpDuty : FanDutyNow);
        }
        WriteEndpoint(DataHandle, EpDuty, 0x07, 0x00, d);
    }

    // ------------------------------------------------------------ lighting

    /// <summary>
    /// Takes software mode and immediately sets safe speeds (pump 100%, fans 100% until the temperature is read),
    /// then reads what's connected. Any failure puts the hub back in hardware mode.
    /// </summary>
    public void Acquire()
    {
        lock (gate)
        {
            try
            {
                TxOk(0x01, 0x03, 0x00, 0x02);
                InSoftwareMode = true;
                CoolantTemp = null;
                WriteSpeeds();
                ReadLedCounts();
                ReadSensors();
                WriteSpeeds();
            }
            catch { Release(); throw; }
        }
    }

    /// <summary>The raw LED-count reply, for diagnostics.</summary>
    public byte[] LastLedReply { get; private set; } = [];

    void ReadLedCounts()
    {
        // Data type 0F 00, then channel count, then per channel: mode (LE16, 2 = connected), LED count (LE16).
        var r = ReadEndpoint(DataHandle, EpLedCounts);
        LastLedReply = r;
        var counts = new int[7];
        if (r[3] == 0x0F && r[4] == 0x00)
        {
            int n = Math.Min((int)r[5], 7);
            for (int i = 0; i < n; i++)
            {
                int mode = r[6 + 4 * i] | r[7 + 4 * i] << 8;
                int leds = r[8 + 4 * i] | r[9 + 4 * i] << 8;
                counts[i] = mode == 2 && leds is > 0 and <= 64 ? leds : 0;
            }
        }
        LedCounts = counts;
    }

    /// <summary>Sets every LED (pump first, then ports 1-6) to one colour. Requires <see cref="Acquire"/>.</summary>
    public void SetAll(byte r, byte g, byte b)
    {
        var f = new byte[TotalLeds * 3];
        for (int i = 0; i < f.Length; i += 3) { f[i] = r; f[i + 1] = g; f[i + 2] = b; }
        SetFrame(f);
    }

    /// <summary>Per-LED colours, R,G,B for each LED in order (pump first, then ports 1-6).</summary>
    public void SetFrame(byte[] rgb)
    {
        lock (gate)
        {
            if (!InSoftwareMode) throw new InvalidOperationException("Call Acquire first.");
            frame = rgb;
            SendFrame();
        }
    }

    void SendFrame()
    {
        if (frame == null || frame.Length == 0) return;
        WriteEndpoint(LightingHandle, EpColours, 0x12, 0x00, frame);
        lastFrameSent = Environment.TickCount64;
    }

    /// <summary>
    /// Call about once a second while holding software mode: re-takes software mode if something gave it back,
    /// reads the coolant temperature and RPM, re-sends the speeds, and refreshes the colours every few seconds.
    /// On any failure the hub is put back in hardware mode and the error is rethrown.
    /// </summary>
    public void Tick()
    {
        lock (gate)
        {
            if (!InSoftwareMode) return;
            try
            {
                if (Mode() != 2)
                {
                    TxOk(0x01, 0x03, 0x00, 0x02);
                    CoolantTemp = null;
                    WriteSpeeds();
                    SendFrame();
                }
                ReadSensors();
                WriteSpeeds();
                if (Environment.TickCount64 - lastFrameSent > 2500) SendFrame();
            }
            catch { Release(); throw; }
        }
    }

    /// <summary>Back to hardware mode: the hub's own pump/fan curves and stored lighting take over.</summary>
    public void Release()
    {
        lock (gate)
        {
            try { CloseHandle(LightingHandle); } catch { }
            try { CloseHandle(DataHandle); } catch { }
            try { Tx(0x01, 0x03, 0x00, 0x01); } catch { }
            InSoftwareMode = false;
        }
    }

    public void Dispose()
    {
        if (InSoftwareMode) Release();
        dev.Dispose();
    }
}
