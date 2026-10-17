# OvercookedWebGL

This is ALL Claude. Feel free to not use it.

Play **your own copy** of Overcooked (2016, Ghost Town Games / Team17, Steam, Unity 5.5) in a web browser: in Chrome on your PC, and on an iPhone as a Home Screen app that also works offline.

This repo contains **no part of the game**. It holds only the recipe: a patch script, small replacement scripts, shaders, a web page and tools. You run them against the game files from your own Steam install, and everything is built on your own PC. Nothing from the game is (or may be) committed here: no art, sound, levels, decompiled code or builds. Don't share the builds you make either; they contain the game.

Every deliberate difference from the original game is listed in [notes/symbol-map.md](notes/symbol-map.md).

Overcooked is a trademark of Team17 / Ghost Town Games. This is an unofficial fan project, not affiliated with or endorsed by them. Licences: [LICENSE](LICENSE) for this repo's own code, [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for adapted code.

## What you need

- **Overcooked on Steam** (the original 2016 game, Windows version), installed. The tools only read it; they never change it.
- **Windows 10/11** PC with about **25 GB free** while building.
- **Python 3** (`py --version` in PowerShell should print 3.x).
- **Unity Hub** with **Unity 2022.3.62f3** and its **WebGL Build Support** module. A free Personal licence is enough; sign in to Unity Hub once.
- **AssetRipper 2.0.0** (free, Windows x64 release from its GitHub page), unzipped to `<lab>\tools\AssetRipper` (see below for `<lab>`).
- Chrome (desktop). For the phone: an iPhone on the same Wi-Fi.

## 1. Tell the tools where things are

The **lab** is a folder outside this repo that holds everything made from the game: the export, the Unity project, builds, logs and the phone certificate. It is `C:\OVERCOOKED-LAB` unless you choose another one.

```
py tools\portpaths.py
```

