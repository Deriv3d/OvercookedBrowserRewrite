# Replacement shaders

AssetRipper Free exports every shader as a dummy. Each `.shader` here replaces the dummy with the same file name; `tools/apply_patches.py` copies it over the dummy and keeps the dummy's `.meta`, so materials stay linked.

Rules:
- Keep the original shader name (`Shader "..."`) and every property in its Properties block, with the same names and types, even if unused.
- Built-in render pipeline, Unity 2022.3, must compile for WebGL 2.0 (GLSL ES 3.0): no geometry/tessellation shaders, no compute.
- Match the look of the Steam game (screenshots); note anything guessed in a comment.

`_reference/` holds the dummies' Properties blocks for reference only.
