// Replacement for the game's "Standard (Specular setup)" (AssetRipper Free exports every shader as a dummy).
// The game's copy is Unity's Standard Specular plus one property, _Brightness, that multiplies
// the final colour of the forward passes (diffuse, specular and GI alike); confirmed from the
// Steam build's compiled ForwardBase pass (reimplemented for this port).
// This file is Unity 2022.3's StandardSpecular layout using the editor's built-in Standard
// includes; the forward fragment functions are wrapped to apply _Brightness. ForwardAdd does
// the same (Not verified: that pass wasn't extracted). Deferred can't scale lighting, so there (and in
// META) _Brightness scales albedo instead; the game renders forward, so that path is unused.
// The original's SM2.0 SubShader is left out (WebGL 2 runs the 3.0 one).
// The original's CustomEditor "CustomizedStandardShaderGUI" isn't in the project, so there is
// no CustomEditor; materials keep the keywords they were saved with.
// _GlossMapScale, _SmoothnessTextureChannel, _SpecularHighlights and _GlossyReflections are not
// in the dummy; they are declared with Unity's defaults so the 2022.3 includes read 1.0, not 0.
Shader "Standard (Specular setup)"
{
	Properties
	{
		_Color("Color", Color) = (1,1,1,1)
		_MainTex("Albedo", 2D) = "white" {}
		_Brightness("Brightness", Range(0,5)) = 1

		_Cutoff("Alpha Cutoff", Range(0.0, 1.0)) = 0.5

		_Glossiness("Smoothness", Range(0.0, 1.0)) = 0.5
		_GlossMapScale("Smoothness Factor", Range(0.0, 1.0)) = 1.0
		[Enum(Specular Alpha,0,Albedo Alpha,1)] _SmoothnessTextureChannel ("Smoothness texture channel", Float) = 0

		_SpecColor("Specular", Color) = (0.2,0.2,0.2,1)
		_SpecGlossMap("Specular", 2D) = "white" {}
		[ToggleOff] _SpecularHighlights("Specular Highlights", Float) = 1.0
		[ToggleOff] _GlossyReflections("Glossy Reflections", Float) = 1.0

		_BumpScale("Scale", Float) = 1.0
		[Normal] _BumpMap("Normal Map", 2D) = "bump" {}

		_Parallax ("Height Scale", Range (0.005, 0.08)) = 0.02
		_ParallaxMap ("Height Map", 2D) = "black" {}

		_OcclusionStrength("Strength", Range(0.0, 1.0)) = 1.0
		_OcclusionMap("Occlusion", 2D) = "white" {}

		_EmissionColor("Color", Color) = (0,0,0,1)
		_EmissionMap("Emission", 2D) = "white" {}

		_DetailMask("Detail Mask", 2D) = "white" {}

		_DetailAlbedoMap("Detail Albedo x2", 2D) = "grey" {}
		_DetailNormalMapScale("Scale", Float) = 1.0
		[Normal] _DetailNormalMap("Normal Map", 2D) = "bump" {}

		[Enum(UV0,0,UV1,1)] _UVSec ("UV Set for secondary textures", Float) = 0

		// Blending state
		[HideInInspector] _Mode ("__mode", Float) = 0.0
		[HideInInspector] _SrcBlend ("__src", Float) = 1.0
		[HideInInspector] _DstBlend ("__dst", Float) = 0.0
		[HideInInspector] _ZWrite ("__zw", Float) = 1.0
	}

	CGINCLUDE
		#define UNITY_SETUP_BRDF_INPUT SpecularSetup
	ENDCG

	SubShader
	{
		Tags { "RenderType"="Opaque" "PerformanceChecks"="False" }
		LOD 300

		// Base forward pass (directional light, emission, lightmaps, ...)
		Pass
		{
			Name "FORWARD"
			Tags { "LightMode" = "ForwardBase" }

			Blend [_SrcBlend] [_DstBlend]
			ZWrite [_ZWrite]

			CGPROGRAM
			#pragma target 3.0

			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
			#pragma shader_feature _EMISSION
			#pragma shader_feature_local _SPECGLOSSMAP
			#pragma shader_feature_local _DETAIL_MULX2
			#pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
			#pragma shader_feature_local _SPECULARHIGHLIGHTS_OFF
			#pragma shader_feature_local _GLOSSYREFLECTIONS_OFF
			#pragma shader_feature_local _PARALLAXMAP

			#pragma multi_compile_fwdbase
			#pragma multi_compile_fog
			#pragma multi_compile_instancing

			#pragma vertex vertBase
			#pragma fragment fragBaseBrightness

			#include "UnityStandardCoreForward.cginc"
			half _Brightness;
			#if UNITY_STANDARD_SIMPLE
			half4 fragBaseBrightness (VertexOutputBaseSimple i) : SV_Target
			#else
			half4 fragBaseBrightness (VertexOutputForwardBase i) : SV_Target
			#endif
			{
				half4 c = fragBase(i);
				c.rgb *= _Brightness;
				return c;
			}
			ENDCG
		}

		// Additive forward pass (one light per pass)
		Pass
		{
			Name "FORWARD_DELTA"
			Tags { "LightMode" = "ForwardAdd" }
			Blend [_SrcBlend] One
			Fog { Color (0,0,0,0) } // in additive pass fog should be black
			ZWrite Off
			ZTest LEqual

			CGPROGRAM
			#pragma target 3.0

			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
			#pragma shader_feature_local _SPECGLOSSMAP
			#pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
			#pragma shader_feature_local _SPECULARHIGHLIGHTS_OFF
			#pragma shader_feature_local _DETAIL_MULX2
			#pragma shader_feature_local _PARALLAXMAP

			#pragma multi_compile_fwdadd_fullshadows
			#pragma multi_compile_fog

			#pragma vertex vertAdd
			#pragma fragment fragAddBrightness

			#include "UnityStandardCoreForward.cginc"
			half _Brightness;
			// [webgl-port] Diagnostic only, off by default (0). WebGLPortEffectToggle sets it globally from a URL flag;
			// in the add pass it replaces the colour with one lighting term as grey (0-255 = 0-1):
			// 1 = spot cookie, 2 = distance falloff LUT, 3 = N.L, 4-6 below. Used to split the 4-2 flashlight loss.
			float _WebGLPortLightDebug;
			half webglPortLightDebug (float3 worldPos, float3 N)
			{
				float m = _WebGLPortLightDebug;
				#if defined(SPOT)
				float4 lc = mul(unity_WorldToLight, float4(worldPos, 1.0));
				if (m < 1.5) return UnitySpotCookie(lc);
				if (m < 2.5) return UnitySpotAttenuate(lc.xyz);
				#elif defined(POINT)
				float3 lc = mul(unity_WorldToLight, float4(worldPos, 1.0)).xyz;
				if (m < 1.5) return 1.0;
				if (m < 2.5) return tex2D(_LightTexture0, dot(lc, lc).rr).UNITY_ATTEN_CHANNEL;
				#endif
				return saturate(dot(normalize(N), normalize(UnityWorldSpaceLightDir(worldPos))));
			}
			#if UNITY_STANDARD_SIMPLE
			half4 fragAddBrightness (VertexOutputForwardAddSimple i) : SV_Target
			#else
			half4 fragAddBrightness (VertexOutputForwardAdd i) : SV_Target
			#endif
			{
				#if !UNITY_STANDARD_SIMPLE
				if (_WebGLPortLightDebug > 3.5)
				{
					// 4 = diffuse colour after energy conservation, 5 = specular colour, 6 = smoothness.
					FRAGMENT_SETUP_FWDADD(s)
					if (_WebGLPortLightDebug < 4.5) return half4(s.diffColor, 1.0);
					if (_WebGLPortLightDebug < 5.5) return half4(s.specColor, 1.0);
					return half4(s.smoothness.xxx, 1.0);
				}
				if (_WebGLPortLightDebug > 0.5)
					return half4(webglPortLightDebug(IN_WORLDPOS_FWDADD(i), i.tangentToWorldAndLightDir[2].xyz).xxx, 1.0);
				#endif
				half4 c = fragAdd(i);
				c.rgb *= _Brightness;
				return c;
			}
			ENDCG
		}

		// Shadow rendering pass (no albedo, so no _Brightness)
		Pass
		{
			Name "ShadowCaster"
			Tags { "LightMode" = "ShadowCaster" }

			ZWrite On ZTest LEqual

			CGPROGRAM
			#pragma target 3.0

			#pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
			#pragma shader_feature_local _SPECGLOSSMAP
			#pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
			#pragma shader_feature_local _PARALLAXMAP
			#pragma multi_compile_shadowcaster
			#pragma multi_compile_instancing

			#pragma vertex vertShadowCaster
			#pragma fragment fragShadowCaster

			#include "UnityStandardShadow.cginc"
			ENDCG
		}

		// Deferred pass
		Pass
		{
			Name "DEFERRED"
			Tags { "LightMode" = "Deferred" }

			CGPROGRAM
			#pragma target 3.0
			#pragma exclude_renderers nomrt

			#pragma shader_feature_local _NORMALMAP
			#pragma shader_feature_local _ _ALPHATEST_ON _ALPHABLEND_ON _ALPHAPREMULTIPLY_ON
			#pragma shader_feature _EMISSION
			#pragma shader_feature_local _SPECGLOSSMAP
			#pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
			#pragma shader_feature_local _SPECULARHIGHLIGHTS_OFF
			#pragma shader_feature_local _DETAIL_MULX2
			#pragma shader_feature_local _PARALLAXMAP

			#pragma multi_compile_prepassfinal
			#pragma multi_compile_instancing

			#pragma vertex vertDeferred
			#pragma fragment fragDeferred

			#include "UnityStandardInput.cginc"
			half _Brightness;
			#define Albedo(texcoords) (Albedo(texcoords) * _Brightness)
			#include "UnityStandardCore.cginc"
			ENDCG
		}

		// Extracts information for lightmapping, GI (emission, albedo, ...)
		Pass
		{
			Name "META"
			Tags { "LightMode"="Meta" }

			Cull Off

			CGPROGRAM
			#pragma vertex vert_meta
			#pragma fragment frag_meta

			#pragma shader_feature _EMISSION
			#pragma shader_feature_local _SPECGLOSSMAP
			#pragma shader_feature_local _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
			#pragma shader_feature_local _DETAIL_MULX2
			#pragma shader_feature EDITOR_VISUALIZATION

			#include "UnityStandardInput.cginc"
			half _Brightness;
			#define Albedo(texcoords) (Albedo(texcoords) * _Brightness)
			#include "UnityStandardMeta.cginc"
			ENDCG
		}
	}

	FallBack "VertexLit"
}
