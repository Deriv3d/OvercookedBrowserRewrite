// Replacement for the game's "Mobile/Color" (AssetRipper Free exports every shader as a dummy).
// Matches the Steam build's compiled shader (reimplemented for this port):
// a Lambert surface shader with noforwardadd and no texture, written out as one ForwardBase
// pass. Point lights reach it only through vertex lights/SH, as in the original.
//   rgb = _LightColor0 * _Color * max(0, N.L) * atten + SH * _Color;  a = 1
// Shadow casting comes from the Fallback, as in the original.
Shader "Mobile/Color" {
	Properties {
		_Color ("Color", Color) = (1,1,1,1)
	}

	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 150

		Pass {
			Name "FORWARD"
			Tags { "LightMode"="ForwardBase" "SHADOWSUPPORT"="true" }

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile_fwdbase
			#pragma multi_compile_fog
			#include "UnityCG.cginc"
			#include "Lighting.cginc"
			#include "AutoLight.cginc"

			fixed4 _Color;

			struct v2f {
				UNITY_POSITION(pos);
				float3 worldPos : TEXCOORD0;
				float3 worldN : TEXCOORD1;
				half3 sh : TEXCOORD2; // ambient + vertex lights
				UNITY_LIGHTING_COORDS(3, 4)
				UNITY_FOG_COORDS(5)
			};

			v2f vert (appdata_full v)
			{
				v2f o;
				UNITY_INITIALIZE_OUTPUT(v2f, o);
				o.pos = UnityObjectToClipPos(v.vertex);
				float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
				float3 worldN = UnityObjectToWorldNormal(v.normal);
				o.worldPos = worldPos;
				o.worldN = worldN;

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
				UNITY_LIGHT_ATTENUATION(atten, i, i.worldPos);
				fixed4 c;
				c.rgb = _LightColor0.rgb * _Color.rgb * (max(0.0, dot(i.worldN, _WorldSpaceLightPos0.xyz)) * atten) + i.sh * _Color.rgb;
				c.a = 1.0;
				UNITY_APPLY_FOG(i.fogCoord, c);
				return c;
			}
			ENDCG
		}
	}

	Fallback "Mobile/VertexLit"
}
