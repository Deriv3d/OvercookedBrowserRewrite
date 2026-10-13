# Properties of the exported dummy shaders (reference)

Read from the AssetRipper export's Assets/Shader folder.
AssetRipper's dummies all say `RenderType=Opaque` and `LOD 200`, so those are not the originals. The Fallback lines appear to be original. Colour properties show as `Vector` in the dummies; the originals were probably `Color`.

| File | Shader name | Properties | Fallback |
|---|---|---|---|
| Toon_Basic | `Toon/Basic` | `_Color` ("Main Color") = (0.5,0.5,0.5,1); `_MainTex` ("Base (RGB)", 2D) = "white"; `_ToonShade` ("ToonShader Cubemap(RGB)", Cube) | `VertexLit` (matches Unity Standard Assets Toon/Basic) |
| Custom_HalfLambertBumpRim | `Custom/HalfLambertBumpRim` | `_MainTex` 2D "white"; `_Brightness` Range(0,5)=1; `_BumpMap` 2D "bump"; `_WrapAmount` Range(0,1)=0.5; `_RimColor` = (0.26,0.19,0.16,0); `_RimPower` Range(0.5,8)=3 | `Diffuse` |
| Custom_HalfLambertBumpRimAlpha | `Custom/HalfLambertBumpRimAlpha` | `_MainTex` 2D "white"; `_Color` = (1,1,1,1); `_Brightness` Range(0,5)=1; `_BumpMap` 2D "bump"; `_WrapAmount` Range(0,1)=0.5; `_RimColor` = (0.26,0.19,0.16,0); `_RimPower` Range(0.5,8)=0; `_Cutoff` ("Alpha Cutoff") Range(0,1)=0.5 | none (cutoff suggests alpha test) |
| Custom_HalfLambertSpecBumpRim | `Custom/HalfLambertSpecBumpRim` | `_MainTex` 2D "white"; `_Brightness` Range(0,5)=1; `_BumpMap` 2D "bump"; `_WrapAmount` Range(0,1)=0.5; `_SpecLevel` Float=0; `_SpecPower` Float=0; `_RimColor` = (0.26,0.19,0.16,0); `_RimPower` Range(0.5,8)=3 | `Diffuse` |
| Custom_TwoSidedAlpha | `Custom/TwoSidedAlpha` | `_Color` ("Main Color") = (1,1,1,1); `_MainTex` ("Base (RGB)", 2D) "white"; `_CutOff` ("AlphaCutoff", Float)=0.1 (capital O) | `Transparent/VertexLit` |
| Mobile_Color | `Mobile/Color` | `_Color` = (1,1,1,1) | `Mobile/VertexLit` |

None has a CustomEditor.

## The other 11

| File | Shader name | Properties | Fallback |
|---|---|---|---|
| Custom_Cloud | `Custom/Cloud` | `_Color` ("Main Color") = (1,1,1,1); `_MainTex` ("Base (RGB) Trans (A)", 2D) "white"; `_WrapAmount` Range(0,1)=0.5 | none |
| Custom_FlambeFood | `Custom/FlambeFood` | `_Texture1` ("Starting Texture (RGB)", 2D) "white"; `_Texture2` ("Ending Texture (RGB)", 2D) "white"; `Prop` Range(0,1)=0 (no underscore); `_Color` = (1,1,1,1); `_Brightness` Range(0,5)=1; `_BumpMap` 2D "bump"; `_WrapAmount` Range(0,1)=0.5; `_RimColor` = (0.26,0.19,0.16,0); `_RimPower` Range(0.5,8)=0; `_Cutoff` Range(0,1)=0.5; `FlambeProp` Range(0,1)=0 (no underscore); `_FlambeColor` = (1,1,1,1); `_FlambeBrightness` Range(0,5)=1; `_FlambeWrapAmount` Range(0,1)=0.5; `_FlambeRimColor` = (0.26,0.19,0.16,0); `_FlambeRimPower` Range(0.5,8)=0 | none |
| Custom_HardParticleAlphaBlended | `Custom/HardParticleAlphaBlended` | `_TintColor` = (0.5,0.5,0.5,0.5); `_MainTex` ("Particle Texture", 2D) "white" | none |
| Custom_MapLiquid | `Custom/MapLiquid` | `_MainTex` ("Base (RGB)", 2D) "white"; `_Samples` Float=4; `_Color1` ("Low Color") = (1,1,1,1); `_Color2` ("High Color") = (1,1,1,1); `_Amplitude` Float=1; `_SpeedMultiplier` Float=1; `_MinWavelength` Float=1; `_MaxWavelength` Float=10 | `Diffuse` |
| Custom_WaveyLiquid | `Custom/WaveyLiquid` | same 8 properties and defaults as MapLiquid | `Diffuse` |
| Custom_RecipeBookPage | `Custom/RecipeBookPage` | `_Color` ("Main Color") = (1,1,1,1); `_MainTex` ("Base (RGB)", 2D) "white"; `_FrontPageTex` ("FrontPage (RGB)", 2D) "white"; `_BackPageTex` ("BackPage (RGB)", 2D) "white"; `_UIAreaUV` ("UI MinMax Area UV", Vector) = (0.3,0.3,0.7,0.7) | `Diffuse` |
| Custom_TransitionTexture | `Custom/TransitionTexture` | `_Texture1` 2D "white"; `_Texture2` 2D "white"; `Prop` Range(0,1)=0 (no underscore); `_Color` = (1,1,1,1) | `Diffuse` |
| Custom_VertexPush | `Custom/VertexPush` | `_MainTex` 2D "white"; `_Color` ("Main Color") = (1,1,1,1); `_Pivot` ("Pivot Vector", Vector) = (0,0,0,0); `_PushVector` ("Push Vector", Vector) = (0,0,0,0); `_WobbleAmount` Float=0; `_RandomTiming` Float=0 | `Diffuse` |
| UI_CircleScreenWipe | `UI/CircleScreenWipe` | UI-Default layout: `[PerRendererData] _MainTex` ("Sprite Texture") "white"; `_Color` ("Tint") = (1,1,1,1); `_StencilComp`=8; `_Stencil`=0; `_StencilOp`=0; `_StencilWriteMask`=255; `_StencilReadMask`=255; `_ColorMask`=15; plus `_Prop` Range(0,1)=0 | none |
| Projector_Multiply | `Projector/Multiply` | `_Prop` Range(0,1)=1 (not in Unity's stock version); `_ShadowTex` ("Cookie", 2D) "gray"; `_FalloffTex` ("FallOff", 2D) "white" | none |
| Standard (Specular setup) | `Standard (Specular setup)` | Unity's Standard Specular set plus **`_Brightness` Range(0,5)=1**: `_Color`, `_MainTex`, `_Brightness`, `_Cutoff`=0.5, `_Glossiness`=0.5, `_SpecColor`=(0.2,0.2,0.2,1), `_SpecGlossMap`, `_BumpScale`=1, `_BumpMap`, `_Parallax` Range(0.005,0.08)=0.02, `_ParallaxMap` "black", `_OcclusionStrength`=1, `_OcclusionMap`, `_EmissionColor`=(0,0,0,1), `_EmissionMap`, `_DetailMask`, `_DetailAlbedoMap` "grey", `_DetailNormalMapScale`=1, `_DetailNormalMap`, `[Enum(UV0,0,UV1,1)] _UVSec`=0, `[HideInInspector] _Mode/_SrcBlend/_DstBlend/_ZWrite` | `VertexLit`; original used `CustomEditor "CustomizedStandardShaderGUI"` (commented out by AssetRipper) |
