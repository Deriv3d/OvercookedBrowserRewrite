# Phone optimisation framework (port/mobile)

Goal: Overcooked runs on iPhone 13 and newer (tested on recent iPhones) without changing what the game looks or plays like. This folder is the scaffolding: a separate phone build, a phone page, and the measuring tools. The actual cuts (per-level loading, warm-up collections, pooling hot spots) plug into it later, each measured with it first.

## Budgets

| Budget | Value | Where it's enforced or checked |
|---|---|---|
| WebAssembly heap | under 350 MB | `WebGLPortPhone.HeapBudgetMB`; perf lines say `over=heap`. Hard cap 512 MB in the phone build for now (`WebGLPortPhoneBuild.MaximumMemoryMB`), 384 once levels load one at a time |
| Pixel density | at most 1.5 on phones | phone page `config.devicePixelRatio`; `?dpr=` overrides |
| Draw calls | under 100 per frame | `WebGLPortPhone.DrawCallBudget`; perf lines say `over=draws`. A target per level, not forced: cutting lights would change the look |
| Textures | ASTC | phone build only |
| Frame rate | steady 60 FPS | perf lines: `over=fps` under 55, `over=hitch` for frames over 25 ms, `over=sustained` after 10 s in a row under 55 (usually heat throttling; Safari exposes no GPU load or temperature) |
| Per-frame GC allocations | none in hot paths | perf lines count frames that allocated; fixes go through `apply_patches.py`, one hot spot at a time |

## What's here

| File | Copied to (by `tools/apply_patches.py`) | What it does |
|---|---|---|
| `scripts/WebGLPortPhone.cs` | game scripts `WebGLPort/Mobile/` | Decides once at start-up: phone or not, perf level, warm-up on/off. Logs `[webgl-port] phone profile ...` |
| `scripts/WebGLPortPerfMonitor.cs` | same | Logs `[webgl-port] perf ...` every second (fps, slowest frame, heap, draw calls, batches, SetPass, triangles, GC bytes and allocating frames, budgets exceeded) and a `perf-summary` per scene. Allocates nothing per frame |
| `scripts/WebGLPortDrawReport.cs` | same | `?drawreport=1`: logs what each scene draws 1 s and 5 s after loading (visible renderers, submeshes, static-batched, shadow casters, per camera, per shader, per top-level object), and at 5 s what is loaded: texture and mesh memory (estimated) and audio size once decoded. Reading only |
| `scripts/WebGLPortShaderWarmup.cs` | same | Warms up `Resources/WebGLPortWarmup/<scene>` and `_shared` ShaderVariantCollections when a scene loads, so Safari compiles shaders on the loading screen, not mid-level |
| `plugins/OvercookedPhone.jslib` | `Assets/Plugins/WebGL/` | Phone detection, URL switches, heap size, on-screen perf box |
| `editor/WebGLPortPhoneBuild.cs` | `Assets/WebGLPort/Editor/` | Menu **Overcooked Port > Build WebGL (Phone)** → `Builds/WebGL-Phone`; restores every setting afterwards. Also logs the shader variant count after every WebGL build |
| `template/index.html`, `template/manifest.json`, `template/sw.js`, `template/saves.js` | `Assets/WebGLTemplates/OvercookedPhone/` | Phone page (and its offline copy, below): fills the screen inside the notch, 16:9 with side bars like the desktop page, no zoom/scroll/long-press, DPR cap, tap to start (unlocks audio, plays through the silent switch), turn-sideways notice, Home Screen fullscreen, `?log=1` |

### Phones start on Super Low

`WebGLPortPhone` sets quality level 0 (SuperQuick, "Super Low" in Options) on phones before the first scene, but only when the save has no graphics choice yet (`Quality` / `UnityGraphicsQuality` in PlayerPrefs). Whatever the player picks in Options is kept. Log line: `[webgl-port] phone graphics: ...`. Desktop keeps Standard.

### Phone build settings (differences from the desktop build)

