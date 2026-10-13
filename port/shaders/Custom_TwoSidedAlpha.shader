// Replacement for the game's "Custom/TwoSidedAlpha" (AssetRipper Free exports every shader as a dummy).
// Matches the Steam build's compiled shader (reimplemented for this port):
// a Lambert surface shader with alpha test and premultiplied blending, written out as explicit
// passes. Cull Off, but the normal is NOT flipped for back faces, as in the original.
//   c = _MainTex * vertex colour * _Color;  clip(c.a - _CutOff);  c.rgb *= c.a
//   rgb = c.rgb * _LightColor0 * max(0, N.L) * atten + c.rgb * SH;  a = c.a
// _CutOff has a capital O, as in the original. No own shadow caster (shadows come from the
// Fallback). The original's PREPASS/DEFERRED/META passes are left out (the game renders forward).
Shader "Custom/TwoSidedAlpha" {
	Properties {
		_Color ("Main Color", Color) = (1,1,1,1)
		_MainTex ("Base (RGB)", 2D) = "white" {}
		_CutOff ("AlphaCutoff", Float) = 0.1
	}

	CGINCLUDE
	#include "UnityCG.cginc"
	#include "Lighting.cginc"
	#include "AutoLight.cginc"

	sampler2D _MainTex;
	float4 _MainTex_ST;
	fixed4 _Color;
	fixed _CutOff;

	struct v2f {
		UNITY_POSITION(pos);
		float2 uv : TEXCOORD0;
		float3 worldPos : TEXCOORD1;
		float3 worldN : TEXCOORD2;
		fixed4 vcol : COLOR0;
		half3 sh : TEXCOORD3; // ambient + vertex lights (ForwardBase only)
		UNITY_LIGHTING_COORDS(4, 5)
		UNITY_FOG_COORDS(6)
	};

	v2f vert (appdata_full v)
	{
		v2f o;
		UNITY_INITIALIZE_OUTPUT(v2f, o);
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
		o.vcol = v.color * _Color;
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

		fixed4 t = tex2D(_MainTex, i.uv);
		clip(t.a * i.vcol.a - _CutOff);
		fixed4 c = t * i.vcol;
		c.rgb *= c.a;

		UNITY_LIGHT_ATTENUATION(atten, i, worldPos);

		fixed4 o;
		o.rgb = c.rgb * _LightColor0.rgb * (max(0.0, dot(i.worldN, L)) * atten);
		#ifdef UNITY_PASS_FORWARDBASE
		o.rgb += c.rgb * i.sh;
		#endif
		o.a = c.a;
		UNITY_APPLY_FOG(i.fogCoord, o);
		return o;
	}
	ENDCG

	SubShader {
		Tags { "Queue"="Transparent" "RenderType"="Transparent" }
		LOD 0

		Pass {
			Name "FORWARD"
			Tags { "LightMode"="ForwardBase" "SHADOWSUPPORT"="true" }
			Blend One OneMinusSrcAlpha
			ColorMask RGB
			ZWrite On
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
			Blend One One
			ColorMask RGB
			ZWrite Off
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile_fwdadd
			#pragma multi_compile_fog
			ENDCG
		}
	}

	Fallback "Transparent/VertexLit"
}
