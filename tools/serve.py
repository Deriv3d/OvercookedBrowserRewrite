#!/usr/bin/env python3
"""Serve the WebGL build on http://localhost:8000.

Usage:
    python tools/serve.py <build-folder> [port] [test-folder] [--lan] [--https]

--https (phone Home Screen app): serve https with the
home certificate from port/mobile/https/make_phone_cert.py (folder
<lab>\\https). Any server also offers that folder's ca.crt at
/__ca.crt, for the phone to install once.

Default build folder: <lab>\\project\\Builds\\WebGL
Default test folder:  <lab>\\test
(<lab> is C:\\OVERCOOKED-LAB unless tools/portpaths.py is told otherwise.)

Testing: /__test.html (tools/test_page.html) loads the same build, posts its
console to /__log and canvas screenshots to /__shot; they are saved under
<test-folder>/<run>/ as browser.log and shot-NN.png. See tools/headless_test.py.

Unity's Brotli build ships files like game.wasm.br. Browsers only accept them
when the server says "Content-Encoding: br" and gives the real type
(application/wasm, ...), which Python's plain http.server doesn't do. This one
does, and it only listens on this PC (127.0.0.1).
"""

import http.server
import functools
import hashlib
import json
import html
import re
import socket
import ssl
import sys
import threading
from pathlib import Path
from urllib.parse import parse_qs, urlsplit

import portpaths

# The lab folder comes from tools/portpaths.py (C:\OVERCOOKED-LAB unless set).
DEFAULT_BUILD = str(portpaths.lab() / "project" / "Builds" / "WebGL")
DEFAULT_TEST = str(portpaths.lab() / "test")
CERT_DIR = portpaths.https_dir()
TEST_PAGE = Path(__file__).with_name("test_page.html")
# Simulated DS4 for headless runs (?pad=...).
FAKE_PAD = Path(__file__).resolve().parent.parent / "port" / "input" / "fake-gamepad.js"

TYPES = {
    ".wasm": "application/wasm",
    ".js": "application/javascript",
    ".data": "application/octet-stream",
    ".symbols.json": "application/octet-stream",
    ".json": "application/json",
    ".html": "text/html; charset=utf-8",
}


