namespace HomeControl.Music;

/// <summary>Features of one audio block, plus the colour the engine picked for it.</summary>
public sealed class MusicFrame
{
    public double Level, Bass, Mid, Treble;
    public float[] Bars = [];
    public bool Beat, Silent, Idle;
    public double H, S, V; // h 0-360, s and v 0-1
}

/// <summary>In-place radix-2 FFT.</summary>
sealed class Fft
{
    readonly int n;
    readonly int[] rev;
    readonly double[] cos, sin;

    public Fft(int size)
    {
        n = size;
        int bits = (int)Math.Log2(size);
        rev = new int[size];
        for (int i = 0; i < size; i++)
        {
            int r = 0;
            for (int b = 0; b < bits; b++) r |= ((i >> b) & 1) << (bits - 1 - b);
            rev[i] = r;
        }
        cos = new double[size / 2];
        sin = new double[size / 2];
        for (int i = 0; i < size / 2; i++)
        {
            cos[i] = Math.Cos(-2 * Math.PI * i / size);
            sin[i] = Math.Sin(-2 * Math.PI * i / size);
        }
    }

    public void Transform(double[] re, double[] im)
    {
        for (int i = 0; i < n; i++)
        {
            int j = rev[i];
            if (j > i)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            int half = len / 2, step = n / len;
            for (int i = 0; i < n; i += len)
                for (int k = 0; k < half; k++)
                {
                    double wr = cos[k * step], wi = sin[k * step];
                    int a = i + k, b = a + half;
                    double xr = re[b] * wr - im[b] * wi, xi = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                }
        }
    }
}

/// <summary>
/// Turns audio into smoothed, auto-levelled features: loudness, bass/mid/treble, 32 spectrum
/// bars and beats. Timing follows the audio itself, not the wall clock.
/// </summary>
public sealed class Analyzer
{
    public const int FftSize = 2048, BarCount = 32;

    readonly int sampleRate;
    readonly Fft fft = new(FftSize);
    readonly double[] buf = new double[FftSize], window = new double[FftSize];
    readonly double[] re = new double[FftSize], im = new double[FftSize], spec = new double[FftSize / 2 + 1];
    readonly (int Lo, int Hi)[] bandBins = new (int, int)[3]; // bass, mid, treble
    readonly (int Lo, int Hi)[] barBins = new (int, int)[BarCount];
    readonly double[] peaks = [1e-6, 1e-6, 1e-6, 1e-6]; // bass, mid, treble, rms
    readonly double[] barPeak = new double[BarCount];
    readonly float[] bars = new float[BarCount];
    readonly double[] bands = new double[3];
    readonly Queue<double> bassHist = new();
    double level, t, lastBeat = -1, silentSince = -1;

    public int SampleRate => sampleRate;

