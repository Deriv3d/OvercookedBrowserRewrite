"""Rebuild the WebGL port from a clean checkout plus the Steam install.

    py tools\\rebuild_all.py [options]

Stages, in order (each can be picked with --from / --only):
  export  checks the Steam copy first (tools/check_game.py), then AssetRipper
          2.0.0 Free, run headless through its web API (/Settings/Update,
          /LoadFolder, /Export/UnityProject, form field Path), exports the
          Steam install to <lab>\\export.
  project copies <lab>\\export\\ExportedProject to <lab>\\project (the working
          copy that gets patched; the export itself stays untouched).
  patch   tools/apply_patches.py on the project (idempotent; copies port/ in).
  import  Unity 2022.3 batchmode import with -accept-apiupdate.
  build   Unity batchmode build: WebGLPortBuild.BuildFromCommandLine (desktop,
          Builds\\WebGL) or WebGLPortPhoneBuild.BuildFromCommandLine with
          --phone (Builds\\WebGL-Phone). With --number N the result is moved to
          Builds\\WebGL<N> (or WebGL-Phone<N>).
  serve   starts tools/serve.py on the new build (8001 by default; --lan for
          the phone build). Don't build over a folder that is being served.

Nothing here touches the Steam install; it is only read. Only one Unity run at
a time. Every stage writes its log under <lab>\\logs and stops the
script on failure. --dry-run prints the commands without running anything.

Each stage uses commands from earlier manual builds; the whole chain is new,
so report problems.
"""

import argparse
import os
import shutil
import subprocess
import sys
import time
import urllib.parse
import urllib.request
from pathlib import Path

import check_game
import portpaths

REPO = Path(__file__).resolve().parent.parent
STAGES = ["export", "project", "patch", "import", "build", "serve"]

ASSETRIPPER_PORT = 8999


def log(msg):
    print("[rebuild] " + msg, flush=True)


def run(cmd, log_file, dry):
    log(("would run: " if dry else "running: ") + " ".join(f'"{c}"' if " " in c else c for c in cmd))
    if dry:
        return
    with open(log_file, "w", encoding="utf-8", errors="replace") as out:
        code = subprocess.call(cmd, stdout=out, stderr=subprocess.STDOUT)
    if code != 0:
        sys.exit(f"[rebuild] failed with exit code {code}; see {log_file}")


def post(path, fields):
    data = urllib.parse.urlencode(fields).encode()
    req = urllib.request.Request(f"http://127.0.0.1:{ASSETRIPPER_PORT}{path}", data=data)
    # /Export/UnityProject answers 302 once the export has finished.
    with urllib.request.urlopen(req, timeout=3600) as resp:
        return resp.status


def stage_export(a):
    log("export: checking the Steam copy of Overcooked (tools/check_game.py)")
    if not check_game.check(Path(a.steam) if a.steam else None) and not a.dry_run:
        sys.exit("[rebuild] the game check failed; see the FAIL lines above")
    exe = Path(a.lab) / "tools" / "AssetRipper" / portpaths.assetripper().name
    out = Path(a.lab) / "export"
    if out.exists() and not a.clean:
        log(f"export: {out} exists, kept (use --clean to redo it)")
        return
    if not exe.exists() and not a.dry_run:
        sys.exit(f"[rebuild] AssetRipper not found at {exe}")
    cmd = [str(exe), "--port", str(ASSETRIPPER_PORT), "--headless"]
    log(("would start: " if a.dry_run else "starting: ") + " ".join(cmd))
    if a.dry_run:
        log(f"would POST /Settings/Update ScriptExportMode=Decompiled ShaderExportMode=Decompile, /LoadFolder Path={a.steam}, /Export/UnityProject Path={out}")
        return
    if out.exists():
        shutil.rmtree(out)
    with open(Path(a.lab) / "logs" / "export.log", "w", encoding="utf-8") as out_log:
        server = subprocess.Popen(cmd, stdout=out_log, stderr=subprocess.STDOUT)
        try:
            for _ in range(60):
                try:
                    urllib.request.urlopen(f"http://127.0.0.1:{ASSETRIPPER_PORT}/", timeout=2)
                    break
                except OSError:
                    time.sleep(1)
            # The Free edition writes dummy shaders whatever the mode;
            # port/shaders replaces them.
            post("/Settings/Update", {"ScriptExportMode": "Decompiled", "ShaderExportMode": "Decompile"})
            post("/LoadFolder", {"Path": a.steam})
            post("/Export/UnityProject", {"Path": str(out)})
        finally:
            server.terminate()
    if not (out / "ExportedProject" / "Assets").exists():
        sys.exit(f"[rebuild] export produced no ExportedProject\\Assets; see {a.lab}\\logs\\export.log")


