// Replacement for the game's "Custom/MapLiquid" (AssetRipper Free exports every shader as a dummy).
// Matches the Steam build's compiled shader (reimplemented for this port).
// Same family as Custom/WaveyLiquid, with three differences: the mesh is NOT displaced (the
// wave sum only picks the colour), UVs come from _MainTex's tiling/offset, not world position,
// and depth state is normal (ZTest LEqual, no Queue tag). Cull Off in both passes.
// Colour: t = ((h / sumA + 1) / 2)^2, lerp(_Color1, _Color2, t), times _MainTex; plain Lambert
// (no x2), SH ambient per vertex in gamma.
// The original's PREPASS and META passes are left out; its DEFERRED pass is kept (the game renders deferred at Dreamy).
// Shadow casting comes from the Fallback, as in the original.
// [webgl-port] Add pass uses fwdadd_fullshadows: the game renders this through the deferred path
// (DeferredLighting at Standard, DeferredShading at Dreamy; DeferredLighting at Standard, DeferredShading at Dreamy), where 5.5 lit it
// with every light per pixel, each with its shadows. We run Forward, so the add pass needs shadows too.
Shader "Custom/MapLiquid" {
	Properties {
		_MainTex ("Base (RGB)", 2D) = "white" {}
		_Samples ("Samples", Float) = 4
		_Color1 ("Low Color", Color) = (1,1,1,1)
		_Color2 ("High Color", Color) = (1,1,1,1)
		_Amplitude ("Amplitude", Float) = 1
		_SpeedMultiplier ("Speed Multiplier", Float) = 1
		_MinWavelength ("Min Wavelength", Float) = 1
		_MaxWavelength ("Max Wavelength", Float) = 10
	}

	CGINCLUDE
	#include "UnityCG.cginc"
	#include "Lighting.cginc"
	#include "AutoLight.cginc"
	#include "UnityGBuffer.cginc"

	sampler2D _MainTex;
	float4 _MainTex_ST;
	float _Samples;
	fixed4 _Color1;
	fixed4 _Color2;
	float _Amplitude;
	float _SpeedMultiplier;
	float _MinWavelength;
	float _MaxWavelength;

	// {wavelength fraction, angle fraction, speed sign}, from the original.
	static const float3 kWaves[8] = {
		float3(0.013057, 0.587232, -1.0),
		float3(0.323078, 0.022073,  1.0),
		float3(0.310725, 0.191367,  1.0),
		float3(0.479646, 0.093988, -1.0),
		float3(0.139999, 0.335770,  1.0),
		float3(0.248458, 0.255532, -1.0),
		float3(0.187190, 0.702764, -1.0),
		float3(0.884915, 0.284208,  1.0)
	};

	struct v2f {
		UNITY_POSITION(pos);
		float2 uv : TEXCOORD0;
		float3 worldPos : TEXCOORD7;
		float3 worldN : TEXCOORD1;
		fixed4 waveCol : TEXCOORD2;
		half3 sh : TEXCOORD3; // ambient + vertex lights (ForwardBase only)
		UNITY_LIGHTING_COORDS(4, 5)
		UNITY_FOG_COORDS(6)
	};

	v2f vert (appdata_full v)
	{
		v2f o;
		UNITY_INITIALIZE_OUTPUT(v2f, o);

		float2 p = mul(unity_ObjectToWorld, v.vertex).xz;
		float amp0 = _Amplitude * 0.1;
		float h = 0.0;
		float sumA = 0.0;
		int count = (int)_Samples;
		for (int i = 0; i < 8; i++)
		{
			if (i >= count)
				break;
			float wl = _MinWavelength + (_MaxWavelength - _MinWavelength) * kWaves[i].x;
			float k = 6.28318 / wl;
			float w = sqrt(9.81 * k) * (_SpeedMultiplier * kWaves[i].z);
			float ang = 6.28318 * kWaves[i].y;
			float d = cos(ang) * p.x + sin(ang) * p.y;
			float a = amp0 / k;
			sumA += a;
			h += a * sin(k * d - w * _Time.y);
		}
		float t = (h / sumA + 1.0) * 0.5;
		t = t * t;
		o.waveCol = lerp(_Color1, _Color2, t);

		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
		float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
		float3 worldN = UnityObjectToWorldNormal(v.normal);
		o.worldPos = worldPos;
		o.worldN = worldN;

		#ifdef UNITY_PASS_FORWARDBASE
		// Per-vertex SH ambient, clamped, then to gamma (the project is in Gamma space).
		// (Not ShadeSH9: in Gamma space it already converts, which would apply the curve twice.)
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

		float2 uv = i.uv;
		half3 albedo = tex2D(_MainTex, uv).rgb * i.waveCol.rgb;

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
		half3 albedo = tex2D(_MainTex, i.uv).rgb * i.waveCol.rgb;
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
			ZTest LEqual
			ZWrite On
			Cull Off
			Blend One Zero

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
			ZTest LEqual
			ZWrite Off
			Cull Off
			Blend One One

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
			ZTest LEqual
			ZWrite On
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
