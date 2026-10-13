// Replacement for the game's "Hidden/ColorCorrectionCurves" (AssetRipper Free exports every shader as a dummy).
// Unity Standard Assets 5.x image effect (ColorCorrectionCurves with depth), rewritten for
// Unity 2022.3 / WebGL 2. Blends the near curves (_RgbTex) to the far curves (_RgbDepthTex)
// by _ZCurve(linear depth).
Shader "Hidden/ColorCorrectionCurves" {
	Properties {
		_MainTex ("Base (RGB)", 2D) = "" {}
		_RgbTex ("_RgbTex (RGB)", 2D) = "" {}
		_ZCurve ("_ZCurve (RGB)", 2D) = "" {}
		_RgbDepthTex ("_RgbDepthTex (RGB)", 2D) = "" {}
	}

	CGINCLUDE
	#include "UnityCG.cginc"

	struct v2f {
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
		float2 uv2 : TEXCOORD1;
	};

	sampler2D _MainTex;
	sampler2D_float _CameraDepthTexture;
	uniform float4 _MainTex_TexelSize;

	sampler2D _RgbTex;
	sampler2D _ZCurve;
	sampler2D _RgbDepthTex;
	fixed _Saturation;

	v2f vert (appdata_img v)
	{
		v2f o;
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = v.texcoord.xy;
		o.uv2 = v.texcoord.xy; // not TRANSFORM_TEX: nothing sets _CameraDepthTexture_ST, so it could read as 0
		#if UNITY_UV_STARTS_AT_TOP
		if (_MainTex_TexelSize.y < 0)
			o.uv2.y = 1 - o.uv2.y;
		#endif
		return o;
	}

	half4 frag (v2f i) : SV_Target
	{
		half4 color = tex2D(_MainTex, i.uv);
		half3 ycoords = half3(0.5, 1.5, 2.5) * 0.25;

		half3 red = tex2D(_RgbTex, half2(color.r, ycoords.x)).rgb * half3(1, 0, 0);
		half3 green = tex2D(_RgbTex, half2(color.g, ycoords.y)).rgb * half3(0, 1, 0);
		half3 blue = tex2D(_RgbTex, half2(color.b, ycoords.z)).rgb * half3(0, 0, 1);

		half theDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv2);
		half zval = tex2D(_ZCurve, half2(Linear01Depth(theDepth), 0.5)).x;

		half3 depthRed = tex2D(_RgbDepthTex, half2(color.r, ycoords.x)).rgb * half3(1, 0, 0);
		half3 depthGreen = tex2D(_RgbDepthTex, half2(color.g, ycoords.y)).rgb * half3(0, 1, 0);
		half3 depthBlue = tex2D(_RgbDepthTex, half2(color.b, ycoords.z)).rgb * half3(0, 0, 1);

		color = half4(lerp(red + green + blue, depthRed + depthGreen + depthBlue, zval), color.a);

		half lum = Luminance(color.rgb);
		color.rgb = lerp(half3(lum, lum, lum), color.rgb, _Saturation);
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
