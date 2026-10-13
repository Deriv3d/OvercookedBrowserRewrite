// Replacement for the game's "Hidden/SSAO" (AssetRipper Free exports every shader as a dummy).
// Unity Standard Assets 5.x ScreenSpaceAmbientOcclusion shader, rewritten for Unity 2022.3 /
// WebGL 2. Pass order matches the script: 0/1/2 = occlusion with 8/14/26 samples (Low/Medium/
// High), 3 = depth-aware blur, 4 = composite. Uniforms: _Params, _NoiseScale, _FarCorner,
// _TexelOffsetScale, _SSAO, _RandomTexture.
// Not verified: the sample kernels are freshly generated random points in the unit sphere, not the
// original constants; they play the same role, so the look should match closely.
Shader "Hidden/SSAO" {
	Properties {
		_MainTex ("", 2D) = "" {}
		_RandomTexture ("", 2D) = "" {}
		_SSAO ("", 2D) = "" {}
	}

	SubShader {
		ZTest Always Cull Off ZWrite Off

	CGINCLUDE
	#include "UnityCG.cginc"

	struct v2f_ao {
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
		float2 uvr : TEXCOORD1;
	};

	uniform float2 _NoiseScale;
	sampler2D _CameraDepthNormalsTexture;
	sampler2D _RandomTexture;
	float4 _Params; // x = radius, y = min z, z = attenuation power, w = SSAO power

	v2f_ao vert_ao (appdata_img v)
	{
		v2f_ao o;
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = v.texcoord.xy; // not TRANSFORM_TEX: nothing sets _CameraDepthNormalsTexture_ST
		o.uvr = v.texcoord.xy * _NoiseScale;
		return o;
	}

	static const float3 RAND_SAMPLES_8[8] =
	{
		float3(0.245803, 0.483574, 0.590387),
		float3(-0.061862, -0.506854, 0.087522),
		float3(0.922956, 0.078447, 0.355661),
		float3(-0.869721, -0.397282, 0.206220),
		float3(-0.380084, 0.637036, -0.038510),
		float3(-0.368414, -0.037563, 0.409338),
		float3(0.575477, -0.267631, 0.157038),
		float3(-0.290414, 0.049404, 0.551206)
	};

	static const float3 RAND_SAMPLES_14[14] =
	{
		float3(-0.817640, -0.318519, 0.221655),
		float3(0.090288, -0.375099, -0.366400),
		float3(-0.188224, -0.525327, 0.187920),
		float3(0.652591, -0.088670, -0.156485),
		float3(0.576019, -0.786739, -0.130890),
		float3(0.952015, -0.092920, -0.023682),
		float3(0.459010, -0.041915, -0.417955),
		float3(-0.192421, -0.706987, -0.245999),
		float3(-0.001355, -0.323042, -0.821725),
		float3(-0.277348, 0.572045, 0.549796),
		float3(0.389193, 0.328037, 0.519277),
		float3(-0.273136, 0.408939, -0.438296),
		float3(-0.028630, 0.539495, 0.381766),
		float3(0.161322, -0.976839, 0.093982)
	};

	static const float3 RAND_SAMPLES_26[26] =
	{
		float3(-0.498615, 0.343286, -0.074122),
		float3(0.633359, 0.294874, 0.595252),
		float3(-0.304231, 0.288126, 0.475655),
		float3(0.656377, -0.299910, 0.685759),
		float3(0.913033, 0.036279, 0.058680),
		float3(-0.045522, 0.382851, 0.439373),
		float3(0.460707, -0.656346, 0.560743),
		float3(0.161691, 0.331112, -0.158416),
		float3(0.247474, 0.549409, 0.273733),
		float3(-0.117863, 0.300234, -0.561924),
		float3(-0.239402, 0.223588, 0.180329),
		float3(-0.189249, -0.442944, -0.179915),
		float3(0.090442, -0.321253, 0.161792),
		float3(0.625980, 0.284593, -0.261115),
		float3(-0.715775, 0.191875, 0.127718),
		float3(-0.784163, 0.131624, 0.230336),
		float3(-0.248303, -0.136632, -0.547328),
		float3(-0.016491, -0.427203, -0.046129),
		float3(-0.756228, 0.243373, -0.113065),
		float3(-0.413794, 0.563413, 0.653611),
		float3(-0.413623, 0.216075, -0.050737),
		float3(0.289753, 0.207707, 0.486946),
		float3(0.066996, -0.327757, -0.406356),
		float3(0.059735, -0.071327, -0.277921),
		float3(-0.495154, -0.088773, 0.833146),
		float3(0.556765, -0.144537, 0.151284)
	};

	half frag_ao8 (v2f_ao i) : SV_Target
	{
		// random normal from the noise texture
		half3 randN = tex2D(_RandomTexture, i.uvr).xyz * 2.0 - 1.0;
		// scene depth/normal
		float4 depthnormal = tex2D(_CameraDepthNormalsTexture, i.uv);
		float3 viewNorm;
		float depth;
		DecodeDepthNormal(depthnormal, depth, viewNorm);
		depth *= _ProjectionParams.z;
		float scale = _Params.x / depth;

		float occ = 0.0;
		for (int s = 0; s < 8; ++s)
		{
			// reflect sample direction around a random vector, into the upper hemisphere
			half3 randomDir = reflect(RAND_SAMPLES_8[s], randN);
			half flip = (dot(viewNorm, randomDir) < 0) ? 1.0 : -1.0;
			randomDir *= -flip;
			// a bit of normal to reduce self-shadowing
			randomDir += viewNorm * 0.3;

			float2 offset = randomDir.xy * scale;
			float sD = depth - (randomDir.z * _Params.x);

			float4 sampleND = tex2D(_CameraDepthNormalsTexture, i.uv + offset);
			float sampleD;
			float3 sampleN;
			DecodeDepthNormal(sampleND, sampleD, sampleN);
			sampleD *= _ProjectionParams.z;
			float zd = saturate(sD - sampleD);
			if (zd > _Params.y)
				occ += pow(1 - zd, _Params.z);
		}
		occ /= 8;
		return 1 - occ;
	}

	half frag_ao14 (v2f_ao i) : SV_Target
	{
		// random normal from the noise texture
		half3 randN = tex2D(_RandomTexture, i.uvr).xyz * 2.0 - 1.0;
		// scene depth/normal
		float4 depthnormal = tex2D(_CameraDepthNormalsTexture, i.uv);
		float3 viewNorm;
		float depth;
		DecodeDepthNormal(depthnormal, depth, viewNorm);
		depth *= _ProjectionParams.z;
		float scale = _Params.x / depth;

		float occ = 0.0;
		for (int s = 0; s < 14; ++s)
		{
			// reflect sample direction around a random vector, into the upper hemisphere
			half3 randomDir = reflect(RAND_SAMPLES_14[s], randN);
			half flip = (dot(viewNorm, randomDir) < 0) ? 1.0 : -1.0;
			randomDir *= -flip;
			// a bit of normal to reduce self-shadowing
			randomDir += viewNorm * 0.3;

			float2 offset = randomDir.xy * scale;
			float sD = depth - (randomDir.z * _Params.x);

			float4 sampleND = tex2D(_CameraDepthNormalsTexture, i.uv + offset);
			float sampleD;
			float3 sampleN;
			DecodeDepthNormal(sampleND, sampleD, sampleN);
			sampleD *= _ProjectionParams.z;
			float zd = saturate(sD - sampleD);
			if (zd > _Params.y)
				occ += pow(1 - zd, _Params.z);
		}
		occ /= 14;
		return 1 - occ;
	}

	half frag_ao26 (v2f_ao i) : SV_Target
	{
		// random normal from the noise texture
		half3 randN = tex2D(_RandomTexture, i.uvr).xyz * 2.0 - 1.0;
		// scene depth/normal
		float4 depthnormal = tex2D(_CameraDepthNormalsTexture, i.uv);
		float3 viewNorm;
		float depth;
		DecodeDepthNormal(depthnormal, depth, viewNorm);
		depth *= _ProjectionParams.z;
		float scale = _Params.x / depth;

		float occ = 0.0;
		for (int s = 0; s < 26; ++s)
		{
			// reflect sample direction around a random vector, into the upper hemisphere
			half3 randomDir = reflect(RAND_SAMPLES_26[s], randN);
			half flip = (dot(viewNorm, randomDir) < 0) ? 1.0 : -1.0;
			randomDir *= -flip;
			// a bit of normal to reduce self-shadowing
			randomDir += viewNorm * 0.3;

			float2 offset = randomDir.xy * scale;
			float sD = depth - (randomDir.z * _Params.x);

			float4 sampleND = tex2D(_CameraDepthNormalsTexture, i.uv + offset);
			float sampleD;
			float3 sampleN;
			DecodeDepthNormal(sampleND, sampleD, sampleN);
			sampleD *= _ProjectionParams.z;
			float zd = saturate(sD - sampleD);
			if (zd > _Params.y)
				occ += pow(1 - zd, _Params.z);
		}
		occ /= 26;
		return 1 - occ;
	}

	// Depth-aware blur
	sampler2D _SSAO;
	float3 _TexelOffsetScale;

	struct v2f {
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
	};

	v2f vert_blur (appdata_img v)
	{
		v2f o;
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = v.texcoord.xy; // not TRANSFORM_TEX: nothing sets _CameraDepthNormalsTexture_ST
		return o;
	}

	inline half CheckSame (half4 n, half4 nn)
	{
		// difference in normals
		half2 diff = abs(n.xy - nn.xy);
		half sn = (diff.x + diff.y) < 0.1;
		// difference in depth
		float z = DecodeFloatRG(n.zw);
		float zz = DecodeFloatRG(nn.zw);
		float zdiff = abs(z - zz) * _ProjectionParams.z;
		half sz = zdiff < 0.2;
		return sn * sz;
	}

	#define NUM_BLUR_SAMPLES 4

	half4 frag_blur (v2f i) : SV_Target
	{
		float2 o = _TexelOffsetScale.xy;
		half sum = tex2D(_SSAO, i.uv).r * (NUM_BLUR_SAMPLES + 1);
		half denom = NUM_BLUR_SAMPLES + 1;
		half4 geom = tex2D(_CameraDepthNormalsTexture, i.uv);

		for (int s = 0; s < NUM_BLUR_SAMPLES; ++s)
		{
			float2 nuv = i.uv + o * (s + 1);
			half4 ngeom = tex2D(_CameraDepthNormalsTexture, nuv);
			half coef = (NUM_BLUR_SAMPLES - s) * CheckSame(geom, ngeom);
			sum += tex2D(_SSAO, nuv).r * coef;
			denom += coef;
		}
		for (int t = 0; t < NUM_BLUR_SAMPLES; ++t)
		{
			float2 nuv = i.uv - o * (t + 1);
			half4 ngeom = tex2D(_CameraDepthNormalsTexture, nuv);
			half coef = (NUM_BLUR_SAMPLES - t) * CheckSame(geom, ngeom);
			sum += tex2D(_SSAO, nuv).r * coef;
			denom += coef;
		}
		return sum / denom;
	}

	// Composite: scene colour times AO
	sampler2D _MainTex;
	float4 _MainTex_TexelSize;

	struct v2f_comp {
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
		float2 uvAO : TEXCOORD1;
	};

	v2f_comp vert_comp (appdata_img v)
	{
		v2f_comp o;
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = v.texcoord.xy;
		o.uvAO = v.texcoord.xy;
		#if UNITY_UV_STARTS_AT_TOP
		if (_MainTex_TexelSize.y < 0)
			o.uvAO.y = 1.0 - o.uvAO.y;
		#endif
		return o;
	}

	half4 frag_comp (v2f_comp i) : SV_Target
	{
		half4 c = tex2D(_MainTex, i.uv);
		half ao = tex2D(_SSAO, i.uvAO).r;
		ao = pow(ao, _Params.w);
		c.rgb *= ao;
		return c;
	}
	ENDCG

		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert_ao
			#pragma fragment frag_ao8
			ENDCG
		}

		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert_ao
			#pragma fragment frag_ao14
			ENDCG
		}

		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert_ao
			#pragma fragment frag_ao26
			ENDCG
		}

		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert_blur
			#pragma fragment frag_blur
			ENDCG
		}

		Pass {
			CGPROGRAM
			#pragma target 3.0
			#pragma vertex vert_comp
			#pragma fragment frag_comp
			ENDCG
		}
	}

	Fallback off
}
