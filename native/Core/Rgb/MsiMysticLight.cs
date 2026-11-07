namespace LedControl.Core.Rgb;

/// <summary>
/// MSI Mystic Light motherboard lighting, 185-byte variant (boards such as MS-7C37 / X570-A PRO), over HID feature report 0x52.
///
/// Safety rules, because this controller has a history of being bricked by bad packets:
/// - every packet starts from the state read back from the board (read-modify-write), or from a packet built that way;
/// - only the static mode (1) with a full-brightness, custom-colour setting is ever written;
/// - the save byte (184) is always 0, so nothing is written to the board's flash;
/// - the state read at open is kept, and <see cref="Restore"/> puts it back.
/// Layout as documented by OpenRGB (MSIMotherboard185Controller) and mystic-why.
/// </summary>
public sealed class MsiMysticLight : IDisposable
{
    public const ushort Vid = 0x1462;
    const byte ReportId = 0x52;
    const int PacketLength = 185;
    const int SaveByte = 184;
    const byte StaticMode = 0x01;
    const byte FullBrightness = 0x28; // brightness 10 of 10 in bits 2-6, speed 0
    const byte CustomColour = 0x80;

    // Zone offsets in the 185-byte packet.
    const int JRgb1 = 1, JRainbow1 = 31, JRainbow2 = 42, OnBoard = 74, JRgb2 = 174;
    static readonly int[] OnBoardLeds = [84, 94, 104, 114, 124, 134];

    readonly HidDevice dev;
    readonly byte[] original;
    byte[] current;

    MsiMysticLight(HidDevice d, byte[] state)
    {
        dev = d;
        original = state;
        current = (byte[])state.Clone();
    }

    public static HidInfo? Find() =>
        HidDevice.List(Vid).FirstOrDefault(i => i.FeatureLength >= PacketLength && i.UsagePage == 0x0001);

    /// <summary>Opens the controller and reads its current lighting state. Sends nothing.</summary>
    public static MsiMysticLight Open(HidInfo info)
    {
        var d = HidDevice.Open(info);
        try
        {
            var state = d.GetFeature(ReportId);
            if (state[0] != ReportId)
                throw new InvalidOperationException($"Unexpected reply 0x{state[0]:X2} from Mystic Light; not sending anything.");
            state[SaveByte] = 0;
            return new MsiMysticLight(d, state);
        }
        catch { d.Dispose(); throw; }
    }

    /// <summary>The state as read when the controller was opened (for diagnostics).</summary>
    public ReadOnlySpan<byte> Original => original;

    static void Zone(byte[] p, int o, byte r, byte g, byte b)
    {
        p[o] = StaticMode;
        p[o + 1] = r; p[o + 2] = g; p[o + 3] = b;
        p[o + 4] = FullBrightness;
        p[o + 5] = r; p[o + 6] = g; p[o + 7] = b;
        p[o + 8] |= CustomColour;
        p[o + 9] = 0;
    }

    /// <summary>
    /// Sets every header and the on-board LEDs to one colour (black = off).
    /// The first call after opening re-sends the board's own state first, as MSI's software does.
    /// </summary>
    public void SetAll(byte r, byte g, byte b)
    {
        var p = (byte[])current.Clone();
        foreach (int o in new[] { JRgb1, JRainbow1, JRainbow2, JRgb2 }) Zone(p, o, r, g, b);
        Zone(p, OnBoard, r, g, b);
        p[OnBoard + 4] &= 0x7F;           // no JRGB sync for static modes
        p[OnBoard + 8] &= unchecked((byte)~0x30); // no pipe sync
        foreach (int o in OnBoardLeds) Zone(p, o, r, g, b);
        p[SaveByte] = 0;
        if (current.AsSpan().SequenceEqual(original)) Send(original);
        Send(p);
        current = p;
    }

    public void Off() => SetAll(0, 0, 0);

    /// <summary>Puts back the lighting the board had when it was opened.</summary>
    public void Restore()
    {
        Send(original);
        current = (byte[])original.Clone();
    }

    void Send(byte[] p)
    {
        if (p[0] != ReportId || p[SaveByte] != 0) throw new InvalidOperationException("Refusing to send an unsafe Mystic Light packet.");
        dev.SetFeature(p.AsSpan(0, Math.Min(p.Length, dev.Info.FeatureLength)));
    }

    public void Dispose() => dev.Dispose();
}
