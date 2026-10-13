// Replacement for the game's "Projector/Multiply" (AssetRipper Free exports every shader as a dummy).
// The game ships Unity's Standard Assets projector shader under this name (blob shadows);
// this is that shader rewritten for Unity 2022.3.
// Properties match the dummy (port/shaders/_reference/properties.md). The game added _Prop.
// Matches the Steam build's compiled shader (reimplemented for this port):
// stock maths plus _Prop scaling the falloff: rgb = lerp(1, cookie.rgb, falloff.a * _Prop). No fog.
Shader "Projector/Multiply" {
	Properties {
		_Prop ("Prop", Range(0,1)) = 1
		_ShadowTex ("Cookie", 2D) = "gray" {}
		_FalloffTex ("FallOff", 2D) = "white" {}
	}

	Subshader {
		Tags { "Queue"="Transparent" }
		Pass {
			ZWrite Off
			ColorMask RGB
			Blend DstColor Zero
			Offset -1, -1
			ZTest LEqual
			Cull Back

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			struct v2f {
				float4 uvShadow : TEXCOORD0;
				float4 uvFalloff : TEXCOORD1;
				float4 pos : SV_POSITION;
			};

			float4x4 unity_Projector;
			float4x4 unity_ProjectorClip;

			v2f vert (float4 vertex : POSITION)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(vertex);
				o.uvShadow = mul(unity_Projector, vertex);
				o.uvFalloff = mul(unity_ProjectorClip, vertex);
				return o;
			}

			sampler2D _ShadowTex;
			sampler2D _FalloffTex;
			fixed _Prop;

			fixed4 frag (v2f i) : SV_Target
			{
				fixed4 texS = tex2Dproj(_ShadowTex, UNITY_PROJ_COORD(i.uvShadow));
				texS.a = 1.0 - texS.a;
				fixed4 texF = tex2Dproj(_FalloffTex, UNITY_PROJ_COORD(i.uvFalloff));
				fixed4 res = lerp(fixed4(1,1,1,0), texS, texF.a * _Prop);
				return res;
			}
			ENDCG
		}
	}
}
