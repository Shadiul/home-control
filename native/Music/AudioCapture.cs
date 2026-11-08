using System.Collections.Concurrent;
using HomeControl.Core;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace HomeControl.Music;

/// <summary>
/// Captures audio with WASAPI: a loopback of the default speakers ("what's playing"),
/// or a microphone. Delivers mono blocks of <see cref="BlockSize"/> samples.
/// </summary>
public sealed class AudioCapture : IDisposable
{
    public const int BlockSize = 1024;

    public sealed record Source(string Id, string Label, string Name);

    readonly WasapiCapture capture;
    readonly BlockingCollection<float[]> blocks = new(64);
    readonly float[] pending = new float[BlockSize];
    int pendingCount;
    volatile bool failed;

    public string Name { get; }
    public string DeviceId { get; }
    public int SampleRate => capture.WaveFormat.SampleRate;
    public bool Failed => failed;

    /// <summary>System audio first, then active inputs (microphones, Stereo Mix, line in).</summary>
    public static List<Source> ListSources()
    {
        var list = new List<Source> { new(MusicPrefs.SystemSource, "System audio (what's playing)", "") };
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                list.Add(new(d.ID, "Input: " + d.FriendlyName, d.FriendlyName));
                d.Dispose();
            }
        }
        catch (Exception)
        {
            // No audio service: system audio is still listed, and fails with a clear message when started.
        }
        return list;
    }

    /// <summary>ID of the device a source maps to right now (the default output can change).</summary>
    public static string CurrentDeviceId(string source)
    {
        try
        {
            using var en = new MMDeviceEnumerator();
            if (source == MusicPrefs.SystemSource)
            {
                using var d = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return d.ID;
            }
            return source;
        }
        catch (Exception)
        {
            return "";
        }
    }

    public AudioCapture(string source)
    {
        var en = new MMDeviceEnumerator();
        try
        {
            MMDevice dev;
            if (source == MusicPrefs.SystemSource)
            {
                dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                capture = new WasapiLoopbackCapture(dev);
            }
            else
            {
                // Saved by ID; older versions saved the device name, so accept that too.
                dev = en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                        .FirstOrDefault(d => d.ID == source || d.FriendlyName == source)
                      ?? throw new LedException("That microphone isn't connected any more. Pick another source.");
                capture = new WasapiCapture(dev, true, 20);
            }
            Name = dev.FriendlyName;
            DeviceId = dev.ID;
        }
        catch (Exception e) when (e is not LedException)
        {
            en.Dispose();
            throw new LedException("No audio device to listen to (" + e.Message + ").");
        }
        en.Dispose();
        capture.DataAvailable += OnData;
        capture.RecordingStopped += (_, e) =>
        {
            failed = true;
            blocks.TryAdd([]);
        };
    }

    public void Start() => capture.StartRecording();

    void OnData(object? sender, WaveInEventArgs e)
    {
        var fmt = capture.WaveFormat;
        int channels = Math.Max(1, fmt.Channels);
        int bytesPerSample = fmt.BitsPerSample / 8;
        int frameBytes = bytesPerSample * channels;
        bool isFloat = fmt.Encoding == WaveFormatEncoding.IeeeFloat ||
                       (fmt is WaveFormatExtensible x && x.SubFormat == NAudio.Dmo.AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);
        var span = e.Buffer.AsSpan(0, e.BytesRecorded);
        for (int off = 0; off + frameBytes <= span.Length; off += frameBytes)
        {
            double sum = 0;
            for (int c = 0; c < channels; c++)
            {
                var s = span.Slice(off + c * bytesPerSample, bytesPerSample);
                sum += bytesPerSample switch
                {
                    4 when isFloat => BitConverter.ToSingle(s),
                    4 => BitConverter.ToInt32(s) / 2147483648.0,
                    3 => ((s[0] << 8) | (s[1] << 16) | (s[2] << 24)) / 2147483648.0,
                    2 => BitConverter.ToInt16(s) / 32768.0,
                    _ => 0,
                };
            }
            pending[pendingCount++] = (float)(sum / channels);
            if (pendingCount == BlockSize)
            {
                blocks.TryAdd((float[])pending.Clone()); // drops audio if the engine falls behind
                pendingCount = 0;
            }
        }
    }

    /// <summary>Next block, or null after <paramref name="timeoutMs"/> without audio (silence:
    /// loopback capture delivers nothing while nothing is playing).</summary>
    public float[]? Take(int timeoutMs) => blocks.TryTake(out var b, timeoutMs) ? b : null;

    public void Dispose()
    {
        try { capture.StopRecording(); } catch { }
        capture.Dispose();
    }
}
