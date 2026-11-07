using LedControl.Core.Rgb;

namespace LedControl;

/// <summary>
/// Probe.exe rgb list               lists the RGB controllers' HID interfaces (opens nothing for writing)
/// Probe.exe rgb nzxt               reads the NZXT controller's firmware and accessories (queries only)
/// Probe.exe rgb nzxt R G B         sets every NZXT channel to one colour
/// Probe.exe rgb nzxt off           turns the NZXT lights off
/// </summary>
static class RgbProbe
{
    static readonly ushort[] Vids = [0x1E71, 0x1B1C, 0x1462];

    public static void Run(string[] args)
    {
        string what = args.Length > 1 ? args[1] : "list";
        if (what == "list")
        {
            foreach (var i in HidDevice.List().Where(i => Vids.Contains(i.Vid)))
                Console.WriteLine($"{i.Vid:X4}:{i.Pid:X4} v{i.Version:X4}  page {i.UsagePage:X4} usage {i.Usage:X4}  in {i.InputLength} out {i.OutputLength} feature {i.FeatureLength}  \"{i.Product}\"");
            return;
        }
        if (what == "corsair")
        {
            // Read-only: firmware and mode properties, which don't take the device out of hardware mode.
            var info = CorsairCommanderCore.Find();
            if (info == null) { Console.WriteLine("No Commander CORE found."); return; }
            using var c = CorsairCommanderCore.Open(info);
            Console.WriteLine($"{info.Product} {info.Pid:X4}, reports in {info.InputLength} / out {info.OutputLength}");
            Console.WriteLine($"  firmware {c.Firmware()}");
            int m = c.Mode();
            Console.WriteLine($"  mode {m} ({(m == 1 ? "hardware: stored curves and lighting" : m == 2 ? "software: host in control" : "unknown")})");
            if (args.Length > 5 && args[2] == "test")
            {
                // rgb corsair test R G B seconds: takes software mode, runs cooling, shows a colour, then gives the hub back.
                byte r = byte.Parse(args[3]), g = byte.Parse(args[4]), b = byte.Parse(args[5]);
                int secs = args.Length > 6 ? int.Parse(args[6]) : 15;
                Console.CancelKeyPress += (_, e) => { e.Cancel = true; c.Release(); Environment.Exit(1); };
                try
                {
                    c.Acquire();
                    Console.WriteLine("  LED reply: " + string.Join(' ', c.LastLedReply.Take(40).Select(x => x.ToString("X2"))));
                    Console.WriteLine($"  LEDs per channel (pump, ports 1-6): {string.Join(", ", c.LedCounts)} = {c.TotalLeds}");
                    Console.WriteLine($"  coolant {c.CoolantTemp?.ToString("0.0") ?? "?"} °C, pump {c.PumpDuty}%, fans {c.FanDutyNow}%, rpm {string.Join(", ", c.Rpm)}");
                    if (c.TotalLeds > 0) { c.SetAll(r, g, b); Console.WriteLine($"  colour {r},{g},{b} sent to {c.TotalLeds} LEDs"); }
                    for (int s = 0; s < secs; s++)
                    {
                        Thread.Sleep(1000);
                        c.Tick();
                        Console.WriteLine($"  {s + 1,2}s mode {c.Mode()} coolant {c.CoolantTemp?.ToString("0.0") ?? "?"} °C, pump {c.PumpDuty}%, fans {c.FanDutyNow}%, rpm {string.Join(", ", c.Rpm)}");
                    }
                }
                finally
                {
                    c.Release();
                    Console.WriteLine($"  released; mode now {c.Mode()}");
                }
            }
            return;
        }
        if (what == "msi")
        {
            var info = MsiMysticLight.Find();
            if (info == null) { Console.WriteLine("No Mystic Light controller found."); return; }
            using var d = MsiMysticLight.Open(info);
            var s = d.Original;
            static string Z(ReadOnlySpan<byte> s, int o) => $"mode {s[o]} colour {s[o + 1]},{s[o + 2]},{s[o + 3]} flags {s[o + 4]:X2}/{s[o + 8]:X2}";
            Console.WriteLine($"{info.Product.Trim()} {info.Pid:X4}, feature report {info.FeatureLength} bytes, read {s.Length}");
            Console.WriteLine($"  JRGB1     {Z(s, 1)}");
            Console.WriteLine($"  JRAINBOW1 {Z(s, 31)} leds {s[41]}");
            Console.WriteLine($"  JRAINBOW2 {Z(s, 42)} leds {s[52]}");
            Console.WriteLine($"  on-board  {Z(s, 74)}");
            Console.WriteLine($"  JRGB2     {Z(s, 174)}  save byte {s[184]}");
            if (args.Length > 2 && args[2] == "restore") { d.Restore(); Console.WriteLine("Restored."); }
            else if (args.Length > 2 && args[2] == "off") { d.Off(); Console.WriteLine("Turned off."); }
            else if (args.Length > 4)
            {
                byte r = byte.Parse(args[2]), g = byte.Parse(args[3]), b = byte.Parse(args[4]);
                d.SetAll(r, g, b);
                Console.WriteLine($"Set every zone to {r},{g},{b}.");
            }
            return;
        }
        if (what == "nzxt-raw")
        {
            // Sends the two queries and prints the start of every report that comes back for 3 seconds.
            var info = NzxtSmartDevice.Find()!;
            using var h = HidDevice.Open(info);
            h.FlushInput();
            var until = DateTime.Now.AddSeconds(3);
            h.Write([0x10, 0x01]);
            h.Write([0x20, 0x03]);
            int n = 0;
            while (DateTime.Now < until)
            {
                var r = h.Read(500);
                if (r == null) { Console.WriteLine("  (no report for 500 ms)"); continue; }
                n++;
                Console.WriteLine($"  {n,3}: " + string.Join(' ', r.Take(24).Select(x => x.ToString("X2"))));
            }
            return;
        }
        if (what == "nzxt")
        {
            var info = NzxtSmartDevice.Find();
            if (info == null) { Console.WriteLine("No NZXT controller found."); return; }
            using var d = NzxtSmartDevice.Open(info);
            Console.WriteLine($"{info.Product} {info.Pid:X4}, firmware {d.Firmware}, {d.Channels.Count} lighting channels");
            for (int c = 0; c < d.Channels.Count; c++)
            {
                var list = d.Channels[c];
                Console.WriteLine($"  channel {c + 1}: {(list.Count == 0 ? "nothing connected" : string.Join(", ", list.Select(a => $"{a.Name} ({a.Leds} LEDs, id 0x{a.Id:X2})")))}  → {d.LedCount(c)} LEDs");
            }
            if (args.Length > 2 && args[2] == "off") { d.Off(); Console.WriteLine("Turned off."); }
            else if (args.Length > 4)
            {
                byte r = byte.Parse(args[2]), g = byte.Parse(args[3]), b = byte.Parse(args[4]);
                d.SetAll(r, g, b);
                Console.WriteLine($"Set every channel to {r},{g},{b}.");
            }
        }
    }
}
