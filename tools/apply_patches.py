#!/usr/bin/env python3
"""Turn an AssetRipper export of Overcooked into a project that builds for WebGL.

Usage:
    python tools/apply_patches.py <exported-project-root>

<exported-project-root> is the folder that contains Assets/ and ProjectSettings/
(for example C:\\OVERCOOKED-LAB\\project). The script edits that folder in place;
nothing from it is copied back into this repo. Running it twice is safe: every
edit is marked with "[webgl-port]" and skipped when already present.

What it does (details in notes/symbol-map.md):
  1. Replaces Steamworks.NET with a stub (the browser can't load steam_api.dll).
  2. Replaces XInputDotNetPure's native imports with "no pad connected" on WebGL.
  3. Copies this repo's own scripts, editor tools, WebGL plugin, page template
     and link.xml into the project.
  4. Removes the Windows-only native plugins.
  5. Makes small anchored edits to game scripts (file:// URLs, save flushing).
"""

import re
import zlib
import shutil
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parent.parent
PORT = REPO / "port"
MARK = "[webgl-port]"

NATIVE_PLUGINS = {"CSteamworks.dll", "steam_api.dll", "steam_api64.dll",
                  "XInputInterface32.dll", "XInputInterface64.dll", "XInputInterface.dll"}


class PatchError(Exception):
    pass


def find_one(root: Path, name: str, must_contain: str = "") -> Path:
    hits = [p for p in root.rglob(name)
            if must_contain in p.read_text(encoding="utf-8", errors="replace")]
    if len(hits) != 1:
        raise PatchError(f"expected exactly one {name}"
                         + (f" containing {must_contain!r}" if must_contain else "")
                         + f", found {len(hits)}: {[str(h) for h in hits]}")
    return hits[0]


def remove_with_meta(path: Path) -> None:
    meta = path.with_name(path.name + ".meta")
    if path.is_dir():
        shutil.rmtree(path)
    elif path.exists():
        path.unlink()
    if meta.exists():
        meta.unlink()


def copy_file(src: Path, dst: Path) -> None:
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(src, dst)
    print(f"  copied  {src.relative_to(REPO)} -> {dst}")


def anchor_regex(anchor: str) -> re.Pattern:
    # Exact text, but any run of whitespace in the anchor matches any run of
    # whitespace in the file, so formatting differences between decompilers
    # (tabs vs spaces, brace placement) don't break the match.
    parts = [re.escape(p) for p in anchor.split()]
    return re.compile(r"\s+".join(parts))


def edit(path: Path, anchor: str, replacement: str, label: str, count: int = 1) -> None:
    text = path.read_text(encoding="utf-8")
    if f"{MARK} {label}" in text:
        print(f"  skipped {path.name}: {label} (already applied)")
        return
    rx = anchor_regex(anchor)
    found = len(rx.findall(text))
    if found != count:
        raise PatchError(f"{path}: anchor for '{label}' matched {found} times, expected {count}.\n"
                         f"  anchor: {anchor!r}")
    text = rx.sub(lambda m: replacement.replace("{MARK}", f"{MARK} {label}"), text)
    path.write_text(text, encoding="utf-8")
    print(f"  patched {path.name}: {label}")


