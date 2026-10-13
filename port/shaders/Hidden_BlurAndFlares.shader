// Replacement for the game's "Hidden/BlurAndFlares" (AssetRipper Free exports every shader as a dummy).
// Unity Standard Assets 5.x image effect used by the Bloom script, rewritten for Unity 2022.3 /
// WebGL 2. Pass order and uniforms match what Bloom.cs sets.
// Passes: 0 post (no blur), 1 anamorphic stretch, 2 pre-filter and cut, 3 post, 4 9-tap gauss blur.
Shader "Hidden/BlurAndFlares" {
	Properties {
		_MainTex ("Base (RGB)", 2D) = "" {}
		_NonBlurredTex ("Base (RGB)", 2D) = "" {}
	}

	CGINCLUDE
	#include "UnityCG.cginc"

	struct v2f {
		float4 pos : SV_POSITION;
		half2 uv : TEXCOORD0;
	};

	struct v2f_opts {
		float4 pos : SV_POSITION;
		half2 uv0 : TEXCOORD0;
		half2 uv1 : TEXCOORD1;
		half2 uv2 : TEXCOORD2;
		half2 uv3 : TEXCOORD3;
		half2 uv4 : TEXCOORD4;
		half2 uv5 : TEXCOORD5;
		half2 uv6 : TEXCOORD6;
	};

	struct v2f_blur {
		float4 pos : SV_POSITION;
		half2 uv : TEXCOORD0;
		half4 uv01 : TEXCOORD1;
		half4 uv23 : TEXCOORD2;
		half4 uv45 : TEXCOORD3;
		half4 uv67 : TEXCOORD4;
	};

	half4 _Offsets;
	half4 _TintColor;
	half _StretchWidth;
	half2 _Threshhold;
	half _Saturation;
	half4 _MainTex_TexelSize;

	sampler2D _MainTex;
	sampler2D _NonBlurredTex;

	v2f vert (appdata_img v)
	{
		v2f o;
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = v.texcoord.xy;
		return o;
	}

	v2f_blur vertWithMultiCoords2 (appdata_img v)
	{
		v2f_blur o;
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = v.texcoord.xy;
		o.uv01 = v.texcoord.xyxy + _Offsets.xyxy * half4(1, 1, -1, -1);
		o.uv23 = v.texcoord.xyxy + _Offsets.xyxy * half4(1, 1, -1, -1) * 2.0;
		o.uv45 = v.texcoord.xyxy + _Offsets.xyxy * half4(1, 1, -1, -1) * 3.0;
		// Outermost tap at 5.0 steps, matches Steam's compiled pass 4.
		o.uv67 = v.texcoord.xyxy + _Offsets.xyxy * half4(1, 1, -1, -1) * 5.0;
		return o;
	}

	v2f_opts vertStretch (appdata_img v)
	{
		v2f_opts o;
		o.pos = UnityObjectToClipPos(v.vertex);
		half b = _StretchWidth;
		o.uv0 = v.texcoord.xy;
		o.uv1 = v.texcoord.xy + b * 2.0 * _Offsets.xy;
		o.uv2 = v.texcoord.xy - b * 2.0 * _Offsets.xy;
		o.uv3 = v.texcoord.xy + b * 4.0 * _Offsets.xy;
		o.uv4 = v.texcoord.xy - b * 4.0 * _Offsets.xy;
		o.uv5 = v.texcoord.xy + b * 6.0 * _Offsets.xy;
		o.uv6 = v.texcoord.xy - b * 6.0 * _Offsets.xy;
		return o;
	}

	v2f_opts vertWithMultiCoords (appdata_img v)
	{
		v2f_opts o;
		o.pos = UnityObjectToClipPos(v.vertex);
		half2 step = _MainTex_TexelSize.xy * _Offsets.xy;
		o.uv0 = v.texcoord.xy;
		o.uv1 = v.texcoord.xy + 0.5 * step;
		o.uv2 = v.texcoord.xy - 0.5 * step;
		o.uv3 = v.texcoord.xy + 1.5 * step;
		o.uv4 = v.texcoord.xy - 1.5 * step;
		o.uv5 = v.texcoord.xy + 2.5 * step;
		o.uv6 = v.texcoord.xy - 2.5 * step;
		return o;
	}

	half4 fragPostNoBlur (v2f i) : SV_Target
	{
		half4 color = tex2D(_MainTex, i.uv);
		return color * 1.0 / (1.0 + Luminance(color.rgb) + 0.5);
	}

	half4 fragGaussBlur (v2f_blur i) : SV_Target
	{
		half4 color = half4(0, 0, 0, 0);
		color += 0.225 * tex2D(_MainTex, i.uv);
		color += 0.150 * tex2D(_MainTex, i.uv01.xy);
		color += 0.150 * tex2D(_MainTex, i.uv01.zw);
		color += 0.110 * tex2D(_MainTex, i.uv23.xy);
		color += 0.110 * tex2D(_MainTex, i.uv23.zw);
		color += 0.075 * tex2D(_MainTex, i.uv45.xy);
		color += 0.075 * tex2D(_MainTex, i.uv45.zw);
		color += 0.0525 * tex2D(_MainTex, i.uv67.xy);
		color += 0.0525 * tex2D(_MainTex, i.uv67.zw);
		return color;
	}

	half4 fragPreAndCut (v2f_opts i) : SV_Target
	{
		half4 color = tex2D(_MainTex, i.uv0);
		color += tex2D(_MainTex, i.uv1);
		color += tex2D(_MainTex, i.uv2);
		color += tex2D(_MainTex, i.uv3);
		color += tex2D(_MainTex, i.uv4);
		color += tex2D(_MainTex, i.uv5);
		color += tex2D(_MainTex, i.uv6);
		color = max(color / 7.0 - _Threshhold.xxxx, float4(0, 0, 0, 0));
		half lum = Luminance(color.rgb);
		color.rgb = lerp(half3(lum, lum, lum), color.rgb, _Saturation) * _TintColor.rgb;
		return color;
	}

	half4 fragStretch (v2f_opts i) : SV_Target
	{
		half4 color = tex2D(_MainTex, i.uv0);
		color = max(color, tex2D(_MainTex, i.uv1));
		color = max(color, tex2D(_MainTex, i.uv2));
		color = max(color, tex2D(_MainTex, i.uv3));
		color = max(color, tex2D(_MainTex, i.uv4));
		color = max(color, tex2D(_MainTex, i.uv5));
		color = max(color, tex2D(_MainTex, i.uv6));
		return color;
	}

	half4 fragPost (v2f_opts i) : SV_Target
	{
		half4 color = tex2D(_MainTex, i.uv0);
		color += tex2D(_MainTex, i.uv1);
		color += tex2D(_MainTex, i.uv2);
		color += tex2D(_MainTex, i.uv3);
		color += tex2D(_MainTex, i.uv4);
		color += tex2D(_MainTex, i.uv5);
		color += tex2D(_MainTex, i.uv6);
		return color * 1.0 / (7.0 + Luminance(color.rgb) + 0.5);
	}
	ENDCG

	SubShader {
		ZTest Always Cull Off ZWrite Off

		// 0
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragPostNoBlur
			ENDCG
		}

		// 1
		Pass {
			CGPROGRAM
			#pragma vertex vertStretch
			#pragma fragment fragStretch
			ENDCG
		}

		// 2
		Pass {
			CGPROGRAM
			#pragma vertex vertWithMultiCoords
			#pragma fragment fragPreAndCut
			ENDCG
		}

		// 3
		Pass {
			CGPROGRAM
			#pragma vertex vertWithMultiCoords
			#pragma fragment fragPost
			ENDCG
		}

		// 4
		Pass {
			CGPROGRAM
			#pragma vertex vertWithMultiCoords2
			#pragma fragment fragGaussBlur
			ENDCG
		}
	}

	Fallback off
}
