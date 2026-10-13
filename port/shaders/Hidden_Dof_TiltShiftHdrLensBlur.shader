// Replacement for the game's "Hidden/Dof/TiltShiftHdrLensBlur" (AssetRipper Free exports every shader as a dummy).
// Unity Standard Assets 5.x tilt-shift effect (TiltShift script, on the world maps), rewritten
// for Unity 2022.3 / WebGL 2. Pass layout matches the game's script: pass = quality * 2 (+1 for
// iris mode), quality Preview 0, Normal 1, High 2; pass 6 = upsample when downsampling.
// Uniforms: _BlurSize (max blur in texels), _BlurArea, _Blurred.
// Not verified: the disc kernels are freshly generated (Vogel spiral, 28 taps) rather than the
// original constants; High adds a second, half-radius ring of 12 taps.
Shader "Hidden/Dof/TiltShiftHdrLensBlur" {
	Properties {
		_MainTex ("-", 2D) = "" {}
	}

	CGINCLUDE
	#include "UnityCG.cginc"

	struct v2f {
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
		float2 uv1 : TEXCOORD1;
	};

	float4 _MainTex_TexelSize;
	float _BlurSize;
	float _BlurArea;
	sampler2D _MainTex;
	sampler2D _Blurred;

	static const int NumSmallDiscSamples = 12;
	static const half2 SmallDiscKernel[12] =
	{
		half2(0.32351, 0.00000),
		half2(-0.33220, 0.30432),
		half2(0.04703, -0.53591),
		half2(0.37057, 0.48334),
		half2(-0.66027, -0.11679),
		half2(0.61210, -0.38937),
		half2(-0.20134, 0.74899),
		half2(-0.37886, -0.72947),
		half2(0.81287, 0.29686),
		half2(-0.83773, 0.34580),
		half2(0.40058, -0.85601),
		half2(0.29393, 0.93709)
	};

	static const int NumDiscSamples = 28;
	static const half2 DiscKernel[28] =
	{
		half2(0.26359, 0.00000),
		half2(-0.25567, 0.23422),
		half2(0.03532, -0.40244),
		half2(0.27411, 0.35753),
		half2(-0.48326, -0.08548),
		half2(0.44442, -0.28271),
		half2(-0.14526, 0.54036),
		half2(-0.27190, -0.52352),
		half2(0.58081, 0.21211),
		half2(-0.59630, 0.24615),
		half2(0.28419, -0.60731),
		half2(0.20793, 0.66289),
		half2(-0.62116, -0.35997),
		half2(0.72295, -0.15894),
		half2(-0.43806, 0.62310),
		half2(-0.10055, -0.77593),
		half2(0.61363, 0.51717),
		half2(-0.82128, 0.03397),
		half2(0.59606, -0.59317),
		half2(-0.03969, 0.85843),
		half2(-0.56210, -0.67358),
		half2(0.88684, 0.11932),
		half2(-0.74859, 0.52085),
		half2(0.20383, -0.90606),
		half2(0.46989, 0.82001),
		half2(-0.91570, -0.29213),
		half2(0.88684, -0.40975),
		half2(-0.38311, 0.91544)
	};

	v2f vert (appdata_img v)
	{
		v2f o;
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = v.texcoord.xy;
		o.uv1 = v.texcoord.xy;
		#if UNITY_UV_STARTS_AT_TOP
		if (_MainTex_TexelSize.y < 0)
			o.uv1.y = 1 - o.uv1.y;
		#endif
		return o;
	}

	half WeightFieldMode (half2 uv)
	{
		half2 tapCoord = uv * 2.0 - 1.0;
		return abs(tapCoord.y * _BlurArea);
	}

	half WeightIrisMode (half2 uv)
	{
		half2 tapCoord = uv * 2.0 - 1.0;
		return dot(tapCoord, tapCoord) * _BlurArea;
	}

	float4 fragFieldPreview (v2f i) : SV_Target { return WeightFieldMode(i.uv) * 0.5; }
	float4 fragIrisPreview (v2f i) : SV_Target { return WeightIrisMode(i.uv) * 0.5; }

	float4 BlurDisc (float2 uv, float w, bool secondRing)
	{
		float4 sum = tex2D(_MainTex, uv);
		if (w < 1e-2)
			return float4(sum.rgb, w);
		float2 scale = _MainTex_TexelSize.xy * w;
		float count = 1.0;
		for (int l = 0; l < NumDiscSamples; l++)
			sum += tex2D(_MainTex, uv + DiscKernel[l] * scale);
		count += NumDiscSamples;
		if (secondRing)
		{
			for (int k = 0; k < NumSmallDiscSamples; k++)
				sum += tex2D(_MainTex, uv + SmallDiscKernel[k] * scale * 0.5);
			count += NumSmallDiscSamples;
		}
		return float4(sum.rgb / count, w);
	}

	float4 fragField (v2f i) : SV_Target { return BlurDisc(i.uv, clamp(WeightFieldMode(i.uv), 0, _BlurSize), false); }
	float4 fragIris (v2f i) : SV_Target { return BlurDisc(i.uv, clamp(WeightIrisMode(i.uv), 0, _BlurSize), false); }
	float4 fragFieldHQ (v2f i) : SV_Target { return BlurDisc(i.uv, clamp(WeightFieldMode(i.uv), 0, _BlurSize), true); }
	float4 fragIrisHQ (v2f i) : SV_Target { return BlurDisc(i.uv, clamp(WeightIrisMode(i.uv), 0, _BlurSize), true); }

	float4 fragUpsample (v2f i) : SV_Target
	{
		float4 blurred = tex2D(_Blurred, i.uv1);
		float4 frame = tex2D(_MainTex, i.uv);
		return lerp(frame, blurred, saturate(blurred.a));
	}
	ENDCG

	SubShader {
		ZTest Always Cull Off ZWrite Off

		// 0
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragFieldPreview
			ENDCG
		}

		// 1
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragIrisPreview
			ENDCG
		}

		// 2
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragField
			ENDCG
		}

		// 3
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragIris
			ENDCG
		}

		// 4
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragFieldHQ
			ENDCG
		}

		// 5
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragIrisHQ
			ENDCG
		}

		// 6
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragUpsample
			ENDCG
		}
	}

	Fallback off
}
