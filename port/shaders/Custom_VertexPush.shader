// Replacement for the game's "Custom/VertexPush" (AssetRipper Free exports every shader as a dummy).
// Matches the Steam build's compiled shader (reimplemented for this port):
// a Lambert surface shader with a vertex function, written out as explicit passes.
// Each vertex moves by _PushVector (world direction) times its distance from _Pivot (a world
// point), plus a wobble of that distance times _WobbleAmount:
//   x += w * sin(_Time.x * _RandomXTiming + ofs.z);  y += w * sin(_Time.x * _RandomZTiming + ofs.z)
// (the Z timing really drives Y). The normal is not changed. Plain Lambert (no x2), SH ambient
// per vertex in gamma, Cull Off.
// The compiled program reads _RandomXTiming and _RandomZTiming, which the AssetRipper dummy
// lacks (it has _RandomTiming instead, unused by the program): all three are declared so the
// materials' saved values bind. _Color is declared but unused, as in the original.
// The original's PREPASS and META passes are left out; its DEFERRED pass is kept (the game renders deferred at Dreamy).
// Shadow casting comes from the Fallback (unpushed), as in the original.
// [webgl-port] Add pass uses fwdadd_fullshadows: the game renders this through the deferred path
// (DeferredLighting at Standard, DeferredShading at Dreamy; DeferredLighting at Standard, DeferredShading at Dreamy), where 5.5 lit it
// with every light per pixel, each with its shadows. We run Forward, so the add pass needs shadows too.
Shader "Custom/VertexPush" {
	Properties {
		_MainTex ("Base (RGB)", 2D) = "white" {}
		_Color ("Main Color", Color) = (1,1,1,1)
		_Pivot ("Pivot Vector", Vector) = (0,0,0,0)
		_PushVector ("Push Vector", Vector) = (0,0,0,0)
		_WobbleAmount ("Wobble Amount", Float) = 0
		_RandomTiming ("Random Timing", Float) = 0
		_RandomXTiming ("Random X Timing", Float) = 0
		_RandomZTiming ("Random Z Timing", Float) = 0
	}

	CGINCLUDE
	#include "UnityCG.cginc"
	#include "Lighting.cginc"
	#include "AutoLight.cginc"
	#include "UnityGBuffer.cginc"

	sampler2D _MainTex;
	float4 _MainTex_ST;
	float4 _Pivot;
	float4 _PushVector;
	float _WobbleAmount;
	float _RandomXTiming;
	float _RandomZTiming;

	struct v2f {
		UNITY_POSITION(pos);
		float2 uv : TEXCOORD0;
		float3 worldPos : TEXCOORD1;
		float3 worldN : TEXCOORD2;
		half3 sh : TEXCOORD3; // ambient + vertex lights (ForwardBase only)
		UNITY_LIGHTING_COORDS(4, 5)
		UNITY_FOG_COORDS(6)
	};

	v2f vert (appdata_full v)
	{
		v2f o;
		UNITY_INITIALIZE_OUTPUT(v2f, o);

		float3 pivotObj = mul(unity_WorldToObject, float4(_Pivot.xyz, 1.0)).xyz;
		float3 ofs = v.vertex.xyz - pivotObj;
		float dist = length(ofs);
		float3 pushObj = mul((float3x3)unity_WorldToObject, _PushVector.xyz);
		float3 p = v.vertex.xyz + dist * pushObj;
		float w = dist * _WobbleAmount;
		p.x += w * sin(_Time.x * _RandomXTiming + ofs.z);
		p.y += w * sin(_Time.x * _RandomZTiming + ofs.z);
		v.vertex.xyz = p;

		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
		float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
		float3 worldN = UnityObjectToWorldNormal(v.normal);
		o.worldPos = worldPos;
		o.worldN = worldN;

		#ifdef UNITY_PASS_FORWARDBASE
		// Per-vertex SH ambient, clamped, then to gamma (the project is in Gamma space).
		float4 n4 = float4(worldN, 1.0);
		half3 sh = max(half3(0, 0, 0), SHEvalLinearL0L1(n4) + SHEvalLinearL2(n4));
		#ifdef UNITY_COLORSPACE_GAMMA
		sh = LinearToGammaSpace(sh);
		#endif
		o.sh = sh;
		#ifdef VERTEXLIGHT_ON
		o.sh += Shade4PointLights(unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
			unity_LightColor[0].rgb, unity_LightColor[1].rgb, unity_LightColor[2].rgb, unity_LightColor[3].rgb,
			unity_4LightAtten0, worldPos, worldN);
		#endif
		#endif

		UNITY_TRANSFER_LIGHTING(o, v.texcoord1.xy);
		UNITY_TRANSFER_FOG(o, o.pos);
		return o;
	}

	fixed4 frag (v2f i) : SV_Target
	{
		float3 worldPos = i.worldPos;
		#ifndef USING_DIRECTIONAL_LIGHT
		float3 L = normalize(UnityWorldSpaceLightDir(worldPos));
		#else
		float3 L = _WorldSpaceLightPos0.xyz;
		#endif

		half3 albedo = tex2D(_MainTex, i.uv).rgb;

		UNITY_LIGHT_ATTENUATION(atten, i, worldPos);

		fixed4 c;
		c.rgb = albedo * _LightColor0.rgb * (max(0.0, dot(i.worldN, L)) * atten);
		#ifdef UNITY_PASS_FORWARDBASE
		c.rgb += albedo * i.sh;
		#endif
		c.a = 1.0;
		UNITY_APPLY_FOG(i.fogCoord, c);
		return c;
	}
	// [webgl-port] Deferred pass. At Dreamy the game renders with DeferredShading, and the original
	// (a Lambert surface shader) had a DEFERRED pass there: it writes the G-buffer in its own queue
	// position, so later opaques draw over it. Without this pass Unity draws the shader forward after
	// the deferred lighting, which with this shader's depth state paints it over the scene.
	// Same G-buffer as a 5.x Lambert surface shader: albedo as diffuse, no specular, smoothness 0,
	// the vertex normal, and albedo x SH ambient (gamma) in the emission buffer.
	void fragDeferred (v2f i,
		out half4 outGBuffer0 : SV_Target0,
		out half4 outGBuffer1 : SV_Target1,
		out half4 outGBuffer2 : SV_Target2,
		out half4 outEmission : SV_Target3
	#if defined(SHADOWS_SHADOWMASK) && (UNITY_ALLOWED_MRT_COUNT > 4)
		, out half4 outShadowMask : SV_Target4
	#endif
		)
	{
		half3 albedo = tex2D(_MainTex, i.uv).rgb;
		float3 worldN = normalize(i.worldN);
		float4 n4 = float4(worldN, 1.0);
		half3 sh = max(half3(0, 0, 0), SHEvalLinearL0L1(n4) + SHEvalLinearL2(n4));
		#ifdef UNITY_COLORSPACE_GAMMA
		sh = LinearToGammaSpace(sh);
		#endif

		UnityStandardData data;
		data.diffuseColor = albedo;
		data.occlusion = 1.0;
		data.specularColor = half3(0, 0, 0);
		data.smoothness = 0.0;
		data.normalWorld = worldN;
		UnityStandardDataToGbuffer(data, outGBuffer0, outGBuffer1, outGBuffer2);

		half4 emission = half4(albedo * sh, 1.0);
		#ifndef UNITY_HDR_ON
		emission.rgb = exp2(-emission.rgb);
		#endif
		outEmission = emission;
	#if defined(SHADOWS_SHADOWMASK) && (UNITY_ALLOWED_MRT_COUNT > 4)
		outShadowMask = 1.0;
	#endif
	}
	ENDCG

	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200

		Pass {
			Name "FORWARD"
			Tags { "LightMode"="ForwardBase" "SHADOWSUPPORT"="true" }
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile_fwdbase
			#pragma multi_compile_fog
			ENDCG
		}

		Pass {
			Name "FORWARD"
			Tags { "LightMode"="ForwardAdd" }
			ZWrite Off
			Blend One One
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile_fwdadd_fullshadows
			#pragma multi_compile_fog
			ENDCG
		}

		Pass {
			Name "DEFERRED"
			Tags { "LightMode"="Deferred" }
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragDeferred
			#pragma target 3.0
			#pragma exclude_renderers nomrt
			#pragma multi_compile_prepassfinal
			ENDCG
		}
	}

	Fallback "Diffuse"
}
