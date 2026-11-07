"""
Music sync: makes the light react to sound, like a music visualizer.

Captures what the PC is playing (WASAPI loopback of the default output device) or a
microphone, analyses loudness, frequency bands and beats, and streams colours to the
light through its "music" data point. Those updates are fire-and-forget (the light never
replies), so ~20 per second is fine. This needs the home-network connection; Tuya Cloud is
far too slow for this.

UI-agnostic: progress is reported through the on_frame / on_status callbacks, which are
called from the engine's own thread.
"""

import colorsys
import math
import select
import threading
import time
import warnings

import numpy as np
import tinytuya

from led_core import tcp_reachable

warnings.filterwarnings("ignore", module="soundcard")  # "data discontinuity" chatter

SAMPLE_RATE = 48000
BLOCK = 1024            # ~21 ms per analysis step
FFT_SIZE = 2048
BARS = 32               # bars for the on-screen visualizer
SYSTEM_SOURCE = "system"
STYLES = {
    "pulse": "Pulse",        # one colour, brightness follows the volume
    "spectrum": "Spectrum",  # bass = red, mids = green, treble = blue
    "beat": "Beat",          # new colour and a flash on every beat
    "party": "Party",        # colours spin with the energy, flash on beats
}
GOLDEN_ANGLE = 137.508


def list_sources():
    """[(id, label)] - system audio first, then microphones."""
    import soundcard as sc
    out = [(SYSTEM_SOURCE, "System audio (what's playing)")]
    for m in sc.all_microphones(include_loopback=False):
        out.append((m.name, f"Microphone: {m.name}"))
    return out


class Analyzer:
    """Turns audio blocks into smoothed, auto-levelled features."""

    def __init__(self):
        self.buf = np.zeros(FFT_SIZE, dtype=np.float32)
        self.window = np.hanning(FFT_SIZE).astype(np.float32)
        freqs = np.fft.rfftfreq(FFT_SIZE, 1 / SAMPLE_RATE)
        self.band_idx = {name: (freqs >= lo) & (freqs < hi) for name, (lo, hi) in
                         {"bass": (20, 160), "mid": (160, 2000), "treble": (2000, 12000)}.items()}
        edges = np.geomspace(40, 14000, BARS + 1)
        self.bar_idx = [(freqs >= edges[i]) & (freqs < edges[i + 1]) for i in range(BARS)]
        self.peaks = {"bass": 1e-6, "mid": 1e-6, "treble": 1e-6, "rms": 1e-6}
        self.bar_peak = np.full(BARS, 1e-6, dtype=np.float32)
        self.bars = np.zeros(BARS, dtype=np.float32)
        self.level = 0.0
        self.bands = {"bass": 0.0, "mid": 0.0, "treble": 0.0}
        self.bass_hist = []
        self.last_beat = -1.0
        self.silent_since = None
        self.t = 0.0  # audio time in seconds (not wall clock), so timing follows the sound

    def feed(self, block, sensitivity):
        """block: (n, channels) float array. sensitivity: 0..100. Returns features dict."""
        mono = block.mean(axis=1) if block.ndim == 2 else block
        n = len(mono)
        self.buf = np.roll(self.buf, -n)
        self.buf[-n:] = mono[-FFT_SIZE:]
        rms = float(np.sqrt(np.mean(mono ** 2))) if n else 0.0
        spec = np.abs(np.fft.rfft(self.buf * self.window)) ** 2

        gain = 0.6 + sensitivity / 50          # 0.6 .. 2.6
        self.t += n / SAMPLE_RATE
        now = self.t
        silent = rms < 2e-4
        if silent:
            self.silent_since = now if self.silent_since is None else self.silent_since
        else:
            self.silent_since = None

        # Auto-levelling: compare each value to a slowly decaying recent peak.
        def norm(key, value, decay=0.997):
            self.peaks[key] = max(value, self.peaks[key] * decay, 1e-6)
            return min(1.0, (value / self.peaks[key]) * gain) if not silent else 0.0

        raw = {k: float(spec[idx].sum()) for k, idx in self.band_idx.items()}
        targets = {k: norm(k, math.sqrt(v)) for k, v in raw.items()}
        level_t = norm("rms", rms)

        # Fast attack, slower release, so flashes are snappy but not flickery.
        def smooth(cur, tgt, up=0.65, down=0.18):
            return cur + (tgt - cur) * (up if tgt > cur else down)

        self.level = smooth(self.level, level_t)
        for k in self.bands:
            self.bands[k] = smooth(self.bands[k], targets[k])

        bars_raw = np.array([math.sqrt(spec[idx].sum()) if idx.any() else 0.0 for idx in self.bar_idx],
                            dtype=np.float32)
        self.bar_peak = np.maximum(bars_raw, self.bar_peak * 0.995) + 1e-9
        bar_gain = 0.75 + sensitivity / 200    # gentler than the light's gain, so bars don't all max out
        bars_t = np.clip(bars_raw / self.bar_peak * bar_gain, 0, 1) if not silent else np.zeros(BARS)
        self.bars = np.where(bars_t > self.bars, self.bars + (bars_t - self.bars) * 0.7,
                             self.bars + (bars_t - self.bars) * 0.2)

        # Beat: bass clearly above its average over the last second.
        bass = math.sqrt(raw["bass"])
        self.bass_hist.append(bass)
        if len(self.bass_hist) > 47:
            self.bass_hist.pop(0)
        avg = sum(self.bass_hist) / len(self.bass_hist)
        threshold = 1.65 - sensitivity / 250  # 1.65 (strict) .. 1.25 (eager)
        beat = (not silent and len(self.bass_hist) > 10 and bass > avg * threshold
                and now - self.last_beat > 0.22)
        if beat:
            self.last_beat = now

        return {"level": self.level, "bands": dict(self.bands), "bars": self.bars.tolist(),
                "beat": beat, "silent": silent,
                "idle": self.silent_since is not None and now - self.silent_since > 0.6}


