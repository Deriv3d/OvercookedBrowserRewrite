// Replacement for the game's "Hidden/Dof/DepthOfFieldHdr" (AssetRipper Free exports every shader as a dummy).
// Unity Standard Assets 5.x DepthOfField (disc blur path; used in one scene), rewritten for
// Unity 2022.3 / WebGL 2. The script picks passes by number, so all 19 slots exist; the ones
// its disc-blur path uses are real, the rest pass the image through:
//   0 write CoC to alpha, 2 foreground CoC blur, 4 capture foreground CoC, 6 downsample keeping
//   CoC, 11/17 disc blur (fewer/more taps), 12/18 upsample and combine, 13 merge foreground CoC,
//   16 visualise focus.
// Uniforms: _CurveParams (x max CoC, y focal size, z aperture, w focal distance 0..1),
// _Offsets, _FgOverlap, _LowRez, _CameraDepthTexture.
// Not verified: the CoC maths follows the 5.x script's parameters; the blur passes, kernels and
// blend curves are rewritten from the description of the effect, not copied. Compare
// 4P_SkyscraperFinale against the Steam game.
Shader "Hidden/Dof/DepthOfFieldHdr" {
	Properties {
		_MainTex ("-", 2D) = "black" {}
		_FgOverlap ("-", 2D) = "black" {}
		_LowRez ("-", 2D) = "black" {}
	}

	CGINCLUDE
	#include "UnityCG.cginc"

	struct v2f {
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
		float2 uv1 : TEXCOORD1;
	};

	float4 _MainTex_TexelSize;
	float4 _CurveParams;
	float4 _Offsets;

	sampler2D _MainTex;
	sampler2D_float _CameraDepthTexture;
	sampler2D _FgOverlap;
	sampler2D _LowRez;

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
		o.uv1 = v.texcoord.xy;
		o.uv = v.texcoord.xy;
		#if UNITY_UV_STARTS_AT_TOP
		if (_MainTex_TexelSize.y < 0)
			o.uv.y = 1 - o.uv.y;
		#endif
		return o;
	}

	half CocFromDepth (float2 uv)
	{
		float d = Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, uv));
		half coc = _CurveParams.z * abs(d - _CurveParams.w) / (d + 1e-5);
		return clamp(max(0.0, coc - _CurveParams.y), 0.0, _CurveParams.x);
	}

	half4 fragPassthrough (v2f i) : SV_Target
	{
		return tex2D(_MainTex, i.uv1);
	}

	// 0: CoC into alpha (ColorMask A)
	half4 fragCaptureCoc (v2f i) : SV_Target
	{
		return half4(0, 0, 0, CocFromDepth(i.uv1));
	}

	// 4: foreground-only CoC
	half4 fragCaptureForegroundCoc (v2f i) : SV_Target
	{
		float d = Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv1));
		half coc = _CurveParams.z * (_CurveParams.w - d) / (d + 1e-5);
		coc = clamp(max(0.0, coc - _CurveParams.y), 0.0, _CurveParams.x);
		return half4(coc, coc, coc, coc);
	}

	// 2: spread the foreground CoC along _Offsets.xy (in texels), keeping the maximum
	half4 fragBlurForFgCoc (v2f i) : SV_Target
	{
		half4 c = tex2D(_MainTex, i.uv1);
		float2 stepUV = _Offsets.xy * _MainTex_TexelSize.xy / 3.0;
		for (int k = 1; k <= 3; k++)
		{
			c = max(c, tex2D(_MainTex, i.uv1 + stepUV * k));
			c = max(c, tex2D(_MainTex, i.uv1 - stepUV * k));
		}
		return c;
	}

	// 13: merge background CoC with the dilated foreground CoC (ColorMask A)
	half4 fragMergeCoc (v2f i) : SV_Target
	{
		half fgCoc = tex2D(_FgOverlap, i.uv).a;
		half coc = CocFromDepth(i.uv1);
		return max(coc, fgCoc).xxxx;
	}

	// 6: half-resolution downsample; colour weighted by CoC, alpha keeps the largest CoC
	half4 fragDownsampleWithCocConserve (v2f i) : SV_Target
	{
		float2 o = _MainTex_TexelSize.xy * 0.5;
		half4 a = tex2D(_MainTex, i.uv1 + float2(-o.x, -o.y));
		half4 b = tex2D(_MainTex, i.uv1 + float2( o.x, -o.y));
		half4 c = tex2D(_MainTex, i.uv1 + float2(-o.x,  o.y));
		half4 d = tex2D(_MainTex, i.uv1 + float2( o.x,  o.y));
		half4 w = half4(a.a, b.a, c.a, d.a) + 1e-3;
		half3 rgb = (a.rgb * w.x + b.rgb * w.y + c.rgb * w.z + d.rgb * w.w) / dot(w, 1.0);
		return half4(rgb, max(max(a.a, b.a), max(c.a, d.a)));
	}

	half BokehWeightDisc (half4 theSample, half sampleDistance, half4 centerSample)
	{
		return smoothstep(-0.265, 0.0, theSample.a - centerSample.a * sampleDistance);
	}

	half4 DiscBlur (float2 uv, bool hq)
	{
		half4 centerTap = tex2D(_MainTex, uv);
		half4 sum = centerTap;
		half2 scale = _MainTex_TexelSize.xy * centerTap.a * _Offsets.w;
		half sampleCount = max(centerTap.a * 0.25, _Offsets.z);
		sum *= sampleCount;

		for (int l = 0; l < NumSmallDiscSamples; l++)
		{
			half4 s = tex2D(_MainTex, uv + SmallDiscKernel[l] * scale);
			half w = s.a > 0.0 ? BokehWeightDisc(s, length(SmallDiscKernel[l]), centerTap) : 0.0;
			sum += s * w;
			sampleCount += w;
		}
		if (hq)
		{
			for (int k = 0; k < NumDiscSamples; k++)
			{
				half4 s = tex2D(_MainTex, uv + DiscKernel[k] * scale);
				half w = s.a > 0.0 ? BokehWeightDisc(s, length(DiscKernel[k]), centerTap) : 0.0;
				sum += s * w;
				sampleCount += w;
			}
		}

		half4 result = sum / sampleCount;
		result.a = centerTap.a;
		return result;
	}

	half4 fragBlurMQ (v2f i) : SV_Target { return DiscBlur(i.uv1, false); }
	half4 fragBlurHQ (v2f i) : SV_Target { return DiscBlur(i.uv1, true); }

	// 12/18: small blur at full resolution, then blend to the half-resolution big blur by CoC
	half4 UpsampleCombine (v2f i, half lowEdge, half highEdge)
	{
		half4 bigBlur = tex2D(_LowRez, i.uv1);
		half4 centerTap = tex2D(_MainTex, i.uv1);
		half4 smallBlur = centerTap;
		half2 scale = _MainTex_TexelSize.xy * centerTap.a * _Offsets.w;
		half sampleCount = max(smallBlur.a * 0.1, 0.0005);
		smallBlur *= sampleCount;

		for (int l = 0; l < NumSmallDiscSamples; l++)
		{
			half2 k = SmallDiscKernel[l] * 1.1;
			half4 s = tex2D(_MainTex, i.uv1 + k * scale);
			half w = BokehWeightDisc(s, length(k), centerTap);
			smallBlur += s * w;
			sampleCount += w;
		}
		smallBlur /= (sampleCount + 1e-5);

		half blend = smoothstep(lowEdge, highEdge, centerTap.a);
		smallBlur = lerp(smallBlur, bigBlur, blend);
		return centerTap.a < 1e-2 ? centerTap : half4(smallBlur.rgb, centerTap.a);
	}

	half4 fragUpsampleMQ (v2f i) : SV_Target { return UpsampleCombine(i, 0.4, 0.6); }
	half4 fragUpsampleHQ (v2f i) : SV_Target { return UpsampleCombine(i, 0.2, 0.5); }

	// 16: focus visualisation (black in focus, white fully blurred)
	half4 fragVisualize (v2f i) : SV_Target
	{
		half4 color = tex2D(_MainTex, i.uv1);
		color.rgb = saturate(color.a / max(_CurveParams.x, 1e-5)).xxx;
		return color;
	}
	ENDCG

	SubShader {
		ZTest Always Cull Off ZWrite Off

		// 0
		Pass {
			ColorMask A
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragCaptureCoc
			ENDCG
		}

		// 1
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragPassthrough
			ENDCG
		}

		// 2
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragBlurForFgCoc
			ENDCG
		}

		// 3
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragPassthrough
			ENDCG
		}

		// 4
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragCaptureForegroundCoc
			ENDCG
		}

		// 5
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragPassthrough
			ENDCG
		}

		// 6
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragDownsampleWithCocConserve
			ENDCG
		}

		// 7
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragPassthrough
			ENDCG
		}

		// 8
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragPassthrough
			ENDCG
		}

		// 9
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragPassthrough
			ENDCG
		}

		// 10
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragPassthrough
			ENDCG
		}

		// 11
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragBlurMQ
			ENDCG
		}

		// 12
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragUpsampleMQ
			ENDCG
		}

		// 13
		Pass {
			ColorMask A
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragMergeCoc
			ENDCG
		}

		// 14
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragPassthrough
			ENDCG
		}

		// 15
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragPassthrough
			ENDCG
		}

		// 16
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragVisualize
			ENDCG
		}

		// 17
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragBlurHQ
			ENDCG
		}

		// 18
		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert
			#pragma fragment fragUpsampleHQ
			ENDCG
		}
	}

	Fallback off
}
