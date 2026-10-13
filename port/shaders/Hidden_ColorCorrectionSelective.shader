// Replacement for the game's "Hidden/ColorCorrectionSelective" (AssetRipper Free exports every shader as a dummy).
// Unity Standard Assets 5.x image effect (ColorCorrectionCurves' selective step: replaces colours
// near selColor with targetColor), rewritten for Unity 2022.3 / WebGL 2.
Shader "Hidden/ColorCorrectionSelective" {
	Properties {
		_MainTex ("Base (RGB)", 2D) = "" {}
	}

	CGINCLUDE
	#include "UnityCG.cginc"

	struct v2f {
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
	};

	sampler2D _MainTex;
	float4 selColor;
	float4 targetColor;

	v2f vert (appdata_img v)
	{
		v2f o;
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = v.texcoord.xy;
		return o;
	}

	fixed4 frag (v2f i) : SV_Target
	{
		fixed4 color = tex2D(_MainTex, i.uv);
		fixed diff = saturate(2.0 * length(color.rgb - selColor.rgb));
		color = lerp(targetColor, color, diff);
		return color;
	}
	ENDCG

	SubShader {
		Pass {
			ZTest Always Cull Off ZWrite Off
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			ENDCG
		}
	}

	Fallback off
}
