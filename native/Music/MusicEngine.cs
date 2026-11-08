using System.Diagnostics;
using HomeControl.Core;

namespace HomeControl.Music;

/// <summary>
/// Music sync: listens to audio, analyses it and streams colours to the light through its
/// "music" data point (fire-and-forget, ~20 updates a second, home network only).
/// Runs on its own thread; <see cref="Frame"/> and <see cref="Status"/> are raised there.
/// </summary>
public sealed class MusicEngine
{
    readonly LightController controller;
    Thread? thread;
    volatile bool stopRequested;

    public volatile string Style = "spectrum";
    public volatile string Source = MusicPrefs.SystemSource;
    public volatile int Sensitivity = 55;
    public volatile int Hue = 280;
    public volatile int Fps = 20;

    public event Action<MusicFrame>? Frame;
    /// <summary>kind: busy | ok | error | stopped</summary>
    public event Action<string, string>? Status;

    /// <summary>True once colours have actually been sent (so stopping should restore the light).</summary>
    public bool Streamed { get; private set; }

    public MusicEngine(LightController controller) => this.controller = controller;

    public bool Running => thread is { IsAlive: true };

    public void Start()
    {
        if (Running) return;
        stopRequested = false;
        Streamed = false;
        thread = new Thread(Run) { IsBackground = true, Name = "music-sync" };
        thread.Start();
    }

    public void Stop(bool wait = true)
    {
        stopRequested = true;
        if (wait && thread != null && thread != Thread.CurrentThread) thread.Join(3000);
    }

    /// <summary>Identifies what is being captured; for system audio that includes the current default output.</summary>
    static string SourceKey(string source, string? deviceId) =>
        source == MusicPrefs.SystemSource ? source + "|" + (deviceId ?? AudioCapture.CurrentDeviceId(source)) : source;

    void Say(string kind, string text)
    {
        try { Status?.Invoke(kind, text); } catch { }
    }

    void Run()
    {
        try
        {
            Say("busy", "Connecting to the light…");
            controller.MusicBeginAsync().GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            Say("error", LightController.Friendly(e));
            Say("stopped", "Stopped");
            return;
        }

        var clock = Stopwatch.StartNew();
        var styler = new Styler();
        Analyzer? analyzer = null;
        bool linkOk = true;
        double reconnectAt = 0, nextSend = 0, lastSend = -10;
        (int, double, double)? lastColour = null;
        var silence = new float[AudioCapture.BlockSize * 6]; // ~130 ms

        try
        {
            while (!stopRequested)
            {
                AudioCapture cap;
                try
                {
                    cap = new AudioCapture(Source);
                    cap.Start();
                }
                catch (Exception e)
                {
                    Say("error", "Can't listen to that audio source: " + LightController.Friendly(e));
                    for (int i = 0; i < 20 && !stopRequested; i++) Thread.Sleep(100);
                    continue;
                }
                string sourceKey = SourceKey(Source, cap.DeviceId);
                if (analyzer == null || analyzer.SampleRate != cap.SampleRate) analyzer = new Analyzer(cap.SampleRate);
                Say("ok", "Listening to " + cap.Name);
                double lastCheck = clock.Elapsed.TotalSeconds;

                using (cap)
                {
                    while (!stopRequested)
                    {
                        // Loopback capture sends nothing while nothing plays: treat a gap as silence.
                        var block = cap.Take(150) ?? silence;
                        if (cap.Failed) break;
                        var f = analyzer.Feed(block, Sensitivity);
                        var (h, s, v) = styler.Colour(Style, f, Hue);
                        if (f.Idle) v = 0.03; // quiet: a low glow instead of black
                        f.H = h; f.S = s; f.V = v;
                        try { Frame?.Invoke(f); } catch { }

                        double now = clock.Elapsed.TotalSeconds;
                        if (!linkOk && now >= reconnectAt)
                        {
                            try
                            {
                                controller.MusicBeginAsync().GetAwaiter().GetResult();
                                linkOk = true;
                                Say("ok", "Listening to " + cap.Name);
                            }
                            catch (Exception e)
                            {
                                reconnectAt = now + 3;
                                Say("error", "Lost the light, retrying… (" + LightController.Friendly(e) + ")");
                            }
                        }
                        // Steady schedule: blocks arrive in bursts, so advance a deadline rather
                        // than measuring time since the last send (keeps the average rate).
                        int fps = Math.Clamp(Fps, 5, 30);
                        if (linkOk && now >= nextSend)
                        {
                            nextSend = Math.Max(nextSend + 1.0 / fps, now - 1.0 / fps);
                            var colour = ((int)Math.Round(h), Math.Round(s, 2), Math.Round(v, 2));
                            if (colour != lastColour || now - lastSend > 1.0) // skip repeats, keep alive
                            {
                                try
                                {
                                    controller.MusicSend(h, s, v);
                                    Streamed = true;
                                    lastColour = colour;
                                    lastSend = now;
                                }
                                catch (Exception)
                                {
                                    linkOk = false;
                                    reconnectAt = now + 1;
                                }
                            }
                        }

                        // Follow a change of source, or of the default output device.
                        if (now - lastCheck > 2)
                        {
                            lastCheck = now;
                            if (SourceKey(Source, null) != sourceKey) break;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Say("error", "Music sync stopped: " + LightController.Friendly(e));
        }
        finally
        {
            Say("stopped", "Stopped");
        }
    }
}
