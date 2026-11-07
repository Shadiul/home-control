using System.Diagnostics;
using LedControl.Core;

namespace LedControl;

/// <summary>probe gree [--save]: find and pair Gree units, read them, blink the first one's display light.</summary>
static class GreeProbe
{
    public static async Task RunAsync(string[] args)
    {
        var sw = Stopwatch.StartNew();
        var cfg = AppConfig.Load();
        var r = await Gree.DiscoverAsync(cfg.Acs);
        Console.WriteLine($"discover: {r.Acs.Count} units, added {r.Added}, moved {r.Moved}, failed [{string.Join(",", r.Failed)}] in {sw.ElapsedMilliseconds} ms");
        foreach (var ac in r.Acs)
        {
            var client = new GreeClient(ac);
            sw.Restart();
            var st = await client.StatusAsync();
            Console.WriteLine($"  {ac.Name} {ac.Ip} {ac.Mac} {(ac.Gcm ? "gcm" : "ecb")} status {sw.ElapsedMilliseconds} ms: " +
                              $"{st.Describe()}, room {st.RoomTemp} °C, fan {AcState.FanNames[st.FanSpeed]}, light {st.Get("Lig")}");
        }
        if (r.Acs.Count > 0)
        {
            var c = new GreeClient(r.Acs[0]);
            int lig = (await c.StatusAsync()).Get("Lig");
            sw.Restart();
            await c.SetAsync(new Dictionary<string, int> { ["Lig"] = lig == 0 ? 1 : 0 });
            Console.WriteLine($"  set Lig={(lig == 0 ? 1 : 0)} in {sw.ElapsedMilliseconds} ms, read back {(await c.StatusAsync()).Get("Lig")}");
            await Task.Delay(2000);
            await c.SetAsync(new Dictionary<string, int> { ["Lig"] = lig });
            Console.WriteLine($"  restored Lig={lig}, read back {(await c.StatusAsync()).Get("Lig")}");

            // A wrong key should re-pair by itself.
            var broken = r.Acs[0].Clone();
            broken.Key = "0000000000000000";
            var bc = new GreeClient(broken);
            bool rekeyed = false;
            bc.KeyChanged += () => rekeyed = true;
            await bc.StatusAsync();
            Console.WriteLine($"  wrong key recovered: {rekeyed && broken.Key == r.Acs[0].Key}");
        }
        if (args.Contains("--save"))
        {
            cfg.Acs = r.Acs;
            cfg.Save();
            Console.WriteLine("saved to " + AppInfo.ConfigPath);
        }
    }
}
