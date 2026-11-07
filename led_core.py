"""
Shared LED logic for the LED Control desktop app and the browser panel.

Talks to a Tuya light either directly over the LAN (needs the device's local key)
or through the Tuya Cloud API (needs a Tuya IoT developer project).

Settings live in %APPDATA%\\LED Control\\config.json so every front end shares them.
"""

import json
import os
import shutil
import socket
import threading
import time
from pathlib import Path

import tinytuya

APP_NAME = "LED Control"
APP_VERSION = "1.1.0"

CONFIG_DIR = Path(os.environ.get("APPDATA") or Path.home()) / APP_NAME
CONFIG_PATH = CONFIG_DIR / "config.json"
# Where the browser-panel version kept its settings before they moved to %APPDATA%.
LEGACY_CONFIG_PATH = Path(__file__).resolve().parent / "config.json"

DEFAULT_CONFIG = {
    "connection": "auto",  # auto | local | cloud
    "device": {
        "id": "bff4467c8027f6519efwb0",
        "ip": "192.168.0.167",
        "version": "3.5",
        "key": "",
        "name": "",
    },
    "cloud": {"region": "eu", "api_key": "", "api_secret": ""},
    "app": {"theme": "auto", "close_to_tray": True},
}

SECRET_FIELDS = {("device", "key"), ("cloud", "api_key"), ("cloud", "api_secret")}


# ---------------------------------------------------------------- config

def load_config():
    cfg = json.loads(json.dumps(DEFAULT_CONFIG))
    if not CONFIG_PATH.exists() and LEGACY_CONFIG_PATH.exists():
        # One-time move of the browser panel's settings into the shared location.
        CONFIG_DIR.mkdir(parents=True, exist_ok=True)
        shutil.copy2(LEGACY_CONFIG_PATH, CONFIG_PATH)
    if CONFIG_PATH.exists():
        try:
            saved = json.loads(CONFIG_PATH.read_text(encoding="utf-8"))
            for section, value in saved.items():
                if isinstance(value, dict) and isinstance(cfg.get(section), dict):
                    cfg[section].update(value)
                else:
                    cfg[section] = value
        except (OSError, ValueError) as e:
            print(f"Could not read config.json ({e}); using defaults.")
    return cfg


def save_config(cfg):
    CONFIG_DIR.mkdir(parents=True, exist_ok=True)
    tmp = CONFIG_PATH.with_suffix(".tmp")
    tmp.write_text(json.dumps(cfg, indent=2), encoding="utf-8")
    os.replace(tmp, CONFIG_PATH)  # atomic, so a crash never leaves half a file


def public_config(cfg):
    """Config for the browser, with secrets replaced by a 'set' flag."""
    out = json.loads(json.dumps(cfg))
    for section, field in SECRET_FIELDS:
        out[section][field + "_set"] = bool(cfg[section].get(field))
        out[section][field] = ""
    return out


# ---------------------------------------------------------------- helpers

def clamp(v, lo, hi):
    return max(lo, min(hi, v))


def tcp_reachable(ip, port=6668, timeout=1.5):
    try:
        with socket.create_connection((ip, port), timeout=timeout):
            return True
    except OSError:
        return False


class LedError(Exception):
    pass


def check(result, what):
    if isinstance(result, dict) and "Error" in result:
        raise LedError(f"{what}: {result.get('Error')} ({result.get('Payload') or result.get('Err')})")
    return result


# ---------------------------------------------------------------- local (LAN) backend

