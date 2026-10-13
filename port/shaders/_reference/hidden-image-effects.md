# Hidden_* image-effect shaders (reference)

All 27 are dummies of Unity's Standard Assets 5.x image-effect shaders; names and Properties match the stock versions (only FXAA Preset 2/3 have a Fallback, "Hidden/FXAA II"). Read from the patched project.

## Which effect scripts the scenes use
Number of .unity/.prefab files referencing each ImageEffects script (files, not instances or enabled state):

| Script | Files | Shaders it needs |
|---|---|---|
| VignetteAndChromaticAberration | 67 | Hidden/Vignetting, Hidden/ChromaticAberration, Hidden/SeparableBlur |
| ScreenSpaceAmbientOcclusion | 67 | Hidden/SSAO |
| Antialiasing | 67 | whichever mode it selects: Hidden/FXAA II, FXAA III (Console), FXAA Preset 2/3, NFAA, DLAA, SSAA |
| ColorCorrectionCurves | 64 | Hidden/ColorCorrectionCurves, ColorCorrectionCurvesSimple, ColorCorrectionSelective |
| BloomOptimized | 9 | Hidden/FastBloom |
| GlobalFog | 5 | Hidden/GlobalFog |
| Bloom | 3 | Hidden/BlendForBloom, BrightPassFilter2, LensFlareCreate, BlurAndFlares |
| TiltShift | 2 | Hidden/Dof/TiltShiftHdrLensBlur |
| MotionBlur | 2 | Hidden/MotionBlur, MotionBlurClear |
| DepthOfField | 1 | Hidden/Dof/DepthOfFieldHdr, Hidden/Dof/DX11Dof |
| CameraMotionBlur | 1 | Hidden/CameraMotionBlur, CameraMotionBlurDX11 |

The other 32 effect scripts are unused. DX11-only variants (DX11Dof, CameraMotionBlurDX11) can't run on WebGL; the scripts fall back when the shader reports unsupported.

## File names
Hidden_BlendForBloom, Hidden_BlurAndFlares, Hidden_BrightPassFilter2, Hidden_CameraMotionBlur, Hidden_CameraMotionBlurDX11, Hidden_ChromaticAberration, Hidden_ColorCorrectionCurves, Hidden_ColorCorrectionCurvesSimple, Hidden_ColorCorrectionSelective, Hidden_DLAA, Hidden_Dof_DX11Dof, Hidden_Dof_DepthOfFieldHdr, Hidden_Dof_TiltShiftHdrLensBlur, "Hidden_FXAA II", "Hidden_FXAA III (Console)", "Hidden_FXAA Preset 2", "Hidden_FXAA Preset 3", Hidden_FastBloom, Hidden_GlobalFog, Hidden_LensFlareCreate, Hidden_MotionBlur, Hidden_MotionBlurClear, Hidden_NFAA, Hidden_SSAA, Hidden_SSAO, Hidden_SeparableBlur, Hidden_Vignetting (all .shader). Note: SeparableBlur is used by VignetteAndChromaticAberration's blur.
