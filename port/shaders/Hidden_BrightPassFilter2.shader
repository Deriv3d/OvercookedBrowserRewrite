// Replacement for the game's "Hidden/BrightPassFilter2" (AssetRipper Free exports every shader as a dummy).
// Unity Standard Assets 5.x image effect used by the Bloom script, rewritten for Unity 2022.3 /
// WebGL 2. Pass order and uniforms match what Bloom.cs sets.
// Passes: 0 scalar threshold, 1 per-channel (colour) threshold; _Threshhold is the script's spelling.
Shader "Hidden/BrightPassFilter2" {
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
	half4 _Threshhold;

	v2f vert (appdata_img v)
	{
		v2f o;
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = v.texcoord.xy;
		return o;
	}

	half4 fragScalarThresh (v2f i) : SV_Target
	{
		half4 color = tex2D(_MainTex, i.uv);
		color.rgb = max(half3(0, 0, 0), color.rgb - _Threshhold.xxx);
		return color;
	}

	half4 fragColorThresh (v2f i) : SV_Target
	{
		half4 color = tex2D(_MainTex, i.uv);
		color.rgb = max(half3(0, 0, 0), color.rgb - _Threshhold.rgb);
		return color;
	}
	ENDCG

	SubShader {
		ZTest Always Cull Off ZWrite Off

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragScalarThresh
			ENDCG
		}

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragColorThresh
			ENDCG
		}
	}

	Fallback off
}