class LocalBackend:
    name = "local"

    def __init__(self, dev):
        self.dev = dev
        if not tcp_reachable(dev["ip"]):
            raise LedError(f"Can't reach the LED at {dev['ip']} on your network "
                           "(your router may be blocking device-to-device traffic).")
        self.bulb = tinytuya.BulbDevice(
            dev_id=dev["id"], address=dev["ip"], local_key=dev["key"], version=float(dev["version"])
        )
        self.bulb.set_socketTimeout(3)
        self.bulb.set_socketRetryLimit(1)

    def state(self):
        st = check(self.bulb.state(), "Reading state")
        caps = {
            "colour": bool(self.bulb.dpset.get("colour")),
            "temp": bool(self.bulb.dpset.get("colourtemp")),
        }
        mode = st.get("mode") or "white"
        out = {"on": bool(st.get("is_on")), "mode": mode, "caps": caps,
               "brightness": 100, "temp": 50, "h": 0, "s": 100, "v": 100}
        try:
            if st.get("brightness") is not None:
                out["brightness"] = round(self.bulb.get_brightness_percentage(state=st))
            if st.get("colourtemp") is not None:
                out["temp"] = round(self.bulb.get_colourtemp_percentage(state=st))
            if st.get("colour"):
                h, s, v = self.bulb.colour_hsv(state=st)
                out.update(h=round(h * 360), s=round(s * 100), v=round(v * 100))
        except (RuntimeError, ValueError, TypeError):
            pass
        return out

    def power(self, on):
        check(self.bulb.turn_on() if on else self.bulb.turn_off(), "Power")

    def white(self, brightness, temp):
        check(self.bulb.set_white_percentage(clamp(brightness, 1, 100), clamp(temp, 0, 100)), "White")

    def colour(self, h, s, v):
        check(self.bulb.set_hsv(clamp(h, 0, 360) / 360, clamp(s, 0, 100) / 100, clamp(v, 1, 100) / 100), "Colour")


# ---------------------------------------------------------------- cloud backend

class CloudBackend:
    name = "cloud"

    def __init__(self, dev, cloud_cfg):
        self.dev_id = dev["id"]
        self.cloud = tinytuya.Cloud(
            apiRegion=cloud_cfg["region"], apiKey=cloud_cfg["api_key"],
            apiSecret=cloud_cfg["api_secret"], apiDeviceID=dev["id"],
        )
        if getattr(self.cloud, "error", None):
            raise LedError(f"Tuya Cloud login failed: {self.cloud.error.get('Payload') or self.cloud.error}")
        self.codes = {}

    def _status(self):
        res = check(self.cloud.getstatus(self.dev_id), "Cloud status")
        return {item["code"]: item["value"] for item in res.get("result", [])}

    def _send(self, commands):
        check(self.cloud.sendcommand(self.dev_id, {"commands": commands}), "Cloud command")

    def _pick(self, *names):
        for n in names:
            if n in self.codes:
                return n
        return None

    def state(self):
        self.codes = self._status()
        sw = self._pick("switch_led", "switch_1", "switch")
        bright = self._pick("bright_value_v2", "bright_value")
        temp = self._pick("temp_value_v2", "temp_value")
        col = self._pick("colour_data_v2", "colour_data")
        big = bright and bright.endswith("_v2")
        bmax = 1000 if big else 255
        out = {
            "on": bool(self.codes.get(sw)), "mode": self.codes.get("work_mode", "white"),
            "caps": {"colour": bool(col), "temp": bool(temp)},
            "brightness": 100, "temp": 50, "h": 0, "s": 100, "v": 100,
        }
        if bright:
            out["brightness"] = round(self.codes[bright] / bmax * 100)
        if temp:
            out["temp"] = round(self.codes[temp] / bmax * 100)
        if col:
            c = self.codes[col]
            if isinstance(c, str):
                try:
                    c = json.loads(c)
                except ValueError:
                    c = {}
            cmax = 1000 if col.endswith("_v2") else 255
            out.update(h=c.get("h", 0), s=round(c.get("s", cmax) / cmax * 100), v=round(c.get("v", cmax) / cmax * 100))
        return out

    def _ensure_codes(self):
        if not self.codes:
            self.codes = self._status()

    def power(self, on):
        self._ensure_codes()
        self._send([{"code": self._pick("switch_led", "switch_1", "switch") or "switch_led", "value": bool(on)}])

    def white(self, brightness, temp):
        self._ensure_codes()
        bright = self._pick("bright_value_v2", "bright_value") or "bright_value_v2"
        tcode = self._pick("temp_value_v2", "temp_value")
        lo, hi = (10, 1000) if bright.endswith("_v2") else (25, 255)
        cmds = [{"code": "switch_led", "value": True}, {"code": "work_mode", "value": "white"},
                {"code": bright, "value": int(lo + (hi - lo) * clamp(brightness, 1, 100) / 100)}]
        if tcode:
            tmax = 1000 if tcode.endswith("_v2") else 255
            cmds.append({"code": tcode, "value": int(tmax * clamp(temp, 0, 100) / 100)})
        self._send(cmds)

    def colour(self, h, s, v):
        self._ensure_codes()
        col = self._pick("colour_data_v2", "colour_data") or "colour_data_v2"
        cmax = 1000 if col.endswith("_v2") else 255
        value = {"h": int(clamp(h, 0, 360)), "s": int(cmax * clamp(s, 0, 100) / 100),
                 "v": int(cmax * clamp(v, 1, 100) / 100)}
        self._send([{"code": "switch_led", "value": True}, {"code": "work_mode", "value": "colour"},
                    {"code": col, "value": value}])