def main(project: Path) -> None:
    assets = project / "Assets"
    if not assets.is_dir():
        raise PatchError(f"{project} has no Assets folder; point this at the exported project root")

    print("Locating script folders")
    steam_player = find_one(assets, "SteamPlayerManager.cs", "class SteamPlayerManager")
    game_dir = steam_player.parent
    stubbed = list(assets.rglob("SteamworksStub.cs"))
    if stubbed:
        steamworks_dir = stubbed[0].parent
    else:
        steamworks_dir = find_one(assets, "SteamAPI.cs", "class SteamAPI").parent
    # The game carries XInputDotNetPure twice: as its own DLL and compiled into
    # Assembly-CSharp. AssetRipper exports both with no asmdefs, so Unity would
    # compile them into one assembly and fail on duplicate types. Keep the copy
    # inside the game scripts (the one Assembly-CSharp was built with) and
    # remove the standalone one.
    standalone_xinput = assets / "Scripts" / "XInputDotNetPure"
    if standalone_xinput.is_dir() and (game_dir / "XInputDotNetPure").is_dir():
        remove_with_meta(standalone_xinput)
        print(f"  removed duplicate {standalone_xinput}")
    xinput_imports = [p for p in assets.rglob("Imports.cs")
                      if "XInputGamePadGetState" in p.read_text(encoding="utf-8", errors="replace")]
    if not xinput_imports:
        raise PatchError("no XInputDotNetPure Imports.cs found")
    print(f"  game scripts:   {game_dir}")
    print(f"  Steamworks.NET: {steamworks_dir}")
    for p in xinput_imports:
        print(f"  XInput imports: {p}")

    print("1. Steamworks.NET -> stub")
    stub = PORT / "replacements" / "Steamworks" / "SteamworksStub.cs"
    if (steamworks_dir / "SteamworksStub.cs").exists() and not (steamworks_dir / "SteamAPI.cs").exists():
        print("  skipped (already replaced)")
    else:
        for f in list(steamworks_dir.iterdir()):
            remove_with_meta(f)
        copy_file(stub, steamworks_dir / "SteamworksStub.cs")

    print("2. XInput native imports -> WebGL-safe version (duplicate copy removed above)")
    for p in xinput_imports:
        copy_file(PORT / "replacements" / "XInputDotNetPure" / "Imports.cs", p)

    print("3. Port files")
    for src in sorted((PORT / "scripts").glob("*.cs")):
        copy_file(src, game_dir / "WebGLPort" / src.name)
    # Browser gamepad profiles for InControl (port/input/README.md).
    for src in sorted((PORT / "input").glob("*.cs")):
        copy_file(src, game_dir / "WebGLPort" / "Input" / src.name)
    for src in sorted((PORT / "editor").glob("*.cs")):
        copy_file(src, assets / "WebGLPort" / "Editor" / src.name)
    # Port scripts deleted from the repo (e.g. PadDiagnostics.cs)
    # would otherwise stay in the project, since copying never removes.
    for folder, source in ((game_dir / "WebGLPort", PORT / "scripts"), (game_dir / "WebGLPort" / "Input", PORT / "input")):
        for old in sorted(folder.glob("*.cs")) if folder.is_dir() else []:
            if not (source / old.name).exists():
                old.unlink()
                meta = old.with_name(old.name + ".meta")
                if meta.exists():
                    meta.unlink()
                print(f"  removed {old} (no longer in {source.relative_to(REPO)})")
    for src in sorted((PORT / "plugins" / "WebGL").glob("*")):
        copy_file(src, assets / "Plugins" / "WebGL" / src.name)
    for src in sorted((PORT / "template").rglob("*")):
        if src.is_file():
            copy_file(src, assets / "WebGLTemplates" / "Overcooked" / src.relative_to(PORT / "template"))
    # The desktop page uses the phone page's save backup/restore script as is.
    copy_file(PORT / "mobile" / "template" / "saves.js", assets / "WebGLTemplates" / "Overcooked" / "saves.js")
    # Gamepad name/layout logger (port/input/pad-log.js) on both pages.
    for page in ("Overcooked", "OvercookedPhone"):
        copy_file(PORT / "input" / "pad-log.js", assets / "WebGLTemplates" / page / "pad-log.js")
    # Phone optimisation framework (port/mobile/README.md): profile, perf
    # monitor, shader warm-up, phone build menu and the phone page template.
    mobile = PORT / "mobile"
    for src in sorted((mobile / "scripts").glob("*.cs")):
        copy_file(src, game_dir / "WebGLPort" / "Mobile" / src.name)
    for src in sorted((mobile / "editor").glob("*.cs")):
        copy_file(src, assets / "WebGLPort" / "Editor" / src.name)
    for src in sorted((mobile / "plugins").glob("*.jslib")):
        copy_file(src, assets / "Plugins" / "WebGL" / src.name)
    for src in sorted((mobile / "template").rglob("*")):
        if src.is_file():
            copy_file(src, assets / "WebGLTemplates" / "OvercookedPhone" / src.relative_to(mobile / "template"))
    copy_file(PORT / "link.xml", assets / "link.xml")
    # Hand-written shaders replace AssetRipper's dummies (Free edition can't
    # decompile them). Same file name and shader name, so materials keep their
    # links; the dummy's .meta (and GUID) stays untouched.
    for src in sorted((PORT / "shaders").glob("*.shader")):
        hits = list(assets.rglob(src.name))
        if len(hits) != 1:
            raise PatchError(f"shader {src.name}: expected one file in the project to replace, found {len(hits)}")
        copy_file(src, hits[0])
    # Our own extra shaders (not replacements), loaded by name from Resources.
    for src in sorted((PORT / "shaders" / "added").glob("*.shader")):
        copy_file(src, assets / "WebGLPort" / "Resources" / src.name)

    print("4. Native plugins and stray assembly folders")
    # AssetRipper exports two one-file folders holding only an AssemblyInfo.cs
    # for System.Runtime.* reference assemblies. With no asmdefs they compile
    # into Assembly-CSharp and clash with its own AssemblyVersion (CS0579).
    for name in ("System.Runtime.CompilerServices.Unsafe", "System.Runtime.InteropServices"):
        stray = assets / "Scripts" / name
        if stray.is_dir():
            remove_with_meta(stray)
            print(f"  removed {stray}")
    for p in list(assets.rglob("*.dll")):
        if p.name in NATIVE_PLUGINS:
            remove_with_meta(p)
            print(f"  removed {p}")

    print("5. Game script edits")
    # AssetBundleManager builds file:// URLs outside the editor; a browser page
    # needs the http(s) StreamingAssets URL instead.
    edit(find_one(assets, "AssetBundleManager.cs", "GetStreamingAssetsPath"),
         "private static string GetStreamingAssetsPath() {",
         "private static string GetStreamingAssetsPath()\n\t{\n"
         "#if UNITY_WEBGL && !UNITY_EDITOR // {MARK}\n"
         "\t\treturn UnityEngine.Application.streamingAssetsPath;\n"
         "#endif",
         "streaming-assets-url")

    # The DLC manager preloads its bundle manifest from a file:/// URL.
    edit(find_one(assets, "SteamDLCManager.cs", "class SteamDLCManager"),
         '"file:///" + Application.streamingAssetsPath + "/"',
         "WebGLPort.StreamingAssetsUrl() /* {MARK} */",
         "dlc-preload-url")

    # Saves: push to IndexedDB after every write/delete so a reload keeps them.
    save = find_one(assets, "PCSaveManager.cs", "class PCSaveManager")
    edit(save, "streamWriter.Close();",
         "streamWriter.Close();\n\t\tWebGLPort.FlushSaves(); // {MARK}",
         "flush-after-save")
    edit(save, "File.Delete(GetFileAddress(ModeToType(_mode), _slot, _dlcNumber));",
         "File.Delete(GetFileAddress(ModeToType(_mode), _slot, _dlcNumber));\n"
         "\t\tWebGLPort.FlushSaves(); // {MARK}",
         "flush-after-delete")
    edit(save, "DestroyMetaSession(); CreateMetaSession();",
         "WebGLPort.FlushSaves(); // {MARK}\n\t\tDestroyMetaSession();\n\t\tCreateMetaSession();",
         "flush-after-delete-all")

    # Keyboard: Chrome won't let a page block Ctrl+W / Ctrl+T / Ctrl+Tab, and
    # the game's keyboard layout uses Left Ctrl (chop/throw) next to W (up) and
    # T (emote), so a chop while walking up can close the tab. Add E and Q as
    # extra keys for the Left Ctrl and Left Alt actions; the originals still
    # work, and the page template locks the keyboard in fullscreen.
    actions = find_one(assets, "StandardActionSet.cs", "ModifiyForCombinedKeyboard")
    edit(actions,
         "actionSet.ButtonActions[ControlPadInput.Button.X].AddDefaultBinding(Key.Control);",
         "actionSet.ButtonActions[ControlPadInput.Button.X].AddDefaultBinding(Key.Control);\n"
         "\t\tactionSet.ButtonActions[ControlPadInput.Button.X].AddDefaultBinding(Key.E); // {MARK}\n"
         "\t\tactionSet.ButtonActions[ControlPadInput.Button.B].AddDefaultBinding(Key.Q);",
         "keyboard-alt-keys-combined")
    edit(actions,
         "actionSet.ButtonActions[ControlPadInput.Button.DPadLeft].AddDefaultBinding(Key.LeftControl);",
         "actionSet.ButtonActions[ControlPadInput.Button.DPadLeft].AddDefaultBinding(Key.LeftControl);\n"
         "\t\tactionSet.ButtonActions[ControlPadInput.Button.DPadLeft].AddDefaultBinding(Key.E); // {MARK}\n"
         "\t\tactionSet.ButtonActions[ControlPadInput.Button.LTrigger].AddDefaultBinding(Key.E);\n"
         "\t\tactionSet.ButtonActions[ControlPadInput.Button.DPadRight].AddDefaultBinding(Key.Q);",
         "keyboard-alt-keys-split")

    # PCPlayerManager.Awake first switches off Windows sticky/filter keys
    # through user32.dll, then calls base.Awake(). In a browser the DLL call
    # throws DllNotFoundException, base.Awake() never runs, and no key ever
    # counts as "press start". There are no such keys to switch off.
    edit(find_one(assets, "WindowsAccessibility.cs", "class WindowsAccessibility"),
         "public static void ToggleAccessibilityShortcutKeys(bool ReturnToStarting) {",
         "public static void ToggleAccessibilityShortcutKeys(bool ReturnToStarting)\n\t{\n"
         "#if UNITY_WEBGL && !UNITY_EDITOR // {MARK}\n"
         "\t\treturn;\n"
         "#endif",
         "no-user32-accessibility")

    # The DLC manager loads a "<platform>" bundle manifest, then DLC1's
    # session prefab from the dlc1-assets bundle. The Steam install ships only
    # the Windows manifest and no DLC bundles, and 5.5 Windows bundles
    # can't load in a 2022 WebGL player anyway, so on PC the result is: DLC1
    # absent, DLC2 from its serialized prefab. Go straight to that result
    # instead of a failing manifest download and its NullReferenceExceptions.
    edit(find_one(assets, "DLCManagerBase.cs", "class DLCManagerBase"),
         "protected virtual IEnumerator PreloadAssetBundleSession(string _path) {",
         "protected virtual IEnumerator PreloadAssetBundleSession(string _path)\n\t{\n"
         "#if UNITY_WEBGL && !UNITY_EDITOR // {MARK}\n"
         "\t\tm_sessionPrefabs = new GameObject[c_dlcCount];\n"
         "\t\tm_sessionPrefabs[1] = m_dlc2SessionPrefab;\n"
         "\t\tyield break;\n"
         "#endif",
         "dlc-no-bundles")

    # InControl only creates the device profiles named in this list. Browser
    # pads report Chrome's standard layout, which no stock profile maps, so
    # put the two WebGL profiles from port/input/ first. The decompiler wrote
    # the array with an explicit size; drop it so the longer list compiles.
    profiles = find_one(assets, "UnityInputDeviceProfileList.cs", "class UnityInputDeviceProfileList")
    edit(profiles, "new string[98]", "new string[] /* {MARK} */", "incontrol-profile-list-size")
    edit(profiles, '"InControl.AirFloWiredPS3MacProfile",',
         '/* {MARK} */ "OvercookedPort.WebGLDualShock4Profile", "OvercookedPort.WebGLStandardGamepadProfile",\n'
         '\t\t"InControl.AirFloWiredPS3MacProfile",',
         "incontrol-webgl-profiles")

    # Button icons inside text ("Press (A) / Space"): the game finds the
    # <quad> tag's vertices at charIndex * 4, which was right in Unity 5.5.
    # Since 2019.1 UI.Text makes no quads for spaces and rich-text tags, so
    # that index hits another character (the "/" vanished and the icon was
    # misplaced). Count only drawn characters instead.
    edit(find_one(assets, "EmbeddedTextImageLookupBase.cs", "class EmbeddedTextImageLookupBase"),
         "int num = match.Index * 4;",
         "int num = WebGLPort.QuadVertexIndex(m_text, match.Index); // {MARK}\n"
         "\t\t\tif (num + 4 > _helper.currentVertCount)\n"
         "\t\t\t{\n"
         "\t\t\t\treturn;\n"
         "\t\t\t}",
         "text-icon-quad-index")

    # Chopped lettuce/tomato/onion/mushroom have applyRootMotion on their
    # child Animator. Their clips carry near-zero RootT and a constant RootQ
    # (AssetRipper wrote them as float curves on the root path); 5.5 moved
    # nothing, but 2022.3 applies them and shifts the sliced mesh to the side
    # of the board, away from the item that is really there. Root
    # motion has no job on a chopped pile, so switch it off.
    edit(find_one(assets, "ChoppedIngredientAnimationDecisions.cs", "class ChoppedIngredientAnimationDecisions"),
         "m_animator = base.gameObject.GetComponentInChildren<Animator>();",
         "m_animator = base.gameObject.GetComponentInChildren<Animator>();\n"
         "\t\tif (m_animator != null)\n"
         "\t\t{\n"
         "\t\t\tm_animator.applyRootMotion = false; // {MARK}\n"
         "\t\t}",
         "chopped-no-root-motion")

    # Deeds feed the Steam achievements. WorkableItem.DoWork fires
    # IngredientChopped after swapping in the chopped item but before it
    # destroys the whole one, so an exception in the deed path on WebGL
    # leaves the old item lying next to the board where nothing can use it.
    # Achievements aren't wanted yet, so a failing deed is logged and
    # skipped; the deed code itself is unchanged.
    edit(find_one(assets, "GameUtils.cs", "class GameUtils"),
         "public static void FireDeed<T>(ControlPadInput.PadNum _player, params object[] _params) "
         "where T : DeedManagerBase.Deed {",
         "public static void FireDeed<T>(ControlPadInput.PadNum _player, params object[] _params) "
         "where T : DeedManagerBase.Deed\n\t{\n"
         "#if UNITY_WEBGL && !UNITY_EDITOR // {MARK}\n"
         "\t\ttry\n"
         "\t\t{\n"
         "\t\t\tFireDeedUnguarded<T>(_player, _params);\n"
         "\t\t}\n"
         "\t\tcatch (Exception e)\n"
         "\t\t{\n"
         "\t\t\tDebug.LogWarning(\"[webgl-port] deed \" + typeof(T).Name + \" skipped: \" + e);\n"
         "\t\t}\n"
         "\t}\n\n"
         "\tprivate static void FireDeedUnguarded<T>(ControlPadInput.PadNum _player, params object[] _params) "
         "where T : DeedManagerBase.Deed\n\t{\n"
         "#endif",
         "deed-failure-not-fatal")
    # Every chop and every served order fires a deed, and on WebGL each one
    # threw (TargetException) and was logged with its stack trace: a visible
    # FPS drop on the iPhone each time an order was turned in. So
    # deeds return at once unless ?deeds=1 (WebGLPortDeeds); the try/catch
    # above still guards that case.
    edit(find_one(assets, "GameUtils.cs", "class GameUtils"),
         "#if UNITY_WEBGL && !UNITY_EDITOR // [webgl-port] deed-failure-not-fatal\n\t\ttry",
         "#if UNITY_WEBGL && !UNITY_EDITOR // [webgl-port] deed-failure-not-fatal\n"
         "\t\tif (!WebGLPortDeeds.Enabled) // {MARK}\n"
         "\t\t{\n"
         "\t\t\treturn;\n"
         "\t\t}\n"
         "\t\ttry",
         "deeds-off")

    # Choices made for this port, not original behaviour:
    # - Music volume starts at 20 (of 100) on a fresh profile. The game's own
    #   Options > Audio slider still changes it and its saved value wins.
    #   SetOption takes 0-10; the menu shows it times 10.
    edit(find_one(assets, "OptionsData.cs", "class OptionsData"),
         "public void PrefenceLoad() {",
         "public void PrefenceLoad()\n\t{\n"
         "#if UNITY_WEBGL && !UNITY_EDITOR // {MARK}\n"
         "\t\tif (!PlayerPrefs.HasKey(OptionType.MusicVolume.ToString()))\n"
         "\t\t{\n"
         "\t\t\tm_options[(int)OptionType.MusicVolume].SetOption(2);\n"
         "\t\t}\n"
         "#endif",
         "default-music-volume-20")
    # - No darkened screen edges: the vignette part of
    #   VignetteAndChromaticAberration is set to nothing (intensity and edge
    #   blur 0); its chromatic aberration still runs.
    edit(find_one(assets, "VignetteAndChromaticAberration.cs", "class VignetteAndChromaticAberration"),
         'm_VignetteMaterial.SetFloat("_Intensity", intensity); m_VignetteMaterial.SetFloat("_Blur", blur);',
         "#if UNITY_WEBGL && !UNITY_EDITOR // {MARK}\n"
         '\t\t\tm_VignetteMaterial.SetFloat("_Intensity", 0f);\n'
         '\t\t\tm_VignetteMaterial.SetFloat("_Blur", 0f);\n'
         "#else\n"
         '\t\t\tm_VignetteMaterial.SetFloat("_Intensity", intensity);\n'
         '\t\t\tm_VignetteMaterial.SetFloat("_Blur", blur);\n'
         "#endif",
         "no-vignette")

    # - OPTIONS in the in-level pause menu, before QUIT. It
    #   opens the book's Graphics/Audio pages over the paused level
    #   (port/scripts/WebGLPortPauseOptions.cs); the level keeps running state.
    # D-pad also moves menu selection on a whole pad (Steam's
    # menus read the sticks only). Whole pads only: WebGLPortMenuDpad.With
    # returns the stick button unchanged for split halves.
    lookup = find_one(assets, "PlayerInputLookup.cs", "class PlayerInputLookup")
    for name, value, kind, limit in (("UIUp", "StickY", "LessThan", "-0.75f"), ("UIDown", "StickY", "Greater", "0.75f"),
                                     ("UILeft", "StickX", "LessThan", "-0.75f"), ("UIRight", "StickX", "Greater", "0.75f")):
        stick = f"new ValueThresholdButton(GetSidedLogicalCombo(AmbiPadValue.{value}, _playerGameInput), ValueThresholdButton.ThresholdType.{kind}, {limit})"
        edit(lookup, f"case LogicalButtonID.{name}:\n\t\t\treturn {stick};",
             f"case LogicalButtonID.{name}:\n\t\t\treturn WebGLPortMenuDpad.With({stick}, _playerGameInput, LogicalButtonID.{name}); // {{MARK}}",
             f"pad-dpad-menu-nav-{name[2:].lower()}")
    pause = find_one(assets, "PauseMenuManager.cs", "class PauseMenuManager")
    # The AssetRipper export writes the enum with explicit values; other
    # decompilers write it without.
    if anchor_regex("Quit = 2, Controls = 3 }").search(pause.read_text(encoding="utf-8")):
        edit(pause, "Quit = 2, Controls = 3 }",
             "Quit = 2,\n\t\tControls = 3,\n\t\tOptions = 4 // {MARK}\n\t}",
             "pause-options-entry")
    else:
        edit(pause, "Quit, Controls }",
             "Quit,\n\t\tControls,\n\t\tOptions // {MARK}\n\t}",
             "pause-options-entry")
    edit(pause, "private bool m_awaitDestroy;",
         "private bool m_awaitDestroy;\n\n\tprivate WebGLPortPauseOptions m_webglOptions; // {MARK}",
         "pause-options-field")
    edit(pause, "private void Pause() { m_sessionData = new SessionData(",
         "private void Pause()\n\t{\n"
         "\t\tm_entries = WebGLPortPauseOptions.WithOptions(m_entries, MenuEntries.Quit, MenuEntries.Options); // {MARK}\n"
         "\t\tm_sessionData = new SessionData(",
         "pause-options-list")
    edit(pause, 'new FrontendListEntry.NameData("PauseMenu." + x,',
         'new FrontendListEntry.NameData((x == MenuEntries.Options) ? "StartScreen.Options" /* {MARK} */ : ("PauseMenu." + x),',
         "pause-options-label")
    edit(pause, "case MenuEntries.Controls: OnControlsSelected(); break;",
         "case MenuEntries.Controls:\n\t\t\t\t\tOnControlsSelected();\n\t\t\t\t\tbreak;\n"
         "\t\t\t\tcase MenuEntries.Options: // {MARK}\n"
         "\t\t\t\t\tm_webglOptions = WebGLPortPauseOptions.Open(m_sessionData.m_guiInstance);\n"
         "\t\t\t\t\tbreak;",
         "pause-options-select")
    # While the options pages are up they own the buttons; the press that
    # closes them must not also close the pause menu.
    edit(pause, "if (m_optionsInstance != null && (m_sessionData.m_uiSelectButton.JustPressed()",
         "if (m_webglOptions != null) // {MARK}\n\t\t{\n"
         "\t\t\tif (!m_webglOptions.IsOpen)\n\t\t\t{\n"
         "\t\t\t\tm_webglOptions = null;\n\t\t\t\tConsumeAllButtons();\n\t\t\t}\n"
         "\t\t\treturn;\n\t\t}\n"
         "\t\tif (m_optionsInstance != null && (m_sessionData.m_uiSelectButton.JustPressed()",
         "pause-options-input")
    edit(pause, "private void Close() { if ((bool)m_optionsInstance)",
         "private void Close()\n\t{\n"
         "\t\tif (m_webglOptions != null) // {MARK}\n\t\t{\n"
         "\t\t\tm_webglOptions.Dispose();\n\t\t\tm_webglOptions = null;\n\t\t}\n"
         "\t\tif ((bool)m_optionsInstance)",
         "pause-options-close")
    # Build 18 follow-ups (applied on top of the edits above, so a project
    # patched by build 18 picks them up too):
    # - the category list is a fresh pause menu from its prefab, not a copy of
    #   the live one (the copy kept its rows and selection);
    # - Dispose destroys the options object, and Unity's == then reports it as
    #   null, so the pause menu skipped its guard and the still-held Cancel/Esc
    #   closed the pause menu as well. Compare as a plain reference instead.
    edit(pause, "m_webglOptions = WebGLPortPauseOptions.Open(m_sessionData.m_guiInstance);",
         "m_webglOptions = WebGLPortPauseOptions.Open(m_sessionData.m_guiInstance, m_startGUIPrefab, m_uiCanvasName); // {MARK}",
         "pause-options-fresh-list")
    edit(pause, f"if (m_webglOptions != null) // {MARK} pause-options-input",
         f"if ((object)m_webglOptions != null) // {MARK} pause-options-input, {{MARK}}",
         "pause-options-input-ref")
    edit(pause, f"if (m_webglOptions != null) // {MARK} pause-options-close",
         f"if ((object)m_webglOptions != null) // {MARK} pause-options-close, {{MARK}}",
         "pause-options-close-ref")
    # The pause list shows 4 lines and OPTIONS makes 5 entries, so QUIT sat
    # off the paper until you scrolled. Grow the list by whole
    # lines of the same height so every entry shows at the game's spacing.
    edit(pause, "m_scrollingList.SetNames(GetMenuNames(_menuEntries));",
         "m_scrollingList.SetNames(GetMenuNames(_menuEntries));\n\t\t\tWebGLPortPauseOptions.FitRows(m_scrollingList, _menuEntries.Length); // {MARK}",
         "pause-options-fit")
    # The book's options page prefab is only referenced from the start screen;
    # moving it (with its .meta, so its GUID and those references hold) into a
    # Resources folder lets the pause menu load it in any level.
    options_gui = [p for p in assets.rglob("OptionsGUI.prefab") if "Resources" not in p.parts]
    options_res = assets / "Resources" / "WebGLPort" / "OptionsGUI.prefab"
    if options_res.exists():
        print("  skipped OptionsGUI.prefab: pause-options-prefab (already in Resources)")
    elif len(options_gui) == 1:
        options_res.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(options_gui[0]), str(options_res))
        shutil.move(str(options_gui[0]) + ".meta", str(options_res) + ".meta")
        print(f"  moved   {options_gui[0]} -> {options_res} (pause-options-prefab)")
    elif not options_gui:
        print("  skipped pause-options-prefab: no OptionsGUI.prefab")
    else:
        raise PatchError(f"expected one OptionsGUI.prefab, found {[str(p) for p in options_gui]}")

    # Unity 2022.3 removed the old WebPlayer and Mac Dashboard targets; their
    # RuntimePlatform members are obsolete-as-error (CS0619) and
    # Application.isWebPlayer is gone (CS0117). None of them can ever be the
    # running platform, so each test becomes `false` and each switch case goes.
    removed = r"RuntimePlatform\.(?:OSXWebPlayer|WindowsWebPlayer|OSXDashboardPlayer)"
    rules = [
        (re.compile(r"[ \t]*case\s+" + removed + r"\s*:[ \t]*\r?\n"), ""),
        (re.compile(r"Application\.platform\s*==\s*" + removed), "false"),
        (re.compile(r"\bApplication\.isWebPlayer\b"), "false"),
    ]
    changed = 0
    for path in assets.rglob("*.cs"):
        text = path.read_text(encoding="utf-8", errors="surrogateescape")
        new = text
        for rx, repl in rules:
            new = rx.sub(repl, new)
        if new != text:
            path.write_text(new, encoding="utf-8", errors="surrogateescape")
            changed += 1
            print(f"  patched {path.name}: removed-webplayer-platforms")
    leftovers = [p.name for p in assets.rglob("*.cs")
                 if re.search(removed + r"|Application\.isWebPlayer",
                              p.read_text(encoding="utf-8", errors="surrogateescape"))]
    if leftovers:
        raise PatchError(f"removed WebPlayer platforms still referenced in: {leftovers}")
    if not changed:
        print("  skipped removed-webplayer-platforms (nothing left to change)")

    print("6. Data settings")
    # Fira Sans faces: AssetRipper fills each font's fallbackFontReferences
    # with other Fira faces in arbitrary order (the 5.5 data has no such
    # lists). Labels that ask for Bold on a thin face (menu entries on
    # UltraLightItalic, option rows on Regular) then pick up ExtraBold/Heavy
    # through those fallbacks in 2022, where 5.5 synthesised bold from the
    # face itself. Empty lists restore the synthetic bold.
    fallback_rx = re.compile(r"(fallbackFontReferences:)[ \t]*\r?\n(?:[ \t]*- \{[^\n]*\}\r?\n)+")
    cleared = 0
    for meta in list(assets.rglob("*.otf.meta")) + list(assets.rglob("*.ttf.meta")):
        text = meta.read_text(encoding="utf-8")
        if "Fira Sans" not in text:
            continue
        new = fallback_rx.sub(r"\1 []\n", text)
        if new != text:
            meta.write_text(new, encoding="utf-8")
            cleared += 1
            print(f"  patched {meta.name}: fira-no-fallback-fonts")
    if not cleared:
        print("  skipped fira-no-fallback-fonts (nothing left to change)")

    # First-run quality level = 2 ("Standard"), the level a Steam install
    # runs (registry UnityGraphicsQuality 2). The exported WebGL and
    # Standalone defaults aren't usable: Standalone's index is 5, out of range
    # for the 4 levels, and copying it made WebGL clamp to 0 (SuperQuick: no
    # shadows, 1 pixel light, deferred start screen) in build7. A quality the
    # player saved in Options still wins.
    quality = project / "ProjectSettings" / "QualitySettings.asset"
    if not quality.exists():
        print("  skipped webgl-quality-standard (no ProjectSettings/QualitySettings.asset)")
    else:
        text = quality.read_text(encoding="utf-8")
        webgl = re.search(r"^([ \t]+)WebGL: (\d+)[ \t]*$", text, re.M)
        standalone = re.search(r"^([ \t]+)Standalone: \d+[ \t]*$", text, re.M)
        if webgl and webgl.group(2) == "2":
            print("  skipped webgl-quality-standard (already 2)")
        elif webgl:
            quality.write_text(text[:webgl.start()] + f"{webgl.group(1)}WebGL: 2" + text[webgl.end():], encoding="utf-8")
            print(f"  patched QualitySettings.asset: webgl-quality-standard ({webgl.group(2)} -> 2)")
        elif standalone:
            quality.write_text(text[:standalone.end()] + f"\n{standalone.group(1)}WebGL: 2" + text[standalone.end():], encoding="utf-8")
            print("  patched QualitySettings.asset: webgl-quality-standard (added, 2)")
        else:
            raise PatchError(f"{quality}: no m_PerPlatformDefaultQuality entries found")

    untrim_sprites(assets)
    name_parameter_curves(assets)
    split_pad_dash_on_sticks(assets)

    print("Done.")