    public Analyzer(int sampleRate)
    {
        this.sampleRate = sampleRate;
        for (int i = 0; i < FftSize; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));
        Array.Fill(barPeak, 1e-6);
        double binHz = sampleRate / (double)FftSize;
        (int, int) Bins(double lo, double hi) =>
            ((int)Math.Ceiling(lo / binHz), Math.Min(FftSize / 2, (int)Math.Ceiling(hi / binHz) - 1));
        bandBins[0] = Bins(20, 160);
        bandBins[1] = Bins(160, 2000);
        bandBins[2] = Bins(2000, 12000);
        for (int i = 0; i < BarCount; i++)
        {
            double lo = 40 * Math.Pow(14000 / 40.0, i / (double)BarCount);
            double hi = 40 * Math.Pow(14000 / 40.0, (i + 1) / (double)BarCount);
            barBins[i] = Bins(lo, hi);
        }
    }

    double Sum(int lo, int hi)
    {
        double s = 0;
        for (int k = lo; k <= hi; k++) s += spec[k];
        return s;
    }

    /// <param name="mono">audio samples, -1..1</param>
    /// <param name="sensitivity">0..100</param>
    public MusicFrame Feed(ReadOnlySpan<float> mono, int sensitivity)
    {
        int n = mono.Length;
        if (n >= FftSize)
        {
            for (int i = 0; i < FftSize; i++) buf[i] = mono[n - FftSize + i];
        }
        else
        {
            Array.Copy(buf, n, buf, 0, FftSize - n);
            for (int i = 0; i < n; i++) buf[FftSize - n + i] = mono[i];
        }
        double sq = 0;
        foreach (var x in mono) sq += x * x;
        double rms = n > 0 ? Math.Sqrt(sq / n) : 0;

        for (int i = 0; i < FftSize; i++)
        {
            re[i] = buf[i] * window[i];
            im[i] = 0;
        }
        fft.Transform(re, im);
        for (int k = 0; k <= FftSize / 2; k++) spec[k] = re[k] * re[k] + im[k] * im[k];

        double gain = 0.6 + sensitivity / 50.0; // 0.6 .. 2.6
        t += n / (double)sampleRate;
        bool silent = rms < 2e-4;
        if (silent) { if (silentSince < 0) silentSince = t; }
        else silentSince = -1;

        // Auto-levelling: each value relative to a slowly decaying recent peak.
        double Norm(int key, double value)
        {
            peaks[key] = Math.Max(Math.Max(value, peaks[key] * 0.997), 1e-6);
            return silent ? 0 : Math.Min(1, value / peaks[key] * gain);
        }
        static double Smooth(double cur, double tgt) => cur + (tgt - cur) * (tgt > cur ? 0.65 : 0.18);

        var raw = new double[3];
        for (int b = 0; b < 3; b++) raw[b] = Sum(bandBins[b].Lo, bandBins[b].Hi);
        for (int b = 0; b < 3; b++) bands[b] = Smooth(bands[b], Norm(b, Math.Sqrt(raw[b])));
        level = Smooth(level, Norm(3, rms));

        double barGain = 0.75 + sensitivity / 200.0; // gentler, so the bars don't all max out
        for (int i = 0; i < BarCount; i++)
        {
            var (lo, hi) = barBins[i];
            double v = hi >= lo ? Math.Sqrt(Sum(lo, hi)) : 0;
            barPeak[i] = Math.Max(v, barPeak[i] * 0.995) + 1e-9;
            double target = silent ? 0 : Math.Clamp(v / barPeak[i] * barGain, 0, 1);
            bars[i] += (float)((target - bars[i]) * (target > bars[i] ? 0.7 : 0.2));
        }

        // Beat: bass clearly above its average over the last second.
        double bass = Math.Sqrt(raw[0]);
        bassHist.Enqueue(bass);
        while (bassHist.Count > 47) bassHist.Dequeue();
        double avg = bassHist.Average();
        double threshold = 1.65 - sensitivity / 250.0; // 1.65 (strict) .. 1.25 (eager)
        bool beat = !silent && bassHist.Count > 10 && bass > avg * threshold && t - lastBeat > 0.22;
        if (beat) lastBeat = t;

        return new MusicFrame
        {
            Level = level, Bass = bands[0], Mid = bands[1], Treble = bands[2],
            Bars = (float[])bars.Clone(), Beat = beat, Silent = silent,
            Idle = silentSince >= 0 && t - silentSince > 0.6,
        };
    }
}

/// <summary>Maps features to a colour for each style.</summary>
public sealed class Styler
{
    const double GoldenAngle = 137.508;
    double hue, flash;

    public (double H, double S, double V) Colour(string style, MusicFrame f, double baseHue)
    {
        flash *= 0.82; // fade first, so a beat in this frame shows at full strength
        if (f.Beat)
        {
            flash = 1;
            if (style is "beat" or "party") hue = (hue + GoldenAngle) % 360;
        }
        double lvl = f.Level;
        switch (style)
        {
            case "pulse":
                return (baseHue, 1, 0.04 + 0.96 * lvl);
            case "spectrum":
            {
                double r = f.Bass, g = f.Mid * 0.85, b = f.Treble;
                double mx = Math.Max(Math.Max(r, g), Math.Max(b, 1e-6));
                var (h, s) = HueSat(r / mx, g / mx, b / mx);
                return (h, Math.Max(0.55, s), 0.04 + 0.96 * Math.Max(f.Bass, Math.Max(f.Mid, f.Treble)));
            }
            case "beat":
                return (hue, 1, 0.08 + 0.92 * Math.Max(flash, lvl * 0.45));
            default: // party
                hue = (hue + 0.6 + 5.0 * lvl) % 360;
                return (hue, 1, 0.06 + 0.94 * Math.Max(flash, lvl * 0.7));
        }
    }

    static (double H, double S) HueSat(double r, double g, double b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
        if (max <= 0 || d <= 0) return (0, 0);
        double h = max == r ? ((g - b) / d) % 6 : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        h *= 60;
        if (h < 0) h += 360;
        return (h, d / max);
    }
}