shows what was found: your Steam copy of Overcooked (found through Steam's library list), Unity 2022.3.62f3 (found through Unity Hub) and the lab. If something says MISSING, copy `overcooked-port.example.json` to `overcooked-port.json` and fix that path there, or set `OVERCOOKED_STEAM`, `OVERCOOKED_UNITY` or `OVERCOOKED_LAB`. Your `overcooked-port.json` stays on your PC (it is git-ignored).

## 2. Check your copy of the game

```
py tools\check_game.py
```

Before anything is built, this confirms you have a real Steam install of Overcooked. It checks that Steam installed it (the library's install record), that the game files are complete, and that it's the 2016 original (Unity 5.5), not the remaster. It only reads files. If a line says FAIL, it tells you what to do: usually install the game from your Steam library, or use Steam's "Verify integrity of game files". The build in step 3 runs the same check and stops if it fails.

## 3. Build it (one command)

```
py tools\rebuild_all.py --number 1 --no-serve
```

This runs every step in order, with logs in `<lab>\logs`:
1. **export**: AssetRipper reads your Steam install into `<lab>\export`.
2. **project**: copies the export to `<lab>\project`.
3. **patch**: `tools/apply_patches.py` adds this repo's code and makes small marked edits (`[webgl-port]`) so the game runs in a browser.
4. **import**: Unity opens and upgrades the project (the first time takes a long while).
5. **build**: the desktop build goes to `<lab>\project\Builds\WebGL1`.

For the phone build, add `--phone` (for example `py tools\rebuild_all.py --from build --phone --number 1 --no-serve`), which goes to `Builds\WebGL-Phone1`. `--dry-run` shows the commands without running them, `--from <stage>` restarts from a stage and `--clean` redoes the export.

Copy the finished build to its own folder, for example `<lab>\served\build1`, so you can keep playing it while you build the next one.

## 4. Play on the PC

```
py tools\serve.py C:\OVERCOOKED-LAB\served\build1 8000
```

Open http://localhost:8000/ in Chrome and click to play. Saves are kept in Chrome's storage for that address, so don't clear its site data. Keyboard and PS4 controllers both work (see below).

## 5. Play on an iPhone (Home Screen app, works offline)

Safari only allows the offline app over https, so the PC makes its own small certificate for your home network.

1. On the PC, once: `py port\mobile\https\make_phone_cert.py`. It writes `<lab>\https`; **keep that folder**, because your phone will trust exactly this certificate.
2. Serve the phone build: `py tools\serve.py <lab>\served\phone1 8002 --lan` and `py tools\serve.py <lab>\served\phone1 8443 --lan --https`. Each prints the address to use on the phone. Windows asks once to let Python onto private networks; allow it.
3. On the iPhone, in Safari: open `http://<pc-address>:8002/__ca.crt` and allow the profile download. Then go to Settings > General > VPN & Device Management and install it. Finally, under Settings > General > About > Certificate Trust Settings, turn on full trust for it. (Tapping past Safari's warning is not enough.)
4. Open `https://<pc-address>:8443/` in Safari, then Share > **Add to Home Screen**.
5. Open the new icon, let it load fully, close it, reopen it and wait for **"Ready to play with no connection."** From then on it plays with the PC off.

Phones start on the Super Low graphics preset; you can change it in Options.

**Updating the phone:** serve the new phone build on 8443 as in step 2, then on home Wi-Fi open the icon, close it, reopen it and wait for the same message. Don't delete the icon, because that deletes its saves.

## 6. Updating to a new version

When this repo has new commits, follow these steps. They apply whether you maintain the repo or just play.

1. **Get the changes:** `git pull`
2. **Check the game:** `py tools\check_game.py`. Steam updates can change the install.
3. **Build the PC version:** `py tools\rebuild_all.py --number <N> --no-serve`. Use a new number each time, for example the last one plus 1. If you still have `<lab>\export` and `<lab>\project`, add `--from patch` to skip the export (faster).
4. **Build the phone version** (if you play on a phone): `py tools\rebuild_all.py --from build --phone --number <N> --no-serve`
5. **Copy the results** next to the old ones:
   - `<lab>\project\Builds\WebGL<N>` to `<lab>\served\build<N>`
   - `<lab>\project\Builds\WebGL-Phone<N>` to `<lab>\served\phone<N>`
6. **Try the new PC build on another port first:** `py tools\serve.py <lab>\served\build<N> 8001`, then open http://localhost:8001/. If it plays, stop the old server on 8000 (Ctrl+C in its window) and start `py tools\serve.py <lab>\served\build<N> 8000`.
7. **Phone:** stop the old phone servers and start the new build on 8002 (`--lan`) and 8443 (`--lan --https`), as in section 5. On the iPhone, on home Wi-Fi, open the Home Screen icon, close it, reopen it and wait for "Ready to play with no connection." Saves stay; don't delete the icon.
8. **Tidy up:** once the new version works, delete the older `served\build*` and `served\phone*` folders. Keep `<lab>\https`.

Saves live in the browser and the phone app, so they carry over to every new build.

## Maintaining this repo

For whoever looks after this repository and makes changes to it.

**The one rule: nothing from the game goes in.** Never commit:
- files from the Steam install, the AssetRipper export or the Unity project;
- decompiled code (beyond the short anchor lines described below);
- builds, screenshots or videos of the game;
- your own `overcooked-port.json`;
- the `<lab>\https` certificate folder.

`.gitignore` blocks the usual suspects, but check `git status` before every commit anyway.

**Where changes go:**
- Our own behaviour goes in new files under `port/`. `tools/apply_patches.py` copies them into the project.
- Edits to the game's own scripts go only through `tools/apply_patches.py`. Each one finds its spot with a short anchor of one or two lines and inserts code marked `[webgl-port]`, with a comment saying why. Running it twice must change nothing.
- Every deliberate difference from the original game gets a row in `notes/symbol-map.md`.

**Test a change:**
```
py tools\rebuild_all.py --from patch --number <N> --no-serve
py tools\serve.py C:\OVERCOOKED-LAB\project\Builds\WebGL<N> 8001
```
`--from patch` reuses the existing export and Unity project, so only patch, import and build run. Add `--phone` for the phone build. Play it on port 8001 and check the browser console for `[webgl-port]` lines and errors. Keep the build you normally play on 8000 until the new one is checked.

**When something breaks:**
- *Steam updated the game:* if an anchor no longer matches, `apply_patches.py` stops and says which patch and file. Find the new spot in the export and update that anchor. `tools/check_game.py` also confirms the install is still the Unity 5.5 version.
- *Moving to a newer Unity:* change `UNITY_VERSION` in `tools/portpaths.py`, then rebuild with `--clean` and fix whatever the import reports.
- *A new AssetRipper:* stay on 2.0.0 unless there is a reason to move. Its headless web calls are in `stage_export` in `tools/rebuild_all.py`.

**Before you push:** search the changes for anything personal: your name or username, home folder paths, your Wi-Fi address, emails, keys. Keep test switches off by default; they only turn on with a parameter in the page address. Never upload a build anywhere: it contains the game.

**Releases** are just tagged commits of this code. People build them from their own copy using the steps above.

## What's where

| Path | What it is |
|---|---|
| `tools/check_game.py` | Confirms a real Steam install of Overcooked (run first) |
| `tools/rebuild_all.py` | Steam install → playable build, in one command |
| `tools/portpaths.py` | Finds Steam, Unity and the lab; reads `overcooked-port.json` |
| `tools/apply_patches.py` | Turns an AssetRipper export into a project that builds for WebGL (idempotent) |
| `tools/serve.py` | Local server with the headers Unity's compressed builds need; `--lan`, `--https` for the phone |
| `port/scripts/` | Our scripts the patched game calls (saves, lighting, input, platform fixes, test switches) |
| `port/replacements/` | Our stand-ins for Steamworks.NET and XInput's native imports |
| `port/shaders/` | Replacement shaders (the exporter can't recover the game's own) |
| `port/input/` | PS4 / standard gamepad support in the browser |
| `port/mobile/` | Phone build, phone page, offline app, certificate tool; see its README |
| `port/editor/` | Unity menu **Overcooked Port**: WebGL settings and one-click builds |
| `port/template/` | The desktop web page |
| `notes/symbol-map.md` | Every change from the original game and why |

## Test switches

Add these to the address, e.g. `http://localhost:8000/?unlockall=1&level=1-1`. `unlockall=1` opens every level (needs a save that reached the world map), and `level=X-Y` jumps straight to a level. More are listed in `notes/symbol-map.md`.

## Keyboard

| Action | One player | Two on one keyboard (left / right) |
|---|---|---|
| Move | WASD or arrows | WASD / arrows |
| Pick up / put down | Space | Space / `/` |
| Chop / throw | E or Ctrl | E or Left Ctrl / Right Ctrl |
| Dash | Q or Alt | Q or Left Alt / Right Alt |
| Emote | T | T / `.` |
| Switch chef | Shift | Left Shift / Right Shift |
| Pause | Esc (Enter = Start) | |

E and Q are added by this port: Chrome won't let a page block Ctrl+W, so Ctrl (chop) plus W (up) could close the tab in a normal window. In fullscreen the page locks the keyboard, so the original Ctrl/Alt keys are safe there too.
