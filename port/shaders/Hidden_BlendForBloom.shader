// Replacement for the game's "Hidden/BlendForBloom" (AssetRipper Free exports every shader as a dummy).
// Unity Standard Assets 5.x image effect used by the Bloom script, rewritten for Unity 2022.3 /
// WebGL 2. Pass order and uniforms match what Bloom.cs sets.
// Passes: 0 screen, 1 add, 2 4-tap max, 3 vignette multiply, 4 screen (cheap), 5 add (cheap),
// 6 4-tap blur, 7 vignette blend to dest, 8 clear, 9 additive (One One), 10 max blend.
Shader "Hidden/BlendForBloom" {
	Properties {
		_MainTex ("Screen Blended", 2D) = "" {}
		_ColorBuffer ("Color", 2D) = "" {}
	}

	CGINCLUDE
	#include "UnityCG.cginc"

	struct v2f {
		float4 pos : SV_POSITION;
		float2 uv0 : TEXCOORD0;
		float2 uv1 : TEXCOORD1;
	};

	struct v2f_mt {
		float4 pos : SV_POSITION;
		float2 uv0 : TEXCOORD0;
		float2 uv1 : TEXCOORD1;
		float2 uv2 : TEXCOORD2;
		float2 uv3 : TEXCOORD3;
		float2 uv4 : TEXCOORD4;
	};

	sampler2D _ColorBuffer;
	sampler2D _MainTex;

	half _Intensity;
	half4 _ColorBuffer_TexelSize;
	half4 _MainTex_TexelSize;

	v2f vert (appdata_img v)
	{
		v2f o;
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv0 = v.texcoord.xy;
		o.uv1 = v.texcoord.xy;
		#if UNITY_UV_STARTS_AT_TOP
		if (_ColorBuffer_TexelSize.y < 0)
			o.uv1.y = 1 - o.uv1.y;
		#endif
		return o;
	}

	v2f_mt vertMultiTap (appdata_img v)
	{
		v2f_mt o;
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv4 = v.texcoord.xy;
		o.uv0 = v.texcoord.xy + _MainTex_TexelSize.xy * 0.5;
		o.uv1 = v.texcoord.xy - _MainTex_TexelSize.xy * 0.5;
		o.uv2 = v.texcoord.xy - _MainTex_TexelSize.xy * half2(1, -1) * 0.5;
		o.uv3 = v.texcoord.xy + _MainTex_TexelSize.xy * half2(1, -1) * 0.5;
		return o;
	}

	half4 fragScreen (v2f i) : SV_Target
	{
		half4 addedbloom = tex2D(_MainTex, i.uv0) * _Intensity;
		half4 screencolor = tex2D(_ColorBuffer, i.uv1);
		return 1 - (1 - addedbloom) * (1 - screencolor);
	}

	half4 fragAdd (v2f i) : SV_Target
	{
		half4 addedbloom = tex2D(_MainTex, i.uv0);
		half4 screencolor = tex2D(_ColorBuffer, i.uv1);
		return _Intensity * addedbloom + screencolor;
	}

	half4 fragVignetteMul (v2f i) : SV_Target
	{
		return tex2D(_MainTex, i.uv0) * tex2D(_ColorBuffer, i.uv0);
	}

	half4 fragVignetteBlend (v2f i) : SV_Target
	{
		return half4(1, 1, 1, tex2D(_ColorBuffer, i.uv0).r);
	}

	half4 fragClear (v2f i) : SV_Target
	{
		return 0;
	}

	half4 fragAddOneOne (v2f i) : SV_Target
	{
		return tex2D(_MainTex, i.uv0) * _Intensity;
	}

	half4 frag1Tap (v2f i) : SV_Target
	{
		return tex2D(_MainTex, i.uv0);
	}

	half4 fragMultiTapMax (v2f_mt i) : SV_Target
	{
		half4 outColor = tex2D(_MainTex, i.uv4);
		outColor = max(outColor, tex2D(_MainTex, i.uv0));
		outColor = max(outColor, tex2D(_MainTex, i.uv1));
		outColor = max(outColor, tex2D(_MainTex, i.uv2));
		outColor = max(outColor, tex2D(_MainTex, i.uv3));
		return outColor;
	}

	half4 fragMultiTapBlur (v2f_mt i) : SV_Target
	{
		half4 outColor = 0;
		outColor += tex2D(_MainTex, i.uv0);
		outColor += tex2D(_MainTex, i.uv1);
		outColor += tex2D(_MainTex, i.uv2);
		outColor += tex2D(_MainTex, i.uv3);
		return outColor / 4;
	}
	ENDCG

	SubShader {
		ZTest Always Cull Off ZWrite Off

		// 0: nicer & softer "screen" blend mode
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragScreen
			ENDCG
		}

		// 1: "add" blend mode
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragAdd
			ENDCG
		}

		// 2: several taps, maxed
		Pass {
			CGPROGRAM
			#pragma vertex vertMultiTap
			#pragma fragment fragMultiTapMax
			ENDCG
		}

		// 3: vignette blending
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragVignetteMul
			ENDCG
		}

		// 4: "screen" blend mode (cheapest)
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragScreen
			ENDCG
		}

		// 5: "add" blend mode (cheapest)
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragAdd
			ENDCG
		}

		// 6: "stable" downsampling (blur)
		Pass {
			CGPROGRAM
			#pragma vertex vertMultiTap
			#pragma fragment fragMultiTapBlur
			ENDCG
		}

		// 7: vignette blending (blend to dest)
		Pass {
			Blend Zero SrcAlpha
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragVignetteBlend
			ENDCG
		}

		// 8: clear
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragClear
			ENDCG
		}

		// 9: additive
		Pass {
			Blend One One
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragAddOneOne
			ENDCG
		}

		// 10: max blend
		Pass {
			BlendOp Max
			Blend One One
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag1Tap
			ENDCG
		}
	}

	Fallback off
}
