"""Where things are on this PC: the Steam game, Unity, and the lab folder.

Every tool asks this module instead of hard-coding paths, so the repo works on
anyone's PC with their own copy of Overcooked.

Each path is taken from the first of these that gives one:
  1. an environment variable (OVERCOOKED_STEAM, OVERCOOKED_UNITY, OVERCOOKED_LAB)
  2. overcooked-port.json in the repo root (not committed; see the example file)
  3. auto-detection: Steam libraries for the game, Unity Hub folders for Unity
  4. the default (lab: C:\\OVERCOOKED-LAB)

    py tools\\portpaths.py           shows what was found
    py tools\\portpaths.py --save    writes what was found to overcooked-port.json
"""

import json
import os
import re
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
CONFIG = REPO / "overcooked-port.json"
UNITY_VERSION = "2022.3.62f3"
GAME_FOLDER = "Overcooked"
DEFAULT_LAB = r"C:\OVERCOOKED-LAB" if os.name == "nt" else str(Path.home() / "OvercookedLab")


def _config():
    try:
        return json.loads(CONFIG.read_text(encoding="utf-8"))
    except FileNotFoundError:
        return {}
    except ValueError as e:
        sys.exit(f"{CONFIG} is not valid JSON: {e}")


def _pick(env, key):
    return os.environ.get(env) or _config().get(key)


def _steam_roots():
    roots = []
    if os.name == "nt":
        try:
            import winreg
            for hive, sub, name in ((winreg.HKEY_CURRENT_USER, r"Software\Valve\Steam", "SteamPath"),
                                    (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                                    (winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Valve\Steam", "InstallPath")):
                try:
                    with winreg.OpenKey(hive, sub) as k:
                        roots.append(Path(winreg.QueryValueEx(k, name)[0]))
                except OSError:
                    pass
        except ImportError:
            pass
        roots.append(Path(r"C:\Program Files (x86)\Steam"))
    else:
        roots += [Path.home() / ".steam" / "steam", Path.home() / ".local" / "share" / "Steam",
                  Path.home() / "Library" / "Application Support" / "Steam"]
    return roots


def _steam_libraries():
    libs = []
    for root in _steam_roots():
        libs.append(root)
        vdf = root / "steamapps" / "libraryfolders.vdf"
        if vdf.exists():
            for m in re.finditer(r'"path"\s+"([^"]+)"', vdf.read_text(encoding="utf-8", errors="replace")):
                libs.append(Path(m.group(1).replace("\\\\", "\\")))
    seen, out = set(), []
    for lib in libs:
        key = str(lib).lower()
        if key not in seen:
            seen.add(key)
            out.append(lib)
    return out


def steam_game():
    """The Overcooked install folder (only read, never changed), or None."""
    picked = _pick("OVERCOOKED_STEAM", "steam")
    if picked:
        return Path(picked)
    for lib in _steam_libraries():
        game = lib / "steamapps" / "common" / GAME_FOLDER
        if game.exists():
            return game
    return None


def unity():
    """Unity.exe for UNITY_VERSION, or None."""
    picked = _pick("OVERCOOKED_UNITY", "unity")
    if picked:
        return Path(picked)
    if os.name != "nt":
        mac = Path(f"/Applications/Unity/Hub/Editor/{UNITY_VERSION}/Unity.app/Contents/MacOS/Unity")
        return mac if mac.exists() else None
    bases = [Path(os.environ.get("ProgramFiles", r"C:\Program Files")) / "Unity" / "Hub" / "Editor"]
    # Unity Hub's "install location" setting, when changed from the default.
    extra = Path(os.environ.get("APPDATA", "")) / "UnityHub" / "secondaryInstallPath.json"
    try:
        p = json.loads(extra.read_text(encoding="utf-8"))
        if p:
            bases.insert(0, Path(p))
    except (OSError, ValueError):
        pass
    for base in bases:
        exe = base / UNITY_VERSION / "Editor" / "Unity.exe"
        if exe.exists():
            return exe
    return None


def lab():
    """The lab folder: export, project, builds, served copies, logs, certificate."""
    return Path(_pick("OVERCOOKED_LAB", "lab") or DEFAULT_LAB)


def assetripper():
    name = "AssetRipper.GUI.Free.exe" if os.name == "nt" else "AssetRipper.GUI.Free"
    return lab() / "tools" / "AssetRipper" / name


def https_dir():
    return lab() / "https"


def main():
    found = {"steam": steam_game(), "unity": unity(), "lab": lab()}
    for key, path in found.items():
        state = "MISSING" if path is None or not Path(path).exists() else "ok"
        print(f"{key:6} {state:8} {path if path else '(not found; set it in ' + CONFIG.name + ')'}")
    ar = assetripper()
    print(f"{'ripper':6} {'ok' if ar.exists() else 'MISSING':8} {ar}")
    if "--save" in sys.argv[1:]:
        data = _config()
        data.update({k: str(v) for k, v in found.items() if v is not None})
        CONFIG.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
        print(f"saved to {CONFIG}")


if __name__ == "__main__":
    main()
