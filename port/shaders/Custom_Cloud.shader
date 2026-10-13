// Replacement for the game's "Custom/Cloud" (AssetRipper Free exports every shader as a dummy).
// Matches the Steam build's compiled shader (reimplemented for this port):
// a GrabPass of the scene behind, then an unlit pass that fades that grab towards white where
// the object-space normal faces along view-space z:
//   f   = saturate(dot(normalize(model-view row 2), objectNormal) * 1.2)
//   rgb = grab.rgb * (1 - f) + f;  a = grab.a
// So cloud edges show the scene behind and faces towards the camera go white. The object-space
// normal is used as is (non-uniform scale isn't corrected), as in the original.
// _Color, _MainTex and _WrapAmount are declared but unused, as in the original. No Fallback.
Shader "Custom/Cloud" {
	Properties {
		_Color ("Main Color", Color) = (1,1,1,1)
		_MainTex ("Base (RGB) Trans (A)", 2D) = "white" {}
		_WrapAmount ("Wrap Amount", Range(0,1)) = 0.5
	}

	SubShader {
		Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Opaque" }

		GrabPass { "_GrabTexture" }

		Pass {
			Blend One Zero
			ZWrite On
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _GrabTexture;

			struct v2f {
				float4 pos : SV_POSITION;
				float4 grabPos : TEXCOORD0;
				float3 normal : TEXCOORD1; // object space
			};

			v2f vert (appdata_base v)
			{
				v2f o;
				o.pos = UnityObjectToClipPos(v.vertex);
				o.grabPos = ComputeGrabScreenPos(o.pos);
				o.normal = v.normal;
				return o;
			}

			fixed4 frag (v2f i) : SV_Target
			{
				float3 a = normalize(UNITY_MATRIX_MV[2].xyz);
				half f = saturate(dot(a, i.normal) * 1.2);
				fixed4 g = tex2Dproj(_GrabTexture, i.grabPos);
				fixed4 c;
				c.rgb = g.rgb * (1.0 - f) + f;
				c.a = g.a;
				return c;
			}
			ENDCG
		}
	}
}
