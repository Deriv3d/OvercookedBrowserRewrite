// Replacement for the game's "Custom/HalfLambertBumpRim" (AssetRipper Free exports every shader as a dummy).
// Matches the Steam build's compiled shader (reimplemented for this port):
// a surface shader with a custom lighting function, written out here as explicit vert/frag
// passes so every step is the same as the original:
//   diff  = N.L * _WrapAmount + (1 - _WrapAmount)        (unclamped, can go negative)
//   rim   = pow(1 - saturate(V.N), _RimPower) * _RimColor  (added per light, not light-coloured)
//   light = (albedo * _LightColor0 * diff * 0.5 + rim) * (atten * 2)
//   base  = albedo * (SH ambient + vertex lights) + light
// albedo = _MainTex.rgb * _Brightness; the bumped world normal is not renormalised.
// Shadow casting comes from the Fallback, as in the original.
Shader "Custom/HalfLambertBumpRim" {
	Properties {
		_MainTex ("Base (RGB)", 2D) = "white" {}
		_Brightness ("Brightness", Range(0,5)) = 1
		_BumpMap ("Normalmap", 2D) = "bump" {}
		_WrapAmount ("Wrap Amount", Range(0,1)) = 0.5
		_RimColor ("Rim Color", Color) = (0.26,0.19,0.16,0)
		_RimPower ("Rim Power", Range(0.5,8)) = 3
	}

	CGINCLUDE
	#include "UnityCG.cginc"
	#include "Lighting.cginc"
	#include "AutoLight.cginc"

	sampler2D _MainTex;
	float4 _MainTex_ST;
	sampler2D _BumpMap;
	float4 _BumpMap_ST;
	half _Brightness;
	half _WrapAmount;
	fixed4 _RimColor;
	half _RimPower;

	struct v2f {
		UNITY_POSITION(pos);
		float4 uv : TEXCOORD0; // xy = _MainTex, zw = _BumpMap
		float4 tSpace0 : TEXCOORD1; // TBN rows, w = world position
		float4 tSpace1 : TEXCOORD2;
		float4 tSpace2 : TEXCOORD3;
		half3 sh : TEXCOORD4; // ambient + vertex lights (ForwardBase only)
		UNITY_LIGHTING_COORDS(5, 6)
		UNITY_FOG_COORDS(7)
	};

	v2f vert (appdata_full v)
	{
		v2f o;
		UNITY_INITIALIZE_OUTPUT(v2f, o);
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv.xy = TRANSFORM_TEX(v.texcoord, _MainTex);
		o.uv.zw = TRANSFORM_TEX(v.texcoord, _BumpMap);

		float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
		float3 worldN = UnityObjectToWorldNormal(v.normal);
		float3 worldT = UnityObjectToWorldDir(v.tangent.xyz);
		float3 worldB = cross(worldN, worldT) * v.tangent.w * unity_WorldTransformParams.w;
		o.tSpace0 = float4(worldT.x, worldB.x, worldN.x, worldPos.x);
		o.tSpace1 = float4(worldT.y, worldB.y, worldN.y, worldPos.y);
		o.tSpace2 = float4(worldT.z, worldB.z, worldN.z, worldPos.z);

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

	// The original's lighting function.
	half3 LightingHalfLambertRim (half3 albedo, float3 N, float3 L, float3 V, half atten)
	{
		half diff = dot(N, L) * _WrapAmount + (1.0 - _WrapAmount);
		half3 rim = pow(1.0 - saturate(dot(V, N)), _RimPower) * _RimColor.rgb;
		return (albedo * _LightColor0.rgb * diff * 0.5 + rim) * (atten * 2.0);
	}

	// [webgl-port] Diagnostic only, off by default (0). WebGLPortEffectToggle sets it globally from a URL flag;
	// in the add pass it replaces the colour with one lighting term as grey (0-255 = 0-1):
	// 1 = spot cookie, 2 = distance falloff LUT, 3 = N.L. Used to split the 4-2 flashlight loss.
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

	fixed4 frag (v2f i) : SV_Target
	{
		float3 worldPos = float3(i.tSpace0.w, i.tSpace1.w, i.tSpace2.w);
		float3 V = normalize(UnityWorldSpaceViewDir(worldPos));
		#ifndef USING_DIRECTIONAL_LIGHT
		float3 L = normalize(UnityWorldSpaceLightDir(worldPos));
		#else
		float3 L = _WorldSpaceLightPos0.xyz;
		#endif

		half3 n = UnpackNormal(tex2D(_BumpMap, i.uv.zw));
		float3 N = float3(dot(i.tSpace0.xyz, n), dot(i.tSpace1.xyz, n), dot(i.tSpace2.xyz, n));

		half3 albedo = tex2D(_MainTex, i.uv.xy).rgb * _Brightness;

		#ifdef UNITY_PASS_FORWARDADD
		if (_WebGLPortLightDebug > 3.5)
			return fixed4(albedo, 1.0); // 4 = albedo
		if (_WebGLPortLightDebug > 0.5)
			return fixed4(webglPortLightDebug(worldPos, N).xxx, 1.0);
		#endif

		UNITY_LIGHT_ATTENUATION(atten, i, worldPos);

		fixed4 c;
		c.rgb = LightingHalfLambertRim(albedo, N, L, V, atten);
		#ifdef UNITY_PASS_FORWARDBASE
		c.rgb += albedo * i.sh;
		#endif
		c.a = 1.0;
		UNITY_APPLY_FOG(i.fogCoord, c);
		return c;
	}
	ENDCG

	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200

		Pass {
			Name "FORWARD"
			Tags { "LightMode"="ForwardBase" "SHADOWSUPPORT"="true" }

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

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile_fwdadd
			#pragma multi_compile_fog
			ENDCG
		}
	}

	Fallback "Diffuse"
}
