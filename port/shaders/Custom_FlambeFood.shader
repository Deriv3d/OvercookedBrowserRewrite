// Replacement for the game's "Custom/FlambeFood" (AssetRipper Free exports every shader as a dummy).
// Matches the Steam build's compiled shader (reimplemented for this port).
// Alpha-blended, writes depth, lit by the main directional light only (no ForwardAdd pass),
// casts no shadows (no ShadowCaster, no Fallback). "Prop" and "FlambeProp" really have no
// leading underscore. Every flambé setting is blended by FlambeProp before it is used:
//   f      = FlambeProp
//   col    = lerp(_Color, _FlambeColor, f);  bright = lerp(_Brightness, _FlambeBrightness, f)
//   wrap   = lerp(_WrapAmount, _FlambeWrapAmount, f)
//   rim    = pow(1 - saturate(V.N), lerp(_RimPower, _FlambeRimPower, f)) * lerp(_RimColor, _FlambeRimColor, f)
//   base   = _Texture1 * col * (1 - Prop) + _Texture2 * col * Prop;  albedo = base.rgb * bright
//   diff   = N.L * wrap + (1 - wrap)                                     (unclamped)
//   rgb    = albedo * SH + (albedo * _LightColor0 * diff * 0.5 + rim) * (atten * 2);  a = base.a
// _Cutoff is declared but unused, as in the original. The META pass (lightmap baking only) is left out.
Shader "Custom/FlambeFood" {
	Properties {
		_Texture1 ("Starting Texture (RGB)", 2D) = "white" {}
		_Texture2 ("Ending Texture (RGB)", 2D) = "white" {}
		Prop ("Prop", Range(0,1)) = 0
		_Color ("Color", Color) = (1,1,1,1)
		_Brightness ("Brightness", Range(0,5)) = 1
		_BumpMap ("Normalmap", 2D) = "bump" {}
		_WrapAmount ("Wrap Amount", Range(0,1)) = 0.5
		_RimColor ("Rim Color", Color) = (0.26,0.19,0.16,0)
		_RimPower ("Rim Power", Range(0.5,8)) = 0
		_Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
		FlambeProp ("Flambe Prop", Range(0,1)) = 0
		_FlambeColor ("Flambe Color", Color) = (1,1,1,1)
		_FlambeBrightness ("Flambe Brightness", Range(0,5)) = 1
		_FlambeWrapAmount ("Flambe Wrap Amount", Range(0,1)) = 0.5
		_FlambeRimColor ("Flambe Rim Color", Color) = (0.26,0.19,0.16,0)
		_FlambeRimPower ("Flambe Rim Power", Range(0.5,8)) = 0
	}

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

			#include "UnityCG.cginc"
			#include "Lighting.cginc"
			#include "AutoLight.cginc"

			sampler2D _Texture1;
			float4 _Texture1_ST;
			sampler2D _Texture2;
			float4 _Texture2_ST;
			sampler2D _BumpMap;
			float4 _BumpMap_ST;
			half Prop;
			fixed4 _Color;
			half _Brightness;
			half _WrapAmount;
			fixed4 _RimColor;
			half _RimPower;
			half FlambeProp;
			fixed4 _FlambeColor;
			half _FlambeBrightness;
			half _FlambeWrapAmount;
			fixed4 _FlambeRimColor;
			half _FlambeRimPower;

			struct v2f {
				UNITY_POSITION(pos);
				float4 uv : TEXCOORD0; // xy = _Texture1, zw = _Texture2
				float2 uv2 : TEXCOORD1; // _BumpMap
				float4 tSpace0 : TEXCOORD2; // TBN rows, w = world position
				float4 tSpace1 : TEXCOORD3;
				float4 tSpace2 : TEXCOORD4;
				half3 sh : TEXCOORD5; // ambient + vertex lights
				UNITY_LIGHTING_COORDS(6, 7)
				UNITY_FOG_COORDS(8)
			};

			v2f vert (appdata_full v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				o.pos = UnityObjectToClipPos(v.vertex);
				o.uv.xy = TRANSFORM_TEX(v.texcoord, _Texture1);
				o.uv.zw = TRANSFORM_TEX(v.texcoord, _Texture2);
				o.uv2 = TRANSFORM_TEX(v.texcoord, _BumpMap);

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
				half f = FlambeProp;
				float3 worldPos = float3(i.tSpace0.w, i.tSpace1.w, i.tSpace2.w);
				float3 V = normalize(UnityWorldSpaceViewDir(worldPos));
				float3 L = _WorldSpaceLightPos0.xyz;

				half3 n = UnpackNormal(tex2D(_BumpMap, i.uv2));
				float3 N = float3(dot(i.tSpace0.xyz, n), dot(i.tSpace1.xyz, n), dot(i.tSpace2.xyz, n));

				half rimPow = _FlambeRimPower * f + _RimPower * (1.0 - f);
				half rim = pow(1.0 - saturate(dot(V, N)), rimPow);
				half3 rimCol = (_FlambeRimColor.rgb * f + _RimColor.rgb * (1.0 - f)) * rim;
				half wrap = _FlambeWrapAmount * f + _WrapAmount * (1.0 - f);
				half diff = dot(N, L) * wrap + (1.0 - wrap);
				half bright = _FlambeBrightness * f + _Brightness * (1.0 - f);
				fixed4 col = _FlambeColor * f + _Color * (1.0 - f);
				fixed4 base = tex2D(_Texture1, i.uv.xy) * col * (1.0 - Prop) + tex2D(_Texture2, i.uv.zw) * col * Prop;
				half3 albedo = base.rgb * bright;

				UNITY_LIGHT_ATTENUATION(atten, i, worldPos);

				fixed4 c;
				c.rgb = albedo * i.sh + (albedo * _LightColor0.rgb * diff * 0.5 + rimCol) * (atten * 2.0);
				c.a = base.a;
				UNITY_APPLY_FOG(i.fogCoord, c);
				return c;
			}
			ENDCG
		}
	}
}
