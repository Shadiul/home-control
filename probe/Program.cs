using LedControl.Music;

if (args.Length > 0 && args[0] == "gree")
{
    await LedControl.GreeProbe.RunAsync(args);
    return;
}

// 8 s of synthetic music at 48 kHz: a kick drum every 128 BPM beat, hi-hats, and a bass line.
const int sr = 48000;
var rnd = new Random(1);
int n = sr * 8;
var audio = new float[n];
double beatLen = 60.0 / 128;
int kicks = 0;
for (int i = 0; i < n; i++)
{
    double t = i / (double)sr;
    double tb = t % beatLen;
    if (tb < 1.0 / sr) kicks++;
    double kick = tb < 0.15 ? Math.Sin(2 * Math.PI * (55 + 90 * Math.Exp(-tb * 30)) * tb) * Math.Exp(-tb * 18) : 0;
    double hat = (t % (beatLen / 2)) < 0.03 ? (rnd.NextDouble() * 2 - 1) * 0.15 : 0;
    double bass = 0.12 * Math.Sin(2 * Math.PI * 82 * t);
    double pad = 0.05 * Math.Sin(2 * Math.PI * 440 * t) + 0.04 * Math.Sin(2 * Math.PI * 660 * t);
    audio[i] = (float)(0.7 * kick + hat + bass + pad);
}
var an = new Analyzer(sr);
var st = new Styler();
int beats = 0, frames = 0;
double maxV = 0, maxBar = 0;
for (int off = 0; off + 1024 <= n; off += 1024)
{
    var f = an.Feed(audio.AsSpan(off, 1024), 55);
    var (h, s, v) = st.Colour("beat", f, 280);
    frames++;
    if (f.Beat) beats++;
    maxV = Math.Max(maxV, v);
    maxBar = Math.Max(maxBar, f.Bars.Max());
}
// Silence afterwards should go idle.
MusicFrame last = null!;
for (int i = 0; i < 40; i++) last = an.Feed(new float[1024], 55);
Console.WriteLine($"kicks in audio: {kicks}, beats detected: {beats}, frames: {frames}, peak flash V: {maxV:0.00}, max bar: {maxBar:0.00}, idle after silence: {last.Idle}");