# ---------------------------------------------------------------- controller

class Controller:
    """Picks local or cloud, remembers when local was unreachable so auto mode stays fast."""

    LOCAL_RETRY_SECS = 60

    def __init__(self):
        self.lock = threading.Lock()
        self.cfg = load_config()
        self._backends = {}
        self.local_down_until = 0.0
        self.last_error = ""

    def reset(self):
        self._backends = {}
        self.local_down_until = 0.0

    def _local(self):
        if "local" not in self._backends:
            d = self.cfg["device"]
            if not (d["id"] and d["ip"] and d["key"]):
                raise LedError("Local control needs the device's local key (see Setup).")
            self._backends["local"] = LocalBackend(d)
        return self._backends["local"]

    def _cloud(self):
        if "cloud" not in self._backends:
            c = self.cfg["cloud"]
            if not (c["api_key"] and c["api_secret"]):
                raise LedError("Cloud control needs your Tuya IoT Access ID and Secret (see Setup).")
            self._backends["cloud"] = CloudBackend(self.cfg["device"], c)
        return self._backends["cloud"]

    def configured(self):
        d, c = self.cfg["device"], self.cfg["cloud"]
        return bool(d["key"]) or bool(c["api_key"] and c["api_secret"])

    def run(self, fn):
        """Run fn(backend) using the configured connection; returns (result, backend_name)."""
        mode = self.cfg.get("connection", "auto")
        errors = []
        order = {"local": ["local"], "cloud": ["cloud"]}.get(mode, ["local", "cloud"])
        for name in order:
            if name == "local" and mode == "auto":
                if time.time() < self.local_down_until or not self.cfg["device"]["key"]:
                    continue
            try:
                backend = self._local() if name == "local" else self._cloud()
                result = fn(backend)
                if name == "local":
                    self.local_down_until = 0.0
                return result, name
            except Exception as e:  # tinytuya raises a mix of exception types
                errors.append(f"{name}: {e}")
                if name == "local":
                    self.local_down_until = time.time() + self.LOCAL_RETRY_SECS
                    self._backends.pop("local", None)
                else:
                    self._backends.pop("cloud", None)
        if not errors:
            errors.append("Not set up yet: add a local key or Tuya Cloud credentials in Setup.")
        raise LedError(" | ".join(errors))

    # --- actions

    def state(self):
        with self.lock:
            st, via = self.run(lambda b: b.state())
            st["via"] = via
            return st

    def command(self, body):
        action = body.get("action")
        with self.lock:
            if action == "power":
                _, via = self.run(lambda b: b.power(bool(body.get("on"))))
            elif action == "white":
                _, via = self.run(lambda b: b.white(int(body.get("brightness", 100)), int(body.get("temp", 50))))
            elif action == "colour":
                _, via = self.run(lambda b: b.colour(int(body.get("h", 0)), int(body.get("s", 100)), int(body.get("v", 100))))
            else:
                raise LedError(f"Unknown action {action!r}")
            return {"ok": True, "via": via}

    def update_config(self, body):
        with self.lock:
            if body.get("connection") in ("auto", "local", "cloud"):
                self.cfg["connection"] = body["connection"]
            for section in ("device", "cloud"):
                for field, value in (body.get(section) or {}).items():
                    if field not in self.cfg[section] or not isinstance(value, str):
                        continue
                    value = value.strip()
                    if (section, field) in SECRET_FIELDS and not value:
                        continue  # blank secret field = keep existing value
                    self.cfg[section][field] = value
            save_config(self.cfg)
            self.reset()
            return public_config(self.cfg)

    def set_app_pref(self, key, value):
        """Desktop-app preferences (theme, tray behaviour); no device reconnect needed."""
        with self.lock:
            self.cfg.setdefault("app", {})[key] = value
            save_config(self.cfg)

    def fetch_key_from_cloud(self):
        """Use the Tuya Cloud credentials to download the device's local key and name."""
        with self.lock:
            c, d = self.cfg["cloud"], self.cfg["device"]
            if not (c["api_key"] and c["api_secret"]):
                raise LedError("Enter your Tuya IoT Access ID and Secret first.")
            cloud = tinytuya.Cloud(apiRegion=c["region"], apiKey=c["api_key"], apiSecret=c["api_secret"],
                                   apiDeviceID=d["id"])
            if getattr(cloud, "error", None):
                raise LedError(f"Tuya Cloud login failed: {cloud.error.get('Payload') or cloud.error}")
            devices = cloud.getdevices(verbose=False)
            if isinstance(devices, dict) and "Error" in devices:
                raise LedError(f"Could not list devices: {devices.get('Payload') or devices.get('Error')}")
            match = next((x for x in devices if x.get("id") == d["id"]), None)
            if not match:
                names = ", ".join(f"{x.get('name')} ({x.get('id')})" for x in devices) or "none"
                raise LedError(f"Device {d['id']} not found in your Tuya project. Devices found: {names}. "
                               "Make sure the Smart Life app account is linked to the project.")
            d["key"] = match.get("key", "")
            d["name"] = match.get("name", "")
            save_config(self.cfg)
            self.reset()
            return {"ok": True, "name": d["name"], "key_found": bool(d["key"])}

    def diagnostics(self):
        d = self.cfg["device"]
        report = {"device_id": d["id"], "configured_ip": d["ip"]}
        found = tinytuya.find_device(dev_id=d["id"])
        report["broadcast_seen"] = bool(found and found.get("ip"))
        if report["broadcast_seen"]:
            report["broadcast_ip"] = found["ip"]
            if found["ip"] != d["ip"]:
                with self.lock:
                    d["ip"] = found["ip"]
                    if found.get("version"):
                        d["version"] = str(found["version"])
                    save_config(self.cfg)
                    self.reset()
                report["ip_updated"] = True
        report["tcp_reachable"] = tcp_reachable(d["ip"])
        report["has_local_key"] = bool(d["key"])
        report["has_cloud_creds"] = bool(self.cfg["cloud"]["api_key"] and self.cfg["cloud"]["api_secret"])

        def try_backend(name):
            try:
                with self.lock:
                    b = self._local() if name == "local" else self._cloud()
                    b.state()
                return True, ""
            except Exception as e:
                with self.lock:
                    self._backends.pop(name, None)
                return False, str(e)

        if report["has_local_key"] and report["tcp_reachable"]:
            report["local_ok"], report["local_error"] = try_backend("local")
        if report["has_cloud_creds"]:
            report["cloud_ok"], report["cloud_error"] = try_backend("cloud")
        if report.get("local_ok"):
            self.local_down_until = 0.0
        return report