class Styler:
    """Maps features to an HSV colour (h 0-360, s 0-1, v 0-1) for each style."""

    def __init__(self):
        self.hue = 0.0
        self.flash = 0.0

    def colour(self, style, f, base_hue):
        self.flash *= 0.82  # fade first, so a beat in this frame shows at full strength
        if f["beat"]:
            self.flash = 1.0
            if style in ("beat", "party"):
                self.hue = (self.hue + GOLDEN_ANGLE) % 360
        lvl = f["level"]

        if style == "pulse":
            return base_hue, 1.0, 0.04 + 0.96 * lvl
        if style == "spectrum":
            b, m, t = f["bands"]["bass"], f["bands"]["mid"], f["bands"]["treble"]
            r, g, bl = b, m * 0.85, t
            mx = max(r, g, bl, 1e-6)
            h, s, _ = colorsys.rgb_to_hsv(r / mx, g / mx, bl / mx)
            return h * 360, max(0.55, s), 0.04 + 0.96 * max(b, m, t)
        if style == "beat":
            return self.hue, 1.0, 0.08 + 0.92 * max(self.flash, lvl * 0.45)
        # party
        self.hue = (self.hue + 0.6 + 5.0 * lvl) % 360
        return self.hue, 1.0, 0.06 + 0.94 * max(self.flash, lvl * 0.7)


class LightStream:
    """Persistent local connection that streams music-mode colours to the light."""

    def __init__(self, dev):
        self.dev = dev
        self.bulb = None
        self.last_drain = 0.0

    def open(self):
        d = self.dev
        if not d.get("key"):
            raise RuntimeError("Music sync needs the light's local key (see Settings).")
        if not tcp_reachable(d["ip"]):
            raise RuntimeError(f"Can't reach the light at {d['ip']} on your home network. "
                               "Music sync only works over the home network.")
        b = tinytuya.BulbDevice(d["id"], d["ip"], d["key"], version=float(d["version"]))
        b.set_socketTimeout(2)
        b.set_socketPersistent(True)
        b.set_socketNODELAY(True)
        st = b.status()
        if not st or "Error" in st:
            raise RuntimeError(f"The light didn't answer: {(st or {}).get('Error', 'no reply')}")
        if not b.bulb_has_capability("music"):
            raise RuntimeError("This light doesn't support music mode.")
        b.set_mode("music")
        self.bulb = b

    def send(self, h, s, v):
        r, g, bl = (int(c * 255) for c in colorsys.hsv_to_rgb((h % 360) / 360, s, max(0.01, v)))
        res = self.bulb.set_music_colour(0, r, g, bl, nowait=True)
        if isinstance(res, dict) and "Error" in res:
            raise OSError(res.get("Error"))
        self._drain()

    def _drain(self):
        """The light sends status frames we never read; discard them so the socket stays healthy."""
        now = time.monotonic()
        sock = getattr(self.bulb, "socket", None)
        if sock is None or now - self.last_drain < 1.0:
            return
        self.last_drain = now
        try:
            while select.select([sock], [], [], 0)[0]:
                if not sock.recv(65536):
                    break
        except OSError:
            pass

    def close(self):
        try:
            if self.bulb:
                self.bulb.set_socketPersistent(False)
                self.bulb.close()
        except Exception:
            pass
        self.bulb = None


