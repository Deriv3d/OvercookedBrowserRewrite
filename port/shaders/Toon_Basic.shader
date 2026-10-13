// Replacement for the game's "Toon/Basic" (AssetRipper Free exports every shader as a dummy).
// The game ships Unity's legacy Standard Assets toon shader under this name; this is that
// shader rewritten for Unity 2022.3 (UnityObjectToClipPos); the Steam build's compiled copy
// (d3d11) confirms the maths, Cull Off and no fog. Lighting comes entirely
// from the _ToonShade cubemap looked up with the view-space normal, so scene lights and
// lightmaps don't affect it, same as the original.
// Properties match the dummy (port/shaders/_reference/properties.md).
Shader "Toon/Basic" {
	Properties {
		_Color ("Main Color", Color) = (.5,.5,.5,1)
		_MainTex ("Base (RGB)", 2D) = "white" {}
		_ToonShade ("ToonShader Cubemap(RGB)", CUBE) = "" { }
	}

	SubShader {
		Tags { "RenderType"="Opaque" }
		Pass {
			Name "BASE"
			Cull Off
			ZWrite On
			ZTest LEqual
			Blend One Zero

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			samplerCUBE _ToonShade;
			float4 _MainTex_ST;
			float4 _Color;

			struct appdata {
				float4 vertex : POSITION;
				float2 texcoord : TEXCOORD0;
				float3 normal : NORMAL;
			};

			struct v2f {
				float4 pos : SV_POSITION;
				float2 texcoord : TEXCOORD0;
				float3 cubenormal : TEXCOORD1;
			};

			v2f vert (appdata v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
				o.cubenormal = mul(UNITY_MATRIX_MV, float4(v.normal, 0)).xyz;
				return o;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				fixed4 col = _Color * tex2D(_MainTex, i.texcoord);
				fixed4 cube = texCUBE(_ToonShade, i.cubenormal);
				fixed4 c = fixed4(2.0 * cube.rgb * col.rgb, col.a);
				return c;
			}
			ENDCG
		}
	}

	Fallback "VertexLit"
}