def rect_rx(key: str) -> re.Pattern:
    # A serialized Rect block ("m_Rect:" / "textureRect:") with x/y/width/height lines.
    return re.compile(r"(\n([ \t]*)" + key + r":[ \t]*\r?\n(?:\2[ \t]+serializedVersion: \d+[ \t]*\r?\n)?"
                      r"\2[ \t]+x: )([^\r\n]+)(\r?\n\2[ \t]+y: )([^\r\n]+)"
                      r"(\r?\n\2[ \t]+width: )([^\r\n]+)(\r?\n\2[ \t]+height: )([^\r\n]+)")


def image_size(path: Path):
    head = path.read_bytes()[:24]
    if head[:8] == b"\x89PNG\r\n\x1a\n":
        return int.from_bytes(head[16:20], "big"), int.from_bytes(head[20:24], "big")
    return None



def split_pad_dash_on_sticks(assets: Path) -> None:
    # Keeps the split-pad mapping at the game's own values (chosen after
    # trying dash-on-sticks only; this puts back what build 17 changed).
    # The edit lives in port/input/split_layout.py; this only finds the
    # asset. No asset (a partial project) is a skip, more than one is an error.
    sys.path.insert(0, str(PORT / "input"))
    from split_layout import patch_sided_asset, SplitLayoutError
    hits = list(assets.rglob("SidedAmbiControlsMappingData.asset"))
    if not hits:
        print("  skipped split-dash-sticks: no SidedAmbiControlsMappingData.asset")
        return
    if len(hits) > 1:
        raise PatchError(f"expected one SidedAmbiControlsMappingData.asset, found {[str(h) for h in hits]}")
    try:
        patch_sided_asset(hits[0])
    except SplitLayoutError as e:
        raise PatchError(f"split-dash-sticks: {e}")

