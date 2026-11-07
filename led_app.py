"""
LED Control - browser panel.

Runs a small web server on http://127.0.0.1:8765 (only reachable from this PC).
The LED logic and settings are shared with the desktop app (see led_core.py).

Run:  python led_app.py
"""

import json
import socket
import sys
import threading
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

from led_core import Controller, public_config

HOST, PORT = "127.0.0.1", 8765
INDEX_PATH = Path(__file__).resolve().parent / "index.html"


# ---------------------------------------------------------------- HTTP

controller = Controller()


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt, *args):
        pass  # keep the console quiet

    def _json(self, code, payload):
        data = json.dumps(payload).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def _body(self):
        n = int(self.headers.get("Content-Length") or 0)
        if not n:
            return {}
        return json.loads(self.rfile.read(n) or b"{}")

    def _guard(self):
        # Only accept requests from this app's own page (blocks other websites poking the API).
        host = self.headers.get("Host", "")
        if host not in (f"{HOST}:{PORT}", f"localhost:{PORT}"):
            self._json(403, {"ok": False, "error": "Forbidden host"})
            return False
        origin = self.headers.get("Origin")
        if origin and origin not in (f"http://{HOST}:{PORT}", f"http://localhost:{PORT}"):
            self._json(403, {"ok": False, "error": "Forbidden origin"})
            return False
        return True

    def do_GET(self):
        if not self._guard():
            return
        try:
            if self.path in ("/", "/index.html"):
                data = INDEX_PATH.read_bytes()
                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.send_header("Content-Length", str(len(data)))
                self.end_headers()
                self.wfile.write(data)
            elif self.path == "/api/state":
                if not controller.configured():
                    self._json(200, {"ok": False, "needs_setup": True,
                                     "error": "Not set up yet: add a local key or Tuya Cloud credentials."})
                else:
                    self._json(200, {"ok": True, **controller.state()})
            elif self.path == "/api/config":
                self._json(200, public_config(controller.cfg))
            elif self.path == "/api/diagnostics":
                self._json(200, {"ok": True, **controller.diagnostics()})
            else:
                self._json(404, {"ok": False, "error": "Not found"})
        except Exception as e:
            self._json(200, {"ok": False, "error": str(e)})

    def do_POST(self):
        if not self._guard():
            return
        try:
            body = self._body()
            if self.path == "/api/command":
                self._json(200, controller.command(body))
            elif self.path == "/api/config":
                self._json(200, controller.update_config(body))
            elif self.path == "/api/fetch-key":
                self._json(200, controller.fetch_key_from_cloud())
            else:
                self._json(404, {"ok": False, "error": "Not found"})
        except Exception as e:
            self._json(200, {"ok": False, "error": str(e)})


class Server(ThreadingHTTPServer):
    # On Windows, address reuse lets a second copy bind the same port; refuse instead.
    allow_reuse_address = sys.platform != "win32"
    daemon_threads = True

    def server_bind(self):
        if sys.platform == "win32" and hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
            self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        super().server_bind()


def main():
    try:
        server = Server((HOST, PORT), Handler)
    except OSError:
        print(f"Port {PORT} is busy - the app is probably already running. Opening it.")
        webbrowser.open(f"http://{HOST}:{PORT}/")
        return
    url = f"http://{HOST}:{PORT}/"
    print(f"Tuya LED control running at {url}  (Ctrl+C to stop)")
    if "--no-browser" not in sys.argv:
        threading.Timer(0.6, lambda: webbrowser.open(url)).start()
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("Stopped.")


if __name__ == "__main__":
    main()
