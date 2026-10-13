// Replacement for the game's "Hidden/Vignetting" (AssetRipper Free exports every shader as a dummy).
// Unity Standard Assets 5.x image effect (used by VignetteAndChromaticAberration), rewritten
// for Unity 2022.3 / WebGL 2. Uniform names and the single pass match what the game's script sets
// (_Intensity, _Blur, _VignetteTex), see port/shaders/_reference/hidden-image-effects.md.
Shader "Hidden/Vignetting" {
	Properties {
		_MainTex ("Base", 2D) = "white" {}
		_VignetteTex ("Vignette", 2D) = "white" {}
	}

	CGINCLUDE
	#include "UnityCG.cginc"

	struct v2f {
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
		float2 uv2 : TEXCOORD1;
	};

	sampler2D _MainTex;
	sampler2D _VignetteTex;
	half _Intensity;
	half _Blur;
	float4 _MainTex_TexelSize;

	v2f vert (appdata_img v)
	{
		v2f o;
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = v.texcoord.xy;
		o.uv2 = v.texcoord.xy;
		#if UNITY_UV_STARTS_AT_TOP
		if (_MainTex_TexelSize.y < 0)
			o.uv2.y = 1.0 - o.uv2.y;
		#endif
		return o;
	}

	half4 frag (v2f i) : SV_Target
	{
		half2 coords = (i.uv - 0.5) * 2.0;
		half coordDot = dot(coords, coords);
		half4 color = tex2D(_MainTex, i.uv);
		float mask = 1.0 - coordDot * _Intensity;
		half4 colorBlur = tex2D(_VignetteTex, i.uv2);
		color = lerp(color, colorBlur, saturate(_Blur * coordDot));
		return color * mask;
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
