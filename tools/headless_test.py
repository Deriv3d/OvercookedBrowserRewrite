#!/usr/bin/env python3
"""Play the first minutes of the WebGL build in a hidden Chrome and keep the log.

Usage (tools/serve.py must already be running):
    python tools/headless_test.py [--run NAME] [--seconds 120] [--script STEPS] [--software]

It opens http://localhost:8000/__test.html in headless Chrome with its own
throwaway profile, so nothing appears on screen, no real keyboard or mouse
input is sent, and your Chrome profile is untouched. Key presses in the script
are DOM events inside that hidden page only.

Results land in <test-folder>/<run>/ (default C:\\OVERCOOKED-LAB\\test\\<run>\\):
browser.log has the game's console with timestamps (seconds since page load);
shot-NN.png are canvas screenshots.

STEPS: comma-separated "<seconds after the game is ready>@<action>", actions:
shot, tap:<KeyboardEvent.code>, down:<code>, up:<code>, end. The default script
takes screenshots and presses Enter/Space to get past the intro and menus.

--software uses Chrome's CPU renderer (SwiftShader) if the GPU path gives no WebGL2.
"""

import argparse
import os
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

DEFAULT_SCRIPT = ",".join([
    "2@shot", "10@shot", "20@shot",
    "25@tap:Enter", "27@shot", "30@tap:Space", "32@shot",
    "40@tap:Enter", "42@shot", "50@tap:Enter", "55@shot",
    "70@shot", "90@shot", "100@end",
])

CHROME_PATHS = [
    r"C:\Program Files\Google\Chrome\Application\chrome.exe",
    r"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    os.path.expandvars(r"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe"),
    "/usr/bin/google-chrome",
    "/usr/bin/chromium",
]


def find_chrome():
    for path in CHROME_PATHS:
        if Path(path).exists():
            return path
    found = shutil.which("chrome") or shutil.which("google-chrome") or shutil.which("chromium")
    if found:
        return found
    sys.exit("Chrome not found; pass its path with --chrome")


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--run", default=time.strftime("run-%H%M%S"))
    ap.add_argument("--seconds", type=int, default=180, help="hard time limit for the whole run")
    ap.add_argument("--script", default=DEFAULT_SCRIPT)
    ap.add_argument("--fx", help="camera effects to keep off; write it as --fx=-Bloom,-Antialiasing (argparse rejects a separate value starting with -) (port/scripts/WebGLPortEffectToggle.cs)")
    ap.add_argument("--pagedebug", help="recipe-book page debug view: 1 side tint, 2 RT alpha")
    ap.add_argument("--pad", help="simulated DS4 steps for port/input/fake-gamepad.js, e.g. '1@connect,20@btn:0'")
    ap.add_argument("--fast", help="test speed outside rounds, e.g. 4 (port/scripts/WebGLPortTestSpeed.cs)")
    ap.add_argument("--param", action="append", default=[], help="extra page-address parameter KEY=VALUE, repeatable (e.g. --param unlockall=1)")
    ap.add_argument("--port", type=int, default=8000)
    ap.add_argument("--chrome")
    ap.add_argument("--software", action="store_true")
    ap.add_argument("--width", type=int, default=1920)
    ap.add_argument("--height", type=int, default=1080)
    args = ap.parse_args()

    base = f"http://localhost:{args.port}"
    try:
        urllib.request.urlopen(base + "/__test.html", timeout=5).read(1)
    except Exception as err:
        sys.exit(f"tools/serve.py doesn't answer on {base} ({err}); start it first.")

    fields = {"run": args.run, "script": args.script, "w": args.width, "h": args.height}
    if args.pad:
        fields["pad"] = args.pad
    if args.fx:
        fields["fx"] = args.fx
    if args.pagedebug:
        fields["pagedebug"] = args.pagedebug
    if args.fast:
        fields["fast"] = args.fast
    for pair in args.param:
        key, _, value = pair.partition("=")
        fields[key] = value
    query = urllib.parse.urlencode(fields)
    url = f"{base}/__test.html?{query}"
    profile = tempfile.mkdtemp(prefix="overcooked-headless-")
    flags = [
        "--headless=new",
        f"--user-data-dir={profile}",
        f"--window-size={args.width},{args.height}",
        "--no-first-run",
        "--no-default-browser-check",
        "--autoplay-policy=no-user-gesture-required",
        "--mute-audio",
        "--ignore-gpu-blocklist",
        "--enable-unsafe-swiftshader" if args.software else "--enable-gpu",
    ]
    if args.software:
        flags.append("--use-angle=swiftshader")
    if os.name != "nt" and os.geteuid() == 0:
        flags.append("--no-sandbox")  # Linux containers run as root
    chrome = args.chrome or find_chrome()
    print(f"Run {args.run}: {url}")
    proc = subprocess.Popen([chrome, *flags, url], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    ended = False
    try:
        deadline = time.time() + args.seconds
        while time.time() < deadline and proc.poll() is None:
            time.sleep(2)
            try:
                urllib.request.urlopen(f"{base}/__ended?run={urllib.parse.quote(args.run)}", timeout=5)
                ended = True
                break
            except urllib.error.HTTPError:
                pass
        time.sleep(2)  # let the last log batch arrive
    finally:
        if os.name == "nt":
            # Chrome starts helper processes; end the whole tree.
            subprocess.run(["taskkill", "/T", "/F", "/PID", str(proc.pid)], capture_output=True)
        else:
            proc.kill()
        proc.wait()
        shutil.rmtree(profile, ignore_errors=True)
    print("Script finished." if ended else f"Stopped after {args.seconds}s (script didn't reach 'end').")
    print("Results: <test-folder>/" + args.run + "/browser.log and shot-NN.png")


if __name__ == "__main__":
    main()
