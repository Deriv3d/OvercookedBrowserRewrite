# port/input: PS4 controllers in the browser

## What's here

| File | What it does |
|---|---|
| `WebGLStandardGamepadProfiles.cs` | Two InControl device profiles for pads Chrome reports with the W3C standard layout, using the numbers Unity's WebGL player re-maps them to (Xbox-on-Windows style, measured in build 25): `WebGLDualShock4Profile` (DS4 v1 05c4, v2 09cc, Sony wireless adapter 0ba0; USB or Bluetooth) and `WebGLStandardGamepadProfile` (any other standard pad, last resort). |
| `split_layout.py` | Split-pad dash: puts Dash and chop in `SidedAmbiControlsMappingData.asset` back to the game's own values (left half dashes on L3 or D-pad right, right half on R3 or Circle), undoing build 17's L3/R3-only edit. No-op on a fresh asset. Idempotent; handles block-list, flow-list and hex int arrays. |
| `pad-log.js` | Logs each pad the browser exposes (name, layout, button and axis counts) as `[pad-log]` console lines on connect/disconnect. For checking what Safari or another browser calls a pad. |
| `fake-gamepad.js` | Simulated DS4 v2 for headless runs: with `?pad=<steps>` on the test page it replaces `navigator.getGamepads()` with one standard-layout pad and plays timed button/stick steps (see the file's header). Lets you check profile match, stick direction and pause without a real pad. |
| `gamepad-test.html` | Standalone pad check. Double-click it in Chrome, press a button on each pad: it shows the pad's name, DS4 v1 or v2, whether Chrome applied the standard layout, and live buttons and sticks. Needs no build. |

## Why a profile is needed

On WebGL, InControl's `UnityInputDeviceManager` sees pads through `Input.GetJoystickNames()`, which in Chrome returns names like `Wireless Controller (STANDARD GAMEPAD Vendor: 054c Product: 09cc)`. No stock profile matches that, so the pad gets InControl's unknown-device fallback (raw Button0..19, no game actions). The stock `PlayStation4WinProfile` would be wrong anyway: Windows DirectInput puts Square on button 0, the browser puts Cross there (Unity's WebGL manual: old Input follows the W3 layout).

## How it's wired in

`tools/apply_patches.py` copies the profiles into the project and lists them first in InControl's `UnityInputDeviceProfileList.Profiles` (patches `incontrol-profile-list-size` and `incontrol-webgl-profiles`).

## Checks so far

- Stick mappings use InControl 1.5's four-direction helpers (the game's version, 1.5.12).
- Earlier draft compiled with mcs against the public InControl 1.4.4 source and Unity's UnityEngine.dll, with and without `UNITY_WEBGL`.
- Unity cuts joystick names at 63 characters (build 10 headless run: "...Vendor: 054c Product: 09c"), so the DS4 profile matches on "STANDARD GAMEPAD Vendor: 054c", not the product id. Tested on 63-character cuts of the Chrome, Firefox and adapter names: DS4 v1/v2/adapter (and DualSense) match the DS4 profile; Xbox in standard layout falls to the generic one; a pad Chrome couldn't remap ("Unknown Gamepad") matches neither.
- `gamepad-test.html` rendered in headless Chromium with fake pads, no script errors.

## Not known yet (to confirm on a real PC)

- Stick Y direction in Unity's WebGL joystick axes (assumed browser-raw, -1 = up, which InControl 1.5's Up/Down helpers expect). If up/down are swapped in game, swap the Up and Down mappings on axes 1 and 3.
- Whether Unity WebGL numbers joysticks in the same order as `navigator.getGamepads()`.
- Which InputControlType the game binds to pause (mapped Options → Start for now).