| Setting | Phone | Why | Look changes? |
|---|---|---|---|
| Texture compression | ASTC | iPhones can't use DXT; Safari would unpack it to raw RGBA | No |
| Template | OvercookedPhone | phone page above | No |
| Heap | initial 256 MB, max 512 MB, geometric growth 20 % capped at 32 MB | starts above the measured peak (216 MB) so scene loads don't grow it; growing briefly needs old + new heap | No |
| Power preference | HighPerformance | full GPU clocks | No |
| File names | hashed | no stale cached files after a rebuild; lets the offline copy drop an old build's files | No |
| Compression | Gzip (desktop: Brotli) | browsers take Brotli only over https; the first phone tests run over plain http on the home network | No |
| Unity data caching | off | the offline copy (sw.js) already stores the files; Unity's cache would store the data file twice | No |
| IL2CPP code generation | OptimizeSize | smaller code, less heap | No |
| Audio clips 20 s or longer | not preloaded: load on first play, freed when nothing plays them (`WebGLPortAudioUnload`, 5 s after each scene change; `?audiounload=0` turns it off) | Web Audio holds every loaded clip as 32-bit PCM outside the heap: 239 MB on the rooftop, where the iPhone died. Unity 2022 refuses CompressedInMemory for WebGL. Now about 140 MB on the rooftop, 76 MB on the world map | No; a long track can start a moment late the first time |
| Lightmap shader variants | stripped | the game ships no lightmaps | No |
| Optimize Mesh Data | off (`OptimizeMeshData` switch) | could break scripts that read vertex channels; turn on only after a side-by-side check | — |

Note: switching to ASTC re-imports every texture, and the desktop build switches back afterwards. Unity keeps both in its cache, so only the first phone build pays the full import.

## Finding the slow part

First phone run: about 13 FPS. Suspects, biggest first (not verified until measured):
1. Per-pixel lights: the port lifts the light limit to 64 while the game asks for deferred lighting (WebGLPortDeferredLighting). In Forward, every light redraws each object it touches, so a kitchen with many lights draws everything many times.
2. Full-screen camera effects: SSAO at the Standard preset, plus bloom, depth of field and FXAA.
3. Realtime shadows.
4. Fill rate (render density).

Test switches, each changes the look, so for measuring only (`scripts/WebGLPortPhoneExperiments.cs`, plus existing ones):

| Switch | Turns off |
|---|---|
| `?pixellights=4` | all but the 4 most important per-pixel lights |
| `?shadows=0` | realtime shadows |
| `?fx=-SSAO` (or `-SSAOPro`, `-Bloom`, ...; names in the `diag camera` log line) | that camera effect |
| `?dpr=0.75` | renders at half the density |

The phone page also logs each pad Safari reports (`[page] gamepad connected ... id=...`), for the controller profile.

## Battery and heat

Levers that don't change the look:
- Fewer pixels: the 1.5x density cap already cuts GPU work by about half compared with the iPhone's native 3x (estimate from pixel counts: 2.25 vs 9 pixels per point).
- No drawing when nobody's looking: Safari stops `requestAnimationFrame` for a hidden tab or a backgrounded Home Screen app, so the game doesn't render then (built into the browser; nothing to add).
- Less waste per frame: per-level loading, shader warm-up, stripped variants and GC fixes all lower CPU/GPU time per frame, which is also battery.
- No more than 60: Safari's frame loop runs at 60 on iPhones (Pro models too, unless a hidden Safari setting is changed), so the game never renders extra frames.

Opt-in only (changes smoothness, so never the default): `?fps=30` caps the frame rate for long sessions on battery. If the game sets its own frame rate later in a session, its value wins.

Not done on purpose: lowering the frame rate in menus or the pause screen (animated menus would look different from Steam).

## Offline play

After one full load from a secure address (https, or `localhost` on the PC), the phone page keeps a copy of the build on the device (`template/sw.js`, a service worker). After that, the Home Screen icon opens the game with no connection and no PC.

