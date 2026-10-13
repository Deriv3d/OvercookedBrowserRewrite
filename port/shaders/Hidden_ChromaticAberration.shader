// Replacement for the game's "Hidden/ChromaticAberration" (AssetRipper Free exports every shader as a dummy).
// Unity Standard Assets 5.x image effect (used by VignetteAndChromaticAberration), rewritten
// for Unity 2022.3 / WebGL 2. Pass order matches the script: 0 = 4-tap downsample for the
// vignette blur, 1 = simple aberration, 2 = advanced (axial) aberration.
Shader "Hidden/ChromaticAberration" {
	Properties {
		_MainTex ("Base", 2D) = "" {}
	}

	CGINCLUDE
	#include "UnityCG.cginc"

	struct v2f {
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
	};

	sampler2D _MainTex;
	float4 _MainTex_TexelSize;
	float _ChromaticAberration;
	float _AxialAberration;
	float _Luminance;
	half2 _BlurDistance;

	v2f vert (appdata_img v)
	{
		v2f o;
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = v.texcoord.xy;
		return o;
	}

	half4 fragDs (v2f i) : SV_Target
	{
		half4 c = tex2D(_MainTex, i.uv + _MainTex_TexelSize.xy * 0.5);
		c += tex2D(_MainTex, i.uv - _MainTex_TexelSize.xy * 0.5);
		c += tex2D(_MainTex, i.uv + _MainTex_TexelSize.xy * float2(0.5, -0.5));
		c += tex2D(_MainTex, i.uv - _MainTex_TexelSize.xy * float2(0.5, -0.5));
		return c / 4.0;
	}

	half4 frag (v2f i) : SV_Target
	{
		half2 coords = (i.uv - 0.5) * 2.0;
		half coordDot = dot(coords, coords);
		half2 uvG = i.uv - _MainTex_TexelSize.xy * _ChromaticAberration * coords * coordDot;
		half4 color = tex2D(_MainTex, i.uv);
		color.g = tex2D(_MainTex, uvG).g;
		return color;
	}

	static const int SmallDiscKernelSamples = 9;
	static const half2 SmallDiscKernel[SmallDiscKernelSamples] =
	{
		half2(-0.926212, -0.40581),
		half2(-0.695914, 0.457137),
		half2(-0.203345, 0.820716),
		half2(0.96234, -0.194983),
		half2(0.473434, -0.480026),
		half2(0.519456, 0.767022),
		half2(0.185461, -0.893124),
		half2(0.89642, 0.412458),
		half2(-0.32194, -0.932615),
	};

	// Stock 5.x advanced mode: the blur radius is max(axial, tangential) everywhere, and only red
	// and blue move towards the blurred tap at edges; green stays sharp (no lateral shift here).
	half4 fragComplex (v2f i) : SV_Target
	{
		half2 coords = (i.uv - 0.5) * 2.0;
		half coordDot = dot(coords, coords);
		half4 color = tex2D(_MainTex, i.uv);
		half tangentialStrength = _ChromaticAberration * coordDot * coordDot;
		half maxOfs = clamp(max(_AxialAberration, tangentialStrength), _BlurDistance.x, _BlurDistance.y);

		half4 blurredTap = color * 0.1;
		for (int l = 0; l < SmallDiscKernelSamples; l++)
		{
			half2 sampleUV = i.uv + SmallDiscKernel[l].xy * _MainTex_TexelSize.xy * maxOfs;
			blurredTap.rgb += tex2D(_MainTex, sampleUV).rgb;
		}
		blurredTap.rgb /= (half)SmallDiscKernelSamples + 0.2;

		half lumDiff = Luminance(abs(blurredTap.rgb - color.rgb));
		half isEdge = saturate(_Luminance * lumDiff);
		color.rb = lerp(color.rb, blurredTap.rb, isEdge);
		return color;
	}
	ENDCG

	SubShader {
		ZTest Always Cull Off ZWrite Off

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragDs
			ENDCG
		}

		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			ENDCG
		}

		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragComplex
			ENDCG
		}
	}

	Fallback off
}
