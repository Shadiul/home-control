namespace LedControl.Core.Rgb;

/// <summary>One accessory on an NZXT lighting channel (an LED strip or an RGB fan).</summary>
public sealed record NzxtAccessory(byte Id, string Name, int Leds);

/// <summary>
/// NZXT Smart Device V2 and RGB &amp; Fan Controller (HUE 2 protocol), over HID.
/// Every command is a 64-byte output report whose first byte is the command (and report ID).
/// Colours are sent G, R, B. Lighting uses only commands 0x10, 0x20, 0x22 and 0x28;
/// fan commands (0x60, 0x62) are never sent, so fan speeds are left alone.
/// Protocol as documented by liquidctl (smart_device.py, SmartDevice2) and OpenRGB (NZXTHue2Controller).
/// </summary>
public sealed class NzxtSmartDevice : IDisposable
{
    public const ushort Vid = 0x1E71;
    static readonly ushort[] Pids = [0x2006, 0x200D, 0x200F, 0x2009, 0x200E, 0x2010];
    public const int MaxLedsPerChannel = 40;

    readonly HidDevice dev;
    public string Firmware { get; private set; } = "";
    /// <summary>Accessories on each lighting channel, in daisy-chain order.</summary>
    public List<List<NzxtAccessory>> Channels { get; } = [];
    public int LedCount(int channel) => Math.Min(MaxLedsPerChannel, Channels[channel].Sum(a => a.Leds));

    NzxtSmartDevice(HidDevice d) { dev = d; }

    public static HidInfo? Find() =>
        HidDevice.List(Vid).FirstOrDefault(i => Pids.Contains(i.Pid) && i.UsagePage == 0xFF00);

    /// <summary>Opens the device and reads its firmware version and the accessories on each channel. Changes nothing.</summary>
    public static NzxtSmartDevice Open(HidInfo info)
    {
        if (info.OutputLength != 64)
            throw new InvalidOperationException($"Unexpected report size {info.OutputLength} (expected 64); not sending anything.");
        var d = new NzxtSmartDevice(HidDevice.Open(info));
        try { d.Detect(); }
        catch { d.Dispose(); throw; }
        return d;
    }

    void Detect()
    {
        dev.FlushInput();
        dev.Write([0x10, 0x01]);
        var fw = dev.ReadUntil(r => r[0] == 0x11 && r[1] == 0x01, 3000) ?? throw new TimeoutException("No firmware reply from the NZXT controller");
        Firmware = $"{fw[0x11]}.{fw[0x12]}.{fw[0x13]}";

        // The accessory reply can take a few seconds (about 2.5 s seen on a Smart Device V2), and status
        // reports (0x67) arrive in between, so read for a while and skip anything else.
        dev.Write([0x20, 0x03]);
        var acc = dev.ReadUntil(r => r[0] == 0x21 && r[1] == 0x03, 6000)
                  ?? throw new TimeoutException("No accessory reply from the NZXT controller");
        int count = Math.Clamp((int)acc[14], 0, 6);
        Channels.Clear();
        for (int c = 0; c < count; c++)
        {
            var list = new List<NzxtAccessory>();
            for (int a = 0; a < 6; a++)
            {
                int at = 15 + 6 * c + a;
                if (at >= acc.Length) break;
                byte id = acc[at];
                if (id == 0) break;
                var (name, leds) = Accessory(id);
                list.Add(new NzxtAccessory(id, name, leds));
            }
            Channels.Add(list);
        }
    }

    /// <summary>Accessory IDs to names and LED counts (OpenRGB's table).</summary>
    static (string, int) Accessory(byte id) => id switch
    {
        0x01 => ("HUE+ LED strip", 10),
        0x02 => ("Aer RGB fan", 8),
        0x04 => ("HUE 2 LED strip 300 mm", 10),
        0x05 => ("HUE 2 LED strip 250 mm", 8),
        0x06 => ("HUE 2 LED strip 200 mm", 6),
        0x07 or 0x08 => ("HUE 2 cable comb", 14),
        0x09 => ("HUE 2 underglow 300 mm", 15),
        0x0A => ("HUE 2 underglow 200 mm", 10),
        0x0B => ("Aer RGB 2 120 mm fan", 8),
        0x0C => ("Aer RGB 2 140 mm fan", 8),
        0x10 => ("Kraken X3 ring", 8),
        0x11 => ("Kraken X3 logo", 1),
        0x13 => ("F120 RGB fan", 18),
        0x14 => ("F140 RGB fan", 18),
        0x15 => ("F120 RGB Duo fan", 20),
        0x16 => ("F140 RGB Duo fan", 20),
        0x17 => ("F120 RGB Core fan", 8),
        0x18 => ("F140 RGB Core fan", 8),
        0x19 => ("F120 RGB Core fan", 8),
        0x1C => ("F280 RGB Core fans", 16),
        0x1D => ("F360 RGB Core fans", 24),
        0x1E => ("Kraken Elite ring", 24),
        0x1F => ("F420 RGB fans", 24),
        _ => ($"Unknown accessory 0x{id:X2}", 0),
    };

    static byte Mask(int channel) => (byte)(1 << channel);

    /// <summary>One colour for a whole channel, using the built-in "fixed" mode (one packet).</summary>
    public void SetChannel(int channel, byte r, byte g, byte b) =>
        dev.Write([0x28, 0x03, Mask(channel), 0x00, 0x00, 0x02, 0x00, 0x00, 0x01, 0x00, g, r, b]);

    /// <summary>Per-LED colours for a channel ("direct" mode), for smooth effects and music sync. Up to 40 LEDs; the rest go dark.</summary>
    public void SetLeds(int channel, ReadOnlySpan<(byte R, byte G, byte B)> colours)
    {
        var grb = new byte[MaxLedsPerChannel * 3];
        int n = Math.Min(colours.Length, MaxLedsPerChannel);
        for (int i = 0; i < n; i++)
        {
            grb[3 * i] = colours[i].G;
            grb[3 * i + 1] = colours[i].R;
            grb[3 * i + 2] = colours[i].B;
        }
        byte m = Mask(channel);
        var p = new byte[64];
        p[0] = 0x22; p[1] = 0x10; p[2] = m;
        grb.AsSpan(0, 60).CopyTo(p.AsSpan(4));
        dev.Write(p);
        p = new byte[64];
        p[0] = 0x22; p[1] = 0x11; p[2] = m;
        grb.AsSpan(60, 60).CopyTo(p.AsSpan(4));
        dev.Write(p);
        dev.Write([0x22, 0xA0, m, 0x00, 0x01, 0x00, 0x00, 0x28, 0x00, 0x00, 0x80, 0x00, 0x32, 0x00, 0x00, 0x01]);
    }

    public void SetAll(byte r, byte g, byte b)
    {
        for (int c = 0; c < Channels.Count; c++) SetChannel(c, r, g, b);
    }

    public void Off() => SetAll(0, 0, 0);

    public void Dispose() => dev.Dispose();
}
