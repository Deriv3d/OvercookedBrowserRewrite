// Replacement for the game's "Hidden/LensFlareCreate" (AssetRipper Free exports every shader as a dummy).
// Unity Standard Assets 5.x image effect used by the Bloom script, rewritten for Unity 2022.3 /
// WebGL 2. Pass order and uniforms match what Bloom.cs sets.
// Adds four mirrored, scaled "ghosts" of the bright pass, tinted colorA..colorD.
Shader "Hidden/LensFlareCreate" {
	Properties {
		_MainTex ("Base (RGB)", 2D) = "" {}
	}

	CGINCLUDE
	#include "UnityCG.cginc"

	struct v2f {
		float4 pos : SV_POSITION;
		float2 uv0 : TEXCOORD0;
		float2 uv1 : TEXCOORD1;
		float2 uv2 : TEXCOORD2;
		float2 uv3 : TEXCOORD3;
	};

	fixed4 colorA;
	fixed4 colorB;
	fixed4 colorC;
	fixed4 colorD;
	sampler2D _MainTex;

	v2f vert (appdata_img v)
	{
		v2f o;
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv0 = ((v.texcoord.xy - 0.5) * -0.85) + 0.5;
		o.uv1 = ((v.texcoord.xy - 0.5) * -1.45) + 0.5;
		o.uv2 = ((v.texcoord.xy - 0.5) * -2.55) + 0.5;
		o.uv3 = ((v.texcoord.xy - 0.5) * -4.15) + 0.5;
		return o;
	}

	fixed4 frag (v2f i) : SV_Target
	{
		fixed4 color = float4(0, 0, 0, 0);
		color += tex2D(_MainTex, i.uv0) * colorA;
		color += tex2D(_MainTex, i.uv1) * colorB;
		color += tex2D(_MainTex, i.uv2) * colorC;
		color += tex2D(_MainTex, i.uv3) * colorD;
		return color;
	}
	ENDCG

	SubShader {
		Blend One One
		ZTest Always Cull Off ZWrite Off
		ColorMask RGB

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			ENDCG
		}
	}

	Fallback off
}
