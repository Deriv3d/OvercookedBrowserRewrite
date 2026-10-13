#!/usr/bin/env python3
"""Pack a phone build into the iPhone app (port/mobile/ios/README.md).

Usage:
    py port\\mobile\\tools\\pack_ipa.py C:\\OVERCOOKED-LAB\\served\\phone7 [out.ipa]

Takes the app shell (port/mobile/ios/dist/Overcooked-shell.ipa: our own code,
built by GitHub, no game files) and adds the phone build to it as
Payload/Overcooked.app/Game/. The default output is <build>\\app\\Overcooked.ipa,
so while serve.py serves that build on 8002 the phone can download it from
http://<PC address>:8002/app/Overcooked.ipa. SideStore signs it on the phone.

The finished .ipa holds game files: it stays in the lab folder and on the
phone, never in the repo or on GitHub.

What changes on the way in:
  - Build/*.gz are stored unpacked and the page points at the unpacked names
    (a web view only unpacks gzip it gets over the network).
  - saves.js comes from port/mobile/template (it has the app's Back up path).
  - The page's "Add to Home Screen" hint is dropped.
  - icon.png becomes the app icon.
Nothing in the build folder is modified.
"""

import gzip
import re
import shutil
import sys
import time
import zipfile
from pathlib import Path

MOBILE = Path(__file__).resolve().parent.parent
SHELL = MOBILE / "ios" / "dist" / "Overcooked-shell.ipa"
TEMPLATE = MOBILE / "template"
APP = "Payload/Overcooked.app/"
GAME = APP + "Game/"
TOP_FILES = ["index.html", "manifest.json", "icon.png"]
FOLDERS = ["Build", "StreamingAssets", "TemplateData"]

# (old, new, what): page edits; a missing anchor is reported, not fatal.
PAGE_EDITS = [
    ("<br>For full screen: Share \u203a Add to Home Screen, then open it from there.", "",
     "dropped the Add to Home Screen hint"),
]


def entry(arcname):
    # Files written from memory: readable by everyone, like files on disk
    # (zipfile's default is owner-only).
    info = zipfile.ZipInfo(arcname, time.localtime()[:6])
    info.compress_type = zipfile.ZIP_DEFLATED
    info.external_attr = 0o100644 << 16
    return info


def fail(message):
    sys.exit("pack_ipa: " + message)


def main():
    args = sys.argv[1:]
    if not args or len(args) > 2:
        sys.exit(__doc__)
    build = Path(args[0])
    out = Path(args[1]) if len(args) > 1 else build / "app" / "Overcooked.ipa"
    if not (build / "index.html").is_file() or not (build / "Build").is_dir():
        fail(f"{build} has no index.html and Build folder; give the served phone build folder")
    if not SHELL.is_file():
        fail(f"no app shell at {SHELL}; pull the branch that has it")

    page = (build / "index.html").read_text(encoding="utf-8")
    if "OvercookedSaves" not in page:
        fail("index.html isn't the phone page (no OvercookedSaves); pack a phone build")

    # Unpacked names for the gzip files, and the page pointed at them.
    unpack = {}
    for f in sorted((build / "Build").iterdir()):
        if f.suffix == ".br":
            fail(f"{f.name} is Brotli; phone builds are gzip. Brotli can't be unpacked with plain Python")
        if f.suffix == ".gz":
            plain = f.name[:-3]
            if page.count(f.name) == 0:
                fail(f"index.html doesn't mention {f.name}")
            page = page.replace(f.name, plain)
            unpack[f.name] = plain
    for old, new, what in PAGE_EDITS:
        if old in page:
            page = page.replace(old, new)
            print("page: " + what)
        else:
            print("page: anchor not found, left as is: " + what)

    out.parent.mkdir(parents=True, exist_ok=True)
    tmp = out.with_suffix(".ipa.part")
    with zipfile.ZipFile(SHELL) as shell, zipfile.ZipFile(tmp, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as ipa:
        names = shell.namelist()
        if APP + "Overcooked" not in names:
            fail("the shell has no Payload/Overcooked.app/Overcooked")
        if any(n.startswith(GAME) for n in names):
            fail("the shell already has a Game folder; use a clean shell")
        # The shell as built, permissions included (the app binary must stay executable).
        for info in shell.infolist():
            ipa.writestr(info, shell.read(info))

        def add_file(path, arcname):
            ipa.write(path, arcname)

        ipa.writestr(entry(GAME + "index.html"), page)
        ipa.write(TEMPLATE / "saves.js", GAME + "saves.js")
        for name in TOP_FILES[1:]:
            if (build / name).is_file():
                add_file(build / name, GAME + name)
        if (build / "icon.png").is_file():
            for scale in ("@2x", "@3x"):
                add_file(build / "icon.png", APP + "AppIcon60x60" + scale + ".png")
        else:
            print("no icon.png in the build: the app gets a blank icon")
        for folder in FOLDERS:
            root = build / folder
            if not root.is_dir():
                continue
            for f in sorted(p for p in root.rglob("*") if p.is_file()):
                rel = f.relative_to(build).as_posix()
                if f.name in unpack:
                    arc = GAME + rel[: -len(f.name)] + unpack[f.name]
                    with gzip.open(f, "rb") as src, ipa.open(entry(arc), "w", force_zip64=True) as dst:
                        shutil.copyfileobj(src, dst, 1 << 22)
                    print(f"unpacked {rel}")
                else:
                    add_file(f, GAME + rel)
    tmp.replace(out)
    check(out, unpack)


def check(path, unpack):
    """Reads the finished app back: every file the page asks for is there."""
    with zipfile.ZipFile(path) as ipa:
        bad = ipa.testzip()
        if bad:
            fail(f"{path} is damaged at {bad}")
        names = set(ipa.namelist())
        page = ipa.read(GAME + "index.html").decode("utf-8")
        wanted = set(re.findall(r'buildUrl \+ "/([^"]+)"', page))
        if not wanted:
            fail("couldn't find the Build files in the packed page")
        missing = sorted(w for w in wanted if GAME + "Build/" + w not in names)
        if missing:
            fail("the page asks for files that aren't in the app: " + ", ".join(missing))
        if any(n in page for n in unpack):
            fail("the page still names a .gz file")
        binary = ipa.getinfo(APP + "Overcooked")
        if not (binary.external_attr >> 16) & 0o111:
            fail("the app binary lost its executable flag")
        game_bytes = sum(i.file_size for i in ipa.infolist() if i.filename.startswith(GAME))
    size = path.stat().st_size
    print(f"wrote {path}: {size / 1048576:.0f} MB ({game_bytes / 1048576:.0f} MB of game files unpacked), "
          f"page loads {len(wanted)} build files, all present")


if __name__ == "__main__":
    main()
