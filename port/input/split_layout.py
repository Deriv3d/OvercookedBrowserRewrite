#!/usr/bin/env python3
"""Split-pad dash for the WebGL port.

On a split pad the left player dashes with D-pad right and
the right player with Circle, and L3/R3 dash too. That is exactly the game's
own SidedAmbiControlsMappingData (from the export):
    Two   (chop/throw) [LT, RT, X(Square), DPadLeft]
    Three (dash)       [L3, R3, B(Circle), DPadRight]
An earlier build (17) cut Dash to [L3, R3]; this puts both entries back to the
game's values. On a fresh copy of the asset it changes nothing, so once no
project copy carries the old edit the apply_patches call can be dropped.

Called from tools/apply_patches.py:
    from split_layout import patch_sided_asset
    patch_sided_asset(path_to_SidedAmbiControlsMappingData.asset)
or on its own:
    python port/input/split_layout.py <path-to-asset>

Idempotent: a second run finds both entries already set and changes nothing.
"""

import re
import sys
from pathlib import Path

# ControlPadInput.Button
X, B, LTRIGGER, RTRIGGER, DPAD_LEFT, DPAD_RIGHT, L3, R3 = 2, 3, 7, 8, 9, 10, 15, 16
# AmbiPadButton -> RealButtons (the game's own values)
CHANGES = {
    1: [LTRIGGER, RTRIGGER, X, DPAD_LEFT],      # Two (chop/throw)
    2: [L3, R3, B, DPAD_RIGHT],                 # Three (dash)
}


class SplitLayoutError(Exception):
    pass


def _encode_hex(values):
    # Unity can write int arrays as one hex string of little-endian int32s.
    return "".join(v.to_bytes(4, "little").hex() for v in values)


def _decode_hex(text):
    if len(text) % 8:
        raise SplitLayoutError(f"RealButtons hex has odd length: {text!r}")
    return [int.from_bytes(bytes.fromhex(text[i:i + 8]), "little") for i in range(0, len(text), 8)]


def rewrite(text, button, new):
    """Return (new_text, old_buttons) with the RealButtons of one
    m_buttonMapping entry set to `new`. Raises unless found exactly once."""
    lines = text.split("\n")
    starts = [i for i, l in enumerate(lines)
              if re.match(r"^\s*-\s*GamepadButton:\s*%d\s*$" % button, l)]
    if len(starts) != 1:
        raise SplitLayoutError(f"expected one m_buttonMapping entry with GamepadButton: {button}, found {len(starts)}")
    i = starts[0]
    entry_indent = len(lines[i]) - len(lines[i].lstrip())
    # Find RealButtons inside this entry (before the next "- " at the same indent).
    j = i + 1
    while j < len(lines):
        l = lines[j]
        indent = len(l) - len(l.lstrip())
        if l.strip().startswith("- ") and indent <= entry_indent:
            break
        m = re.match(r"^(\s*)RealButtons:\s*(.*)$", l)
        if m:
            lead, value = m.group(1), m.group(2).strip()
            if value.startswith("["):                       # flow list: [3, 9, 10]
                old = [int(v) for v in re.findall(r"-?\d+", value)]
                lines[j] = f"{lead}RealButtons: [{', '.join(map(str, new))}]"
                return "\n".join(lines), old
            if value and re.fullmatch(r"[0-9a-fA-F]+", value):  # hex blob
                old = _decode_hex(value)
                lines[j] = f"{lead}RealButtons: {_encode_hex(new)}"
                return "\n".join(lines), old
            if value == "":                                  # block list on the next lines
                k = j + 1
                old, item_lines = [], []
                while k < len(lines):
                    im = re.match(r"^(\s*)-\s*(-?\d+)\s*$", lines[k])
                    if not im or len(im.group(1)) < len(lead):
                        break
                    old.append(int(im.group(2)))
                    item_lines.append(im.group(1))
                    k += 1
                if not old:
                    raise SplitLayoutError("RealButtons block list is empty or unreadable")
                item_lead = item_lines[0]
                lines[j + 1:k] = [f"{item_lead}- {v}" for v in new]
                return "\n".join(lines), old
            raise SplitLayoutError(f"unrecognised RealButtons value: {value!r}")
        j += 1
    raise SplitLayoutError(f"GamepadButton {button} entry has no RealButtons field")


def patch_sided_asset(path):
    path = Path(path)
    text = path.read_text(encoding="utf-8")
    done = []
    for button, new in CHANGES.items():
        text, old = rewrite(text, button, new)
        if old != new:
            done.append(f"{button}: {old} -> {new}")
    if not done:
        print(f"  skipped {path.name}: split-dash (already applied)")
        return
    path.write_text(text, encoding="utf-8", newline="\n")
    print(f"  patched {path.name}: split-dash ({'; '.join(done)})")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    try:
        patch_sided_asset(sys.argv[1])
    except SplitLayoutError as e:
        sys.exit(f"split_layout: {e}")
