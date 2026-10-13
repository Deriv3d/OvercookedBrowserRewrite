# Third-party notices

This repository's own code is under the MIT licence in [LICENSE](LICENSE). Some files are adapted from other people's work, listed here with their terms.

**Overcooked (Team17 / Ghost Town Games).** No part of the game is included. The tools only read your own installed copy, on your own PC. `tools/apply_patches.py` finds the places it edits by short text matches of one or two lines; it contains no game code beyond those short anchors. The shaders under `port/shaders/` named after the game's shaders (`Custom_*`, `Mobile_Color`, `UI_CircleScreenWipe`, `Projector_Multiply`, `Toon_Basic`) were written for this port to reproduce how the game's shaders look. The exporter only provides empty stand-ins for them.

**Unity built-in shaders** (Unity Technologies, MIT licence). `port/shaders/added/UI-Default-55.shader` is adapted from Unity 5.5's built-in `UI/Default`, and `port/shaders/Standard (Specular setup).shader` uses Unity's built-in shader include files. Unity's built-in shader source is published under the MIT licence; copyright (c) 2016 Unity Technologies.

**Unity Standard Assets image effects** (Unity Technologies). The `port/shaders/Hidden_*` image-effect shaders (bloom, depth of field, SSAO, colour correction, fog, vignetting, lens flares, blur) are rewritten for Unity 2022.3 from the Unity 5.x Standard Assets "Effects" package, which the game uses. That package came with Unity under Unity's terms.

**NVIDIA FXAA 3.11** (Timothy Lottes, NVIDIA Corporation). `port/shaders/Hidden_FXAA III (Console).shader` is a port of FXAA 3.11 (console variant), by way of Unity's Standard Assets. FXAA is distributed by NVIDIA with this notice: "Copyright (c) 2010, 2011 NVIDIA Corporation. All rights reserved. TO THE MAXIMUM EXTENT PERMITTED BY APPLICABLE LAW, THIS SOFTWARE IS PROVIDED *AS IS* AND NVIDIA AND ITS SUPPLIERS DISCLAIM ALL WARRANTIES, EITHER EXPRESS OR IMPLIED, INCLUDING, BUT NOT LIMITED TO, IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE. IN NO EVENT SHALL NVIDIA OR ITS SUPPLIERS BE LIABLE FOR ANY SPECIAL, INCIDENTAL, INDIRECT, OR CONSEQUENTIAL DAMAGES WHATSOEVER."

**Steamworks.NET and XInputDotNetPure** (MIT licence). `port/replacements/` holds this port's own stand-ins with the same class and method names, so the game's code compiles without Steam or XInput. `XInputDotNetPure/Imports.cs` repeats that library's two native function declarations.

**AssetRipper** and **Unity** are separate downloads. Neither is included here.
