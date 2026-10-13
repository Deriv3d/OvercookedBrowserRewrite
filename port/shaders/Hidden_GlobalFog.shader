// Replacement for the game's "Hidden/GlobalFog" (AssetRipper Free exports every shader as a dummy).
// Unity Standard Assets 5.x global fog (distance and/or height fog from the depth texture),
// rewritten for Unity 2022.3 / WebGL 2. Pass order matches the script: 0 = distance + height,
// 1 = distance only, 2 = height only.
// The 5.x script draws its own quad (CustomGraphicsBlit) with the frustum-corner index in each
// vertex's z, which picks a row of _FrustumCornersWS; this vertex function expects that.
// Check: if the game's GlobalFog.cs uses Graphics.Blit instead, the ray must come from the UVs.
Shader "Hidden/GlobalFog" {
	Properties {
		_MainTex ("Base (RGB)", 2D) = "black" {}
	}

	CGINCLUDE
	#include "UnityCG.cginc"

	uniform sampler2D _MainTex;
	uniform sampler2D_float _CameraDepthTexture;

	// x = fog height, y = FdotC (camera y - fog height), z = k (FdotC <= 0), w = density / 2
	uniform float4 _HeightParams;
	// x = -start distance, y = 1 to leave far pixels (skybox) unfogged, 2 to fog them
	uniform float4 _DistanceParams;

	int4 _SceneFogMode; // x = fog mode, y = use radial distance
	float4 _SceneFogParams;

	uniform float4 _MainTex_TexelSize;

	// for fast world-space reconstruction
	uniform float4x4 _FrustumCornersWS;
	uniform float4 _CameraWS;

	struct v2f {
		float4 pos : SV_POSITION;
		float2 uv : TEXCOORD0;
		float2 uv_depth : TEXCOORD1;
		float4 interpolatedRay : TEXCOORD2;
	};

	v2f vert (appdata_img v)
	{
		v2f o;
		half index = v.vertex.z;
		v.vertex.z = 0.1;
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = v.texcoord.xy;
		o.uv_depth = v.texcoord.xy;

		#if UNITY_UV_STARTS_AT_TOP
		if (_MainTex_TexelSize.y < 0)
			o.uv.y = 1 - o.uv.y;
		#endif

		o.interpolatedRay = _FrustumCornersWS[(int)index];
		o.interpolatedRay.w = index;
		return o;
	}

	// One of the standard fog formulas, given the fog coordinate (distance).
	half ComputeFogFactor (float coord)
	{
		float fogFac = 0.0;
		if (_SceneFogMode.x == 1) // linear: (end - z) / (end - start)
		{
			fogFac = coord * _SceneFogParams.z + _SceneFogParams.w;
		}
		if (_SceneFogMode.x == 2) // exp: exp(-density * z)
		{
			fogFac = _SceneFogParams.y * coord;
			fogFac = exp2(-fogFac);
		}
		if (_SceneFogMode.x == 3) // exp2: exp(-(density * z)^2)
		{
			fogFac = _SceneFogParams.x * coord;
			fogFac = exp2(-fogFac * fogFac);
		}
		return saturate(fogFac);
	}

	// Distance-based fog
	float ComputeDistance (float3 camDir, float zdepth)
	{
		float dist;
		if (_SceneFogMode.y == 1)
			dist = length(camDir);
		else
			dist = zdepth * _ProjectionParams.z;
		// Built-in fog starts at the near plane, so match that.
		dist -= _ProjectionParams.y;
		return dist;
	}

	// Linear half-space fog (Lengyel, "Unified Distance Formulas for Halfspace Fog")
	float ComputeHalfSpace (float3 wsDir)
	{
		float3 wpos = _CameraWS.xyz + wsDir;
		float FH = _HeightParams.x;
		float3 P = wpos;
		float3 aV = _HeightParams.w * wsDir;
		float FdotC = _HeightParams.y;
		float k = _HeightParams.z;
		float FdotP = P.y - FH;
		float FdotV = wsDir.y;
		float c1 = k * (FdotP + FdotC);
		float c2 = (1 - 2 * k) * FdotP;
		float g = min(c2, 0.0);
		g = -length(aV) * (c1 - g * g / abs(FdotV + 1.0e-5));
		return g;
	}

	half4 ComputeFog (v2f i, bool distance, bool height)
	{
		half4 sceneColor = tex2D(_MainTex, i.uv);

		// Reconstruct the world-space direction towards this pixel.
		float rawDepth = SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uv_depth);
		float dpth = Linear01Depth(rawDepth);
		float4 wsDir = dpth * i.interpolatedRay;

		float g = _DistanceParams.x;
		if (distance)
			g += ComputeDistance(wsDir.xyz, dpth);
		if (height)
			g += ComputeHalfSpace(wsDir.xyz);

		half fogFac = ComputeFogFactor(max(0.0, g));
		// Do not fog the skybox
		if (dpth == _DistanceParams.y)
			fogFac = 1.0;

		return lerp(unity_FogColor, sceneColor, fogFac);
	}
	ENDCG

	SubShader {
		ZTest Always Cull Off ZWrite Off Fog { Mode Off }

		// 0: distance + height
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			half4 frag (v2f i) : SV_Target { return ComputeFog(i, true, true); }
			ENDCG
		}

		// 1: distance only
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			half4 frag (v2f i) : SV_Target { return ComputeFog(i, true, false); }
			ENDCG
		}

		// 2: height only
		Pass {
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			half4 frag (v2f i) : SV_Target { return ComputeFog(i, false, true); }
			ENDCG
		}
	}

	Fallback off
}