def untrim_sprites(assets: Path) -> None:
    # Sprites made from a whole texture (single-sprite textures with the pivot
    # at the texture centre, e.g. the keyboard keycaps and pad button icons)
    # come out of AssetRipper with their rect cut down to the opaque area.
    # 5.5 drew the whole texture, transparent margin included, into the Image;
    # with the cut rect 2022 stretches just the key over the Image, so every
    # icon grows and the wide Space key turns tall. Restore the full
    # texture rect; the pivot stays where it was in texture pixels. This is
    # the same for every whole-texture sprite (5.5's Single sprite mode always
    # covers the whole texture), so it isn't limited to icons. 9-sliced sprites
    # were cut the same way and AssetRipper took the cut margin off their
    # borders too (Cooking_Foreground: 4 x 93.92 on a 193.85 rect, i.e. 125 on
    # the full 256 texture, the same as Cooking_Background). Their borders get
    # the margin back, so the transparent margin is drawn again: that margin is
    # what insets the green chop-bar fill and leaves Steam's white rim.
    texture_by_guid = {}
    for meta in assets.rglob("*.png.meta"):
        m = re.search(r"^guid: ([0-9a-f]{32})", meta.read_text(encoding="utf-8", errors="replace"), re.M)
        if m:
            texture_by_guid[m.group(1)] = meta.with_name(meta.name[:-5])
    num = r"(-?[0-9.eE+-]+)"
    fixed = 0
    for asset in assets.rglob("*.asset"):
        with asset.open("rb") as f:
            if b"\nSprite:" not in f.read(512):
                continue
        text = asset.read_text(encoding="utf-8")
        tex = re.search(r"\n[ \t]+texture: \{fileID: \d+, guid: ([0-9a-f]{32})", text)
        uvt = re.search(r"uvTransform: \{x: " + num + ", y: " + num + ", z: " + num + ", w: " + num + r"\}", text)
        rect = rect_rx("m_Rect").search(text)
        if not (tex and uvt and rect and tex.group(1) in texture_by_guid):
            continue
        png = texture_by_guid[tex.group(1)]
        size = image_size(png)
        if not size or "spriteMode: 1" not in png.with_name(png.name + ".meta").read_text(encoding="utf-8"):
            continue
        w, h = size
        if abs(float(uvt.group(2)) - w / 2) > 0.01 or abs(float(uvt.group(4)) - h / 2) > 0.01:
            continue
        if float(rect.group(7)) >= w - 0.01 and float(rect.group(9)) >= h - 0.01:
            continue  # already the whole texture
        full = lambda m: f"{m.group(1)}0{m.group(4)}0{m.group(6)}{w}{m.group(8)}{h}"
        new = text
        border = re.search(r"m_Border: \{x: ([^,]+), y: ([^,]+), z: ([^,]+), w: ([^}]+)\}", text)
        if border and any(float(v) != 0 for v in border.groups()):
            # Sprite border order: left, bottom, right, top; rect y counts up from the bottom.
            rx, ry = float(rect.group(3)), float(rect.group(5))
            rw, rh = float(rect.group(7)), float(rect.group(9))
            left, bottom, right, top = (float(v) for v in border.groups())
            grown = (left + rx, bottom + ry, right + (w - rx - rw), top + (h - ry - rh))
            new = new[:border.start()] + "m_Border: {x: %g, y: %g, z: %g, w: %g}" % grown + new[border.end():]
            print(f"  border  {asset.name}: ({left:g}, {bottom:g}, {right:g}, {top:g}) -> ({grown[0]:g}, {grown[1]:g}, {grown[2]:g}, {grown[3]:g})")
        new = rect_rx("m_Rect").sub(full, new, count=1)
        new = rect_rx("textureRect").sub(full, new, count=1)
        new = re.sub(r"m_Pivot: \{x: [^,]+, y: [^}]+\}", "m_Pivot: {x: 0.5, y: 0.5}", new, count=1)
        new = re.sub(r"m_Offset: \{x: [^,]+, y: [^}]+\}", "m_Offset: {x: 0, y: 0}", new, count=1)
        new = re.sub(r"textureRectOffset: \{x: [^,]+, y: [^}]+\}", "textureRectOffset: {x: 0, y: 0}", new, count=1)
        asset.write_text(new, encoding="utf-8")
        fixed += 1
        print(f"  patched {asset.name}: sprite-full-texture-rect ({w}x{h})")
    if not fixed:
        print("  skipped sprite-full-texture-rect (nothing left to change)")


