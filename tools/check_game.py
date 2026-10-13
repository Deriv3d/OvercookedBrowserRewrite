"""Check that this PC has a real Steam install of Overcooked before building.

    py tools\\check_game.py

The port is built from your own copy of the game, so the first step checks
that copy. It only reads files and changes nothing. It checks:
  1. the game folder was found (tools/portpaths.py: Steam libraries or your setting)
  2. Steam installed it: the library has an appmanifest for a game in that folder
  3. the game files are there: Overcooked.exe, Overcooked_Data, the game's code
  4. it is the Unity 5.5 version of Overcooked (the 2016 original, not the remaster)

tools/rebuild_all.py runs the same check before it exports anything.
"""

import re
import sys
from pathlib import Path

import portpaths


def checks(game):
    """Yields (passed, text) for each check."""
    if game is None or not game.is_dir():
        yield False, ("Overcooked folder not found" + (f" at {game}" if game else "")
                      + ". Install Overcooked from your Steam library, or set its folder in overcooked-port.json")
        return
    yield True, f"Overcooked folder: {game}"

    steamapps = game.parent.parent
    manifests = []
    for acf in steamapps.glob("appmanifest_*.acf"):
        text = acf.read_text(encoding="utf-8", errors="replace")
        m = re.search(r'"installdir"\s+"([^"]+)"', text)
        if m and m.group(1).lower() == game.name.lower():
            manifests.append(acf.name)
    yield (bool(manifests),
           f"Installed by Steam ({manifests[0]})" if manifests
           else f"No Steam install record for this folder in {steamapps}. Install the game through Steam")

    data = game / "Overcooked_Data"
    needed = [game / "Overcooked.exe", data, data / "Managed" / "Assembly-CSharp.dll"]
    missing = [p.name for p in needed if not p.exists()]
    yield (not missing,
           "Game files present" if not missing
           else "Missing " + ", ".join(missing) + ". In Steam: Overcooked > Properties > Installed Files > Verify integrity")

    version = None
    for name in ("globalgamemanagers", "mainData", "data.unity3d"):
        f = data / name
        if f.exists():
            with open(f, "rb") as fh:
                m = re.search(rb"(\d{1,4}\.\d+\.\d+[abfp]\d+)", fh.read(4096))
            if m:
                version = m.group(1).decode()
                break
    yield (bool(version and version.startswith("5.5.")),
           f"Unity {version}, the original Overcooked" if version and version.startswith("5.5.")
           else f"Unity version {version or 'not found'}; this port needs the 2016 Overcooked (Unity 5.5), "
                "not Overcooked! All You Can Eat")


def check(game=None, quiet=False):
    ok = True
    for passed, text in checks(game if game is not None else portpaths.steam_game()):
        ok &= passed
        if not quiet or not passed:
            print(("  ok    " if passed else "  FAIL  ") + text)
    return ok


def main():
    print("Checking your copy of Overcooked:")
    if check():
        print("All good: you can build the port (py tools\\rebuild_all.py).")
    else:
        sys.exit("Fix the FAIL lines above, then run this again.")


if __name__ == "__main__":
    main()