class MusicSync:
    def __init__(self, get_device, on_frame=None, on_status=None):
        """get_device() -> device config dict; callbacks are called from the engine thread."""
        self.get_device = get_device
        self.on_frame = on_frame or (lambda f: None)
        self.on_status = on_status or (lambda kind, text: None)
        self.style = "spectrum"
        self.sensitivity = 50
        self.hue = 280
        self.source = SYSTEM_SOURCE
        self.fps = 20
        self._thread = None
        self._stop = threading.Event()

    @property
    def running(self):
        return bool(self._thread and self._thread.is_alive())

    def start(self):
        if self.running:
            return
        self._stop.clear()
        self._thread = threading.Thread(target=self._run, name="music-sync", daemon=True)
        self._thread.start()

    def stop(self, wait=True):
        self._stop.set()
        if wait and self._thread and self._thread is not threading.current_thread():
            self._thread.join(timeout=3)

    # ---- engine thread

    def _open_source(self):
        import soundcard as sc
        if self.source == SYSTEM_SOURCE:
            spk = sc.default_speaker()
            return sc.get_microphone(id=str(spk.name), include_loopback=True), spk.name
        return sc.get_microphone(id=self.source, include_loopback=False), self.source

    def _run(self):
        stream = LightStream(self.get_device())
        try:
            self.on_status("busy", "Connecting to the light…")
            stream.open()
        except Exception as e:
            stream.close()
            self.on_status("error", str(e))
            self.on_status("stopped", "Stopped")
            return

        analyzer, styler = Analyzer(), Styler()
        last_send, last_colour, reconnect_at, next_send = 0.0, None, 0.0, 0.0
        try:
            import soundcard as sc
            while not self._stop.is_set():
                mic, name = self._open_source()
                source_key = (self.source, name)
                self.on_status("ok", f"Listening to {name}")
                last_check = time.monotonic()
                with mic.recorder(samplerate=SAMPLE_RATE, channels=2, blocksize=512) as rec:
                    while not self._stop.is_set():
                        block = rec.record(numframes=BLOCK)
                        f = analyzer.feed(block, self.sensitivity)
                        h, s, v = styler.colour(self.style, f, self.hue)
                        if f["idle"]:
                            v = 0.03  # quiet: low glow instead of black
                        f["colour"] = (h, s, v)
                        self.on_frame(f)

                        now = time.monotonic()
                        if stream.bulb is None and now >= reconnect_at:
                            try:
                                stream.open()
                                self.on_status("ok", f"Listening to {name}")
                            except Exception as e:
                                reconnect_at = now + 3
                                self.on_status("error", f"Lost the light, retrying… ({e})")
                        # Steady schedule: audio blocks arrive every ~21 ms, so "time since last
                        # send" would round 20/s down to ~15/s. Advancing a deadline keeps the average.
                        if stream.bulb is not None and now >= next_send:
                            next_send = max(next_send + 1 / self.fps, now - 1 / self.fps)
                            colour = (round(h), round(s, 2), round(v, 2))
                            # Skip identical frames, but keep the connection alive.
                            if colour != last_colour or now - last_send > 1.0:
                                try:
                                    stream.send(h, s, v)
                                    last_colour = colour
                                    last_send = now
                                except Exception:
                                    stream.close()
                                    reconnect_at = now + 1

                        # Follow changes of source or default output device.
                        if now - last_check > 2:
                            last_check = now
                            cur = sc.default_speaker().name if self.source == SYSTEM_SOURCE else self.source
                            if (self.source, cur) != source_key:
                                break
        except Exception as e:
            self.on_status("error", f"Audio capture stopped: {e}")
        finally:
            stream.close()
            self.on_status("stopped", "Stopped")
