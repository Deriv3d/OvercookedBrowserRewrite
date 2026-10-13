// Replacement for the game's "Custom/HalfLambertBumpRimAlpha" (AssetRipper Free exports every shader as a dummy).
// Matches the Steam build's compiled shader (reimplemented for this port).
// Alpha-blended (not cut out) but still writes depth. Lit by the main directional light only:
// the original has no ForwardAdd pass, so point lights don't light it.
//   tex   = _MainTex * _Color;  albedo = tex.rgb * _Brightness
//   diff  = N.L * _WrapAmount + (1 - _WrapAmount)          (unclamped)
//   rim   = pow(1 - saturate(V.N), _RimPower) * _RimColor
//   rgb   = albedo * SH + (albedo * _LightColor0 * diff * 0.5 + rim) * (atten * 2);  a = tex.a
// _Cutoff is used only by the shadow caster, which clips at _Cutoff and then dithers by alpha
// (what a surface shader with alphatest:_Cutoff alpha:fade addshadow produces).
// The original's META pass (lightmap baking only) is left out. No Fallback, as in the original.
Shader "Custom/HalfLambertBumpRimAlpha" {
	Properties {
		_MainTex ("Base (RGB) Trans (A)", 2D) = "white" {}
		_Color ("Color", Color) = (1,1,1,1)
		_Brightness ("Brightness", Range(0,5)) = 1
		_BumpMap ("Normalmap", 2D) = "bump" {}
		_WrapAmount ("Wrap Amount", Range(0,1)) = 0.5
		_RimColor ("Rim Color", Color) = (0.26,0.19,0.16,0)
		_RimPower ("Rim Power", Range(0.5,8)) = 0
		_Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
	}

	CGINCLUDE
	#include "UnityCG.cginc"
	#include "Lighting.cginc"
	#include "AutoLight.cginc"

	sampler2D _MainTex;
	float4 _MainTex_ST;
	sampler2D _BumpMap;
	float4 _BumpMap_ST;
	fixed4 _Color;
	half _Brightness;
	half _WrapAmount;
	fixed4 _RimColor;
	half _RimPower;
	fixed _Cutoff;
	sampler3D _DitherMaskLOD;

	struct v2f {
		UNITY_POSITION(pos);
		float4 uv : TEXCOORD0; // xy = _MainTex, zw = _BumpMap
		float4 tSpace0 : TEXCOORD1; // TBN rows, w = world position
		float4 tSpace1 : TEXCOORD2;
		float4 tSpace2 : TEXCOORD3;
		half3 sh : TEXCOORD4; // ambient + vertex lights
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

		UNITY_TRANSFER_LIGHTING(o, v.texcoord1.xy);
		UNITY_TRANSFER_FOG(o, o.pos);
		return o;
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

		fixed4 tex = tex2D(_MainTex, i.uv.xy) * _Color;
		half3 albedo = tex.rgb * _Brightness;

		half3 rim = pow(1.0 - saturate(dot(V, N)), _RimPower) * _RimColor.rgb;
		half diff = dot(N, L) * _WrapAmount + (1.0 - _WrapAmount);

		UNITY_LIGHT_ATTENUATION(atten, i, worldPos);

		fixed4 c;
		c.rgb = albedo * i.sh + (albedo * _LightColor0.rgb * diff * 0.5 + rim) * (atten * 2.0);
		c.a = tex.a; // no clip in this pass
		UNITY_APPLY_FOG(i.fogCoord, c);
		return c;
	}

	// The clip position leaves the vertex program as a separate output, so the fragment can
	// take VPOS (both in one input is a duplicate system value), as surface shaders do.
	struct v2fShadow {
		V2F_SHADOW_CASTER_NOPOS
		float2 uv : TEXCOORD1;
	};

	v2fShadow vertShadow (appdata_full v, out float4 opos : SV_POSITION)
	{
		v2fShadow o;
		UNITY_INITIALIZE_OUTPUT(v2fShadow, o);
		TRANSFER_SHADOW_CASTER_NOPOS(o, opos)
		o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
		return o;
	}

	fixed4 fragShadow (v2fShadow i, UNITY_VPOS_TYPE vpos : VPOS) : SV_Target
	{
		fixed a = tex2D(_MainTex, i.uv).a * _Color.a;
		clip(a - _Cutoff);
		half dither = tex3D(_DitherMaskLOD, float3(vpos.xy * 0.25, a * 0.9375)).a;
		clip(dither - 0.01);
		SHADOW_CASTER_FRAGMENT(i)
	}
	ENDCG

	SubShader {
		Tags { "Queue"="Transparent" "RenderType"="Transparent" }
		LOD 200

		Pass {
			Name "FORWARD"
			Tags { "LightMode"="ForwardBase" "SHADOWSUPPORT"="true" }
			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite On

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile_fwdbase
			#pragma multi_compile_fog
			ENDCG
		}

		Pass {
			Name "ShadowCaster"
			Tags { "LightMode"="ShadowCaster" }
			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite On

			CGPROGRAM
			#pragma vertex vertShadow
			#pragma fragment fragShadow
			#pragma target 3.0
			#pragma multi_compile_shadowcaster
			ENDCG
		}
	}
}