class Handler(http.server.SimpleHTTPRequestHandler):
    test_dir = Path(DEFAULT_TEST)
    scheme = "http"
    lock = threading.Lock()
    shot_counts = {}
    ended = set()

    def log_message(self, format, *args):
        # Keep the console readable: skip the test page's own log/shot posts.
        if not self.path.startswith("/__"):
            super().log_message(format, *args)

    def run_dir(self):
        query = parse_qs(urlsplit(self.path).query)
        run = re.sub(r"[^A-Za-z0-9_.-]", "_", query.get("run", ["run"])[0]) or "run"
        folder = self.test_dir / run
        folder.mkdir(parents=True, exist_ok=True)
        return run, folder

    def reply(self, code=204):
        self.send_response(code)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def do_GET(self):
        own = {"/__test.html": (TEST_PAGE, "text/html; charset=utf-8"),
               "/__fake-gamepad.js": (FAKE_PAD, "application/javascript")}
        if urlsplit(self.path).path in own:
            page, ctype = own[urlsplit(self.path).path]
            body = page.read_bytes()
            if page == TEST_PAGE:
                body = self.with_build_names(body)
            self.send_response(200)
            self.send_header("Content-Type", ctype)
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        if urlsplit(self.path).path in ("/", "/index.html"):
            body = (Path(self.directory) / "index.html").read_bytes()
            body = body.replace(b"<!--port-build-->", self.build_label().encode())
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        if urlsplit(self.path).path == "/__ca.crt":
            # The home CA (make_phone_cert.py) for the phone to install. Only
            # the certificate; its key never leaves CERT_DIR.
            ca = CERT_DIR / "ca.crt"
            if not ca.exists():
                self.reply(404)
                return
            body = ca.read_bytes()
            self.send_response(200)
            self.send_header("Content-Type", "application/x-x509-ca-cert")
            self.send_header("Content-Disposition", 'attachment; filename="Overcooked-Home-CA.crt"')
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        if urlsplit(self.path).path == "/__save.json":
            # Test runs with ?restore=1 start from this save backup (made with
            # "Back up saves" on the page), kept next to the test logs.
            save = self.test_dir / "test-save.json"
            if not save.exists():
                self.reply(404)
                return
            body = save.read_bytes()
            # Unity names the save folder /idbfs/<md5 of the page's folder URL
            # without the trailing slash> (a save backup from localhost:8000 sits
            # under md5("http://localhost:8000")), so move the backup's folder
            # to this server's, or the game on another port finds no save.
            here = hashlib.md5((self.scheme + "://" + self.headers.get("Host", "")).encode()).hexdigest()
            body = re.sub(rb'"/idbfs/[0-9a-f]{32}', b'"/idbfs/' + here.encode(), body)
            if "phone=1" in urlsplit(self.path).query:
                # The phone page (?restore=1) takes the progress only: the PC's
                # PlayerPrefs carry its Graphics setting (Standard/Dreamy), so
                # they're left out and the phone keeps its Super Low default.
                data = json.loads(body)
                data["files"] = [f for f in data["files"] if not f["path"].endswith("/PlayerPrefs")]
                body = json.dumps(data).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self.end_headers()
            self.wfile.write(body)
            return
        if urlsplit(self.path).path == "/__ended":
            run, _ = self.run_dir()
            self.reply(200 if run in self.ended else 404)
            return
        super().do_GET()

    def build_label(self):
        # Shown under "Click to play" so you can see which build a port
        # serves: the number from the served folder's name
        # (Builds\WebGL24, served\build24). The build time comes from the
        # page itself (WebGLPortBuild sets it); file times are unreliable
        # because Unity reuses cached build files.
        folder = Path(self.directory).resolve()
        number = re.search(r"(\d+)$", folder.name)
        return html.escape(f"Build {number.group(1)} \u00b7" if number else folder.name + " \u00b7")

    def with_build_names(self, body):
        # Builds name their files by content hash (nameFilesAsHashes), so the
        # test page's fixed WebGL.* names are swapped for this build's files.
        files = [p.name for p in (Path(self.directory) / "Build").iterdir()]
        for fixed, suffix in (("WebGL.loader.js", ".loader.js"), ("WebGL.data.br", ".data"),
                              ("WebGL.framework.js.br", ".framework.js"), ("WebGL.wasm.br", ".wasm")):
            if fixed in files:
                continue
            hits = [f for f in files if f.endswith(suffix) or f.endswith(suffix + ".br") or f.endswith(suffix + ".gz")]
            if len(hits) == 1:
                body = body.replace(fixed.encode(), hits[0].encode())
        return body

    def do_POST(self):
        route = urlsplit(self.path).path
        body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        run, folder = self.run_dir()
        with self.lock:
            if route == "/__log":
                with open(folder / "browser.log", "ab") as f:
                    f.write(body)
            elif route == "/__shot":
                n = self.shot_counts.get(run, 0) + 1
                self.shot_counts[run] = n
                (folder / f"shot-{n:02d}.png").write_bytes(body)
            elif route == "/__end":
                self.ended.add(run)
            else:
                self.reply(404)
                return
        self.reply()

    def guess_type(self, path):
        name = str(path)
        for compressed in (".br", ".gz"):
            if name.endswith(compressed):
                name = name[: -len(compressed)]
        for ext, ctype in TYPES.items():
            if name.endswith(ext):
                return ctype
        return super().guess_type(name)

    def end_headers(self):
        path = self.path.split("?", 1)[0]
        if path.endswith(".br"):
            self.send_header("Content-Encoding", "br")
        elif path.endswith(".gz"):
            self.send_header("Content-Encoding", "gzip")
        # Always fetch fresh files after a rebuild; Unity's own data cache
        # (IndexedDB) still avoids re-downloading unchanged content.
        self.send_header("Cache-Control", "no-cache")
        super().end_headers()


def lan_address():
    # The address other devices on the home network reach this PC at. A UDP
    # "connect" only picks the outgoing interface; nothing is sent.
    with socket.socket(socket.AF_INET, socket.SOCK_DGRAM) as probe:
        try:
            probe.connect(("192.168.0.1", 9))
            return probe.getsockname()[0]
        except OSError:
            return "this PC's Wi-Fi address (ipconfig)"


def main():
    # --lan (phone tests on the same Wi-Fi): listen on
    # every network interface instead of this PC only. Windows asks once to
    # allow Python on private networks.
    args = [a for a in sys.argv[1:] if a not in ("--lan", "--https")]
    lan = "--lan" in sys.argv[1:]
    https = "--https" in sys.argv[1:]
    build = Path(args[0] if len(args) > 0 else DEFAULT_BUILD)
    port = int(args[1]) if len(args) > 1 else 8000
    Handler.test_dir = Path(args[2] if len(args) > 2 else DEFAULT_TEST)
    if not (build / "index.html").exists():
        sys.exit(f"No index.html in {build}. Build first (Unity menu: Overcooked Port > Build WebGL).")
    handler = functools.partial(Handler, directory=str(build))
    server = http.server.ThreadingHTTPServer(("0.0.0.0" if lan else "127.0.0.1", port), handler)
    scheme = "http"
    if https:
        if not (CERT_DIR / "server.crt").exists():
            sys.exit(f"No certificate in {CERT_DIR}. Run: py port\\mobile\\https\\make_phone_cert.py")
        context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
        context.load_cert_chain(CERT_DIR / "server.crt", CERT_DIR / "server.key")
        server.socket = context.wrap_socket(server.socket, server_side=True)
        scheme = Handler.scheme = "https"
    print(f"Serving {build}\nOpen {scheme}://localhost:{port} in Chrome. Ctrl+C to stop.")
    if lan:
        print(f"On the same Wi-Fi (phone): {scheme}://{lan_address()}:{port}/")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