- The page itself: network first (an online visit always gets the newest build), cached copy when offline.
- Build files: cache first. Names are hashed, so a new build's files are new entries; once a new build has loaded, the page removes the old build's files.
- Not cached: `/__*` (serve.py's log/test routes), non-GET and range requests.
- On plain `http://` (for example the PC's address on the home network) Safari doesn't allow it; the page then loads from the network as before.
- `?offline=0` removes the copy and turns it off for that visit.
- The page asks Safari for persistent storage (`navigator.storage.persist()`).

**If the copy is cleared.** The copy and the game's saves live in the same site storage. A Home Screen app isn't subject to Safari's 7-day clean-up of websites, but iOS can still clear it when the phone runs very low on storage, and it's gone if the player clears Safari's website data or deletes the Home Screen icon. Then:
- online: the next visit downloads the game again (one full load) and it is offline-ready again;
- offline: the page says there is no saved copy and asks to open it once while online;
- saves are lost along with it, unless the player made a backup (next section).

Tested in headless Chromium with a stand-in build (fake loader, data, framework, wasm) served by `tools/serve.py`: first online load stored all files and the page; a fresh offline visit opened from the copy; a renamed data file (new build) replaced the old one in the copy; `?offline=0` cleared it. Not yet tried in iOS Safari.

### https at home (offline Home Screen app)

Safari keeps the offline copy only for an https address. `https/make_phone_cert.py` makes a private home CA and a server certificate with Python's standard library alone (into `<lab>\https`; the CA key never leaves it). `tools/serve.py --https` serves with that certificate, and `/__ca.crt` hands the phone the CA to install. Phone steps: [the main README](../../README.md), section 5.

Tested here: openssl verifies the chain (`-x509_strict`), serve.py with `--https` answers https only with the CA trusted, `/__ca.crt` returns the CA. Not yet tried on an iPhone.

## Saves: back up and restore

The saves and options sit in the same site storage as the offline copy, so the phone start screen has **Back up saves** and **Restore saves** (`template/saves.js`).

- Back up: reads the game's save folder (Unity's IDBFS: IndexedDB `/idbfs`, store `FILE_DATA`) into one `overcooked-saves-<date>.json` and opens the iPhone Share sheet (Save to Files, iCloud Drive, AirDrop, Mail). Browsers without file sharing download it instead. The start screen shows when the last backup was made.
- Restore: checks the file is an Overcooked backup with paths only inside `/idbfs/`, asks to confirm, parks it, reloads, and writes it before Unity starts (never while the game runs, which would write its own copy back on the next save). It replaces all saves on the device.
- Works offline; nothing is sent anywhere.
- A backup made at one address restores at another (plain http link to the https Home Screen app): Unity names the save folder after the page's address (`/idbfs/<md5 of the folder URL>`), so restore moves the files into this address's folder. Tested in headless Chromium: a backup under md5("http://192.168.1.20:8002") restored under the test address's folder, byte for byte.
- Desktop page: same backup/restore.

Tested in headless Chromium with a stand-in build and IDBFS records shaped like Emscripten's: backup file had all 3 records, a non-backup file was refused, restore after the saves were wiped gave back identical records. To verify on the real build: that Unity 2022 keeps both the save files and PlayerPrefs in `/idbfs` (expected, unverified), and the Share sheet on iOS.

## How to run

- Phone build: Unity menu **Overcooked Port > Build WebGL (Phone)**, or batchmode `-executeMethod WebGLPortPhoneBuild.BuildFromCommandLine`.
- Serve it: `py tools\serve.py C:\OVERCOOKED-LAB\project\Builds\WebGL-Phone 8002`.
- Try it on the PC first: `http://localhost:8002/?phone=1&perf=2`. Expected: the "Tap to play" screen, then the game with a small perf box in the top-left corner and `[webgl-port] perf` lines in the console once a second.
- On the desktop build, `?perf=1` or `?perf=2` gives the same perf lines (phone detection only changes the defaults).

### URL switches

| Switch | Effect |
|---|---|
| `?phone=0` / `?phone=1` | override phone detection |
| `?perf=0/1/2` | no perf lines / lines every second (phone default) / lines plus on-screen box |
| `?warmup=0` | skip shader warm-up, to measure what it saves |
| `?dpr=1.5` | render pixel density (phone page) |
| `?fps=30` | battery saver, opt-in: frame-rate cap |
| `?pixellights=N`, `?shadows=0` | test only: light limit, no shadows (see "Finding the slow part") |
| `?drawreport=1` | what each scene draws, for cutting draw calls (world map first) |
| `?offline=0` | phone page: remove the offline copy and don't keep one this visit |
| `?log=1` | phone page sends every console line to `serve.py`'s `/__log` (`<test folder>/phone-<time>/browser.log`) |

Counters a release build doesn't record (draw calls and GC in some Unity versions) show `-1`; a Development build has them all.

## Next pieces (not built yet)

1. Per-level loading (biggest memory win).
2. Warm-up collections recorded per level in the editor.
3. Touch controls in `#touch-layer` (as an InControl device).
4. Music streamed through an `<audio>` element if Unity holds it decoded.
5. GC hot spots from the perf lines, patched one at a time.