def name_parameter_curves(assets: Path) -> None:
    # Animation clips can drive Animator float parameters (curve on path "",
    # class Animator, attribute = the parameter name). AssetRipper couldn't
    # name these and wrote "typetree_0x<CRC32 of the name>_<junk>", which binds
    # to nothing in 2022. The page-turn clips use them for FrontCanvasVis and
    # BackCanvasVis. The hash is the CRC32 of the parameter name, so
    # match it against the parameters of every AnimatorController and put the
    # name (and the Animator class, 95) back.
    names = {}
    for controller in assets.rglob("*.controller"):
        for name in re.findall(r"^[ \t]+- m_Name: (\S+)[ \t]*$",
                               controller.read_text(encoding="utf-8", errors="replace"), re.M):
            names[f"{zlib.crc32(name.encode()) & 0xFFFFFFFF:08X}"] = name
    curve = re.compile(r"(attribute: )typetree_0x([0-9A-Fa-f]{8})_\w*(\s*\n[ \t]*path:[^\n]*\n[ \t]*classID: )(\d+)")
    fixed = 0
    unknown = set()
    for clip in assets.rglob("*.anim"):
        text = clip.read_text(encoding="utf-8")
        if "typetree_0x" not in text:
            continue

        def rename(m):
            name = names.get(m.group(2).upper())
            if name is None:
                unknown.add(m.group(2).upper())
                return m.group(0)
            return f"{m.group(1)}{name}{m.group(3)}95"
        new = curve.sub(rename, text)
        if new != text:
            clip.write_text(new, encoding="utf-8")
            fixed += 1
            print(f"  patched {clip.name}: animator-parameter-curves")
    if unknown:
        print(f"  note: {len(unknown)} typetree curve hash(es) match no controller parameter: {', '.join(sorted(unknown))}")
    if not fixed:
        print("  skipped animator-parameter-curves (nothing left to change)")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        print(__doc__)
        sys.exit(2)
    try:
        main(Path(sys.argv[1]).resolve())
    except PatchError as e:
        print(f"\nFAILED: {e}", file=sys.stderr)
        sys.exit(1)