def stage_project(a):
    src = Path(a.lab) / "export" / "ExportedProject"
    dst = Path(a.lab) / "project"
    if dst.exists() and not a.clean:
        log(f"project: {dst} exists, kept (patches are idempotent; use --clean for a fresh copy)")
        return
    log(f"project: copy {src} -> {dst}")
    if a.dry_run:
        return
    if dst.exists():
        shutil.rmtree(dst)
    shutil.copytree(src, dst)


def stage_patch(a):
    run([sys.executable, str(REPO / "tools" / "apply_patches.py"), str(Path(a.lab) / "project")],
        Path(a.lab) / "logs" / "patch.log", a.dry_run)


def unity(a, extra, name):
    run([a.unity, "-batchmode", "-quit", "-accept-apiupdate", "-projectPath", str(Path(a.lab) / "project"),
         "-logFile", str(Path(a.lab) / "logs" / f"{name}.log")] + extra,
        Path(a.lab) / "logs" / f"{name}-stdout.log", a.dry_run)


def stage_import(a):
    unity(a, [], "import")


def build_dir(a):
    builds = Path(a.lab) / "project" / "Builds"
    plain = builds / ("WebGL-Phone" if a.phone else "WebGL")
    if a.number is None:
        return plain, plain
    return plain, builds / (("WebGL-Phone" if a.phone else "WebGL") + str(a.number))


def stage_build(a):
    method = "WebGLPortPhoneBuild.BuildFromCommandLine" if a.phone else "WebGLPortBuild.BuildFromCommandLine"
    plain, final = build_dir(a)
    unity(a, ["-executeMethod", method], ("build-phone" if a.phone else "build") + (str(a.number) if a.number else ""))
    if final != plain:
        log(f"build: move {plain} -> {final}")
        if not a.dry_run:
            if final.exists():
                sys.exit(f"[rebuild] {final} already exists; pick another --number")
            shutil.move(str(plain), str(final))


def stage_serve(a):
    _, final = build_dir(a)
    port = str(a.port or (8002 if a.phone else 8001))
    cmd = [sys.executable, str(REPO / "tools" / "serve.py"), str(final), port] + (["--lan"] if a.phone else [])
    log(("would run: " if a.dry_run else "serving (Ctrl+C stops): ") + " ".join(cmd))
    if not a.dry_run:
        subprocess.call(cmd)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    # Defaults come from tools/portpaths.py: env vars, overcooked-port.json, auto-detection.
    steam, unity = portpaths.steam_game(), portpaths.unity()
    p.add_argument("--steam", default=str(steam) if steam else None, help="Overcooked Steam folder (read only)")
    p.add_argument("--lab", default=str(portpaths.lab()), help="lab folder for export, project, builds, logs")
    p.add_argument("--unity", default=str(unity) if unity else None, help=f"Unity {portpaths.UNITY_VERSION} editor exe")
    p.add_argument("--from", dest="start", choices=STAGES, default="export", help="first stage to run")
    p.add_argument("--only", choices=STAGES, help="run just this stage")
    p.add_argument("--no-serve", action="store_true", help="stop after the build")
    p.add_argument("--phone", action="store_true", help="phone build (Builds\\WebGL-Phone, serve --lan on 8002)")
    p.add_argument("--number", type=int, help="move the build to Builds\\WebGL<N>")
    p.add_argument("--port", type=int, help="serve port (default 8001, phone 8002)")
    p.add_argument("--clean", action="store_true", help="redo the export and the project copy")
    p.add_argument("--dry-run", action="store_true", help="print the commands only")
    a = p.parse_args()

    if not a.dry_run:
        for path, what in ((a.steam, "Steam install"), (a.unity, "Unity editor")):
            if not path or not Path(path).exists():
                sys.exit(f"[rebuild] {what} not found: {path}")
        (Path(a.lab) / "logs").mkdir(parents=True, exist_ok=True)

    stages = [a.only] if a.only else STAGES[STAGES.index(a.start):]
    if a.no_serve and "serve" in stages and not a.only:
        stages.remove("serve")
    for name in stages:
        log(f"== {name}")
        globals()["stage_" + name](a)
    log("done")


if __name__ == "__main__":
    main()
