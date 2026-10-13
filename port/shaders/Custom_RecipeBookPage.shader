// Replacement for the game's "Custom/RecipeBookPage" (AssetRipper Free exports every shader as a dummy).
// Matches the Steam build's compiled shader (reimplemented for this port):
// a Lambert surface shader, written out as explicit passes. It replaces the earlier 
// guess (lit from both sides); compare with Steam again.
// _MainTex is the paper. Inside the _UIAreaUV rectangle (xy = min UV, zw = max UV) the front
// shows _FrontPageTex and the back shows _BackPageTex (x mirrored), each over the paper by its
// alpha. Two expressions are written exactly as compiled, though they use the matrices unusually:
//   front/back: dot(tangent-space view dir, object Y axis in world) >= 0 means front
//           [webgl-port] replaced, see the note at isFront below
//   normal: the tangent-space normal is unity_WorldToObject's column 1, taken to world through
//           the TBN rows (not renormalised); it is not the mesh normal.
// Lighting: plain Lambert (no x2); ambient is the standard per-pixel SH (L0L1 per pixel on that
// normal + L2 per vertex on the mesh normal, light probe proxy volumes included), turned to
// gamma per pixel. _Color is declared but reaches nothing visible, as in the original.
// The original's PREPASS and META passes are left out; its DEFERRED pass is kept (the game renders deferred at Dreamy).
// [webgl-port] Add pass uses fwdadd_fullshadows: the game renders this through the deferred path
// (DeferredLighting at Standard, DeferredShading at Dreamy; DeferredLighting at Standard, DeferredShading at Dreamy), where 5.5 lit it
// with every light per pixel, each with its shadows. We run Forward, so the add pass needs shadows too.
Shader "Custom/RecipeBookPage" {
	Properties {
		_Color ("Main Color", Color) = (1,1,1,1)
		_MainTex ("Base (RGB)", 2D) = "white" {}
		_FrontPageTex ("FrontPage (RGB)", 2D) = "white" {}
		_BackPageTex ("BackPage (RGB)", 2D) = "white" {}
		_UIAreaUV ("UI MinMax Area UV", Vector) = (0.3,0.3,0.7,0.7)
	}

	CGINCLUDE
	#include "UnityCG.cginc"
	#include "Lighting.cginc"
	#include "AutoLight.cginc"
	#include "UnityStandardUtils.cginc"
	#include "UnityGBuffer.cginc"

	sampler2D _MainTex;
	float4 _MainTex_ST;
	sampler2D _FrontPageTex;
	sampler2D _BackPageTex;
	float4 _UIAreaUV;
	// [webgl-port] Diagnostic only, off by default (0): WebGLPortEffectToggle sets it globally from a URL flag
	// to see which side the front/back test picks while a page turns. 1 = tint front red, back
	// green; 2 = show the chosen page RT's alpha (white = RT fully replaces paper); 3 = tint by
	// Steam's original side test.
	float _WebGLPortPageDebug;

	struct v2f {
		UNITY_POSITION(pos);
		float2 uv : TEXCOORD0;
		float4 tSpace0 : TEXCOORD1; // TBN rows, w = world position
		float4 tSpace1 : TEXCOORD2;
		float4 tSpace2 : TEXCOORD3;
		half3 sh : TEXCOORD4; // vertex lights + SH L2 (ForwardBase only)
		UNITY_LIGHTING_COORDS(5, 6)
		UNITY_FOG_COORDS(7)
	};

	v2f vert (appdata_full v)
	{
		v2f o;
		UNITY_INITIALIZE_OUTPUT(v2f, o);
		o.pos = UnityObjectToClipPos(v.vertex);
		o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);

		float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
		float3 worldN = UnityObjectToWorldNormal(v.normal);
		float3 worldT = UnityObjectToWorldDir(v.tangent.xyz);
		float3 worldB = cross(worldN, worldT) * v.tangent.w * unity_WorldTransformParams.w;
		o.tSpace0 = float4(worldT.x, worldB.x, worldN.x, worldPos.x);
		o.tSpace1 = float4(worldT.y, worldB.y, worldN.y, worldPos.y);
		o.tSpace2 = float4(worldT.z, worldB.z, worldN.z, worldPos.z);

		#ifdef UNITY_PASS_FORWARDBASE
		// As a surface shader does: vertex lights, then ShadeSHPerVertex (to linear, + L2).
		o.sh = 0;
		#ifdef VERTEXLIGHT_ON
		o.sh += Shade4PointLights(unity_4LightPosX0, unity_4LightPosY0, unity_4LightPosZ0,
			unity_LightColor[0].rgb, unity_LightColor[1].rgb, unity_LightColor[2].rgb, unity_LightColor[3].rgb,
			unity_4LightAtten0, worldPos, worldN);
		#endif
		o.sh = ShadeSHPerVertex(worldN, o.sh);
		#endif

		UNITY_TRANSFER_LIGHTING(o, v.texcoord1.xy);
		UNITY_TRANSFER_FOG(o, o.pos);
		return o;
	}

	// Page colour and lighting normal, shared by the forward and deferred passes. debugOut.a > 0
	// means pagedebug=2 asked for the RT alpha view instead (debugOut.rgb).
	fixed3 pageSurface (v2f i, out float3 Nw, out fixed4 debugOut)
	{
		debugOut = 0;
		float3 worldPos = float3(i.tSpace0.w, i.tSpace1.w, i.tSpace2.w);
		float3 T = float3(i.tSpace0.x, i.tSpace1.x, i.tSpace2.x);
		float3 B = float3(i.tSpace0.y, i.tSpace1.y, i.tSpace2.y);
		float3 Nm = float3(i.tSpace0.z, i.tSpace1.z, i.tSpace2.z);
		float3 Vw = normalize(UnityWorldSpaceViewDir(worldPos));
		float3 Vts = float3(dot(T, Vw), dot(B, Vw), dot(Nm, Vw));

		float2 uvUI = (i.uv - _UIAreaUV.xy) / (_UIAreaUV.zw - _UIAreaUV.xy);
		float2 d = uvUI - 0.5;
		bool inside = d.x * d.x <= 0.25 && d.y * d.y <= 0.25;
		// [webgl-port] Steam's compiled test is dot(Vts, object Y axis in world) >= 0. The page is a
		// SkinnedMeshRenderer, and 2022 gives it the rootBone's matrix (joint1, rotated -90/90), so
		// that test picks the wrong side mid-turn (build 14 pagedebug captures) where Steam shows
		// each face its own side. Use the side of the skinned mesh normal facing the camera
		// (Vts.z = dot(N, V)). This assumes the mesh normal points out of the front face, so the flat
		// right page shows front and the flat left page back, as in Steam; pagedebug=1 shows this
		// test and pagedebug=3 the original one, to check.
		float3 objY = float3(unity_ObjectToWorld[0].y, unity_ObjectToWorld[1].y, unity_ObjectToWorld[2].y);
		bool isFrontSteam = dot(Vts, objY) >= 0.0;
		bool isFront = Vts.z >= 0.0;
		half front = (isFront && inside) ? 1.0 : 0.0;
		half back = (!isFront && inside) ? 1.0 : 0.0;

		fixed3 m = tex2D(_MainTex, i.uv).rgb;
		fixed4 F = tex2D(_FrontPageTex, uvUI);
		fixed4 Bk = tex2D(_BackPageTex, float2(1.0 - uvUI.x, uvUI.y));
		fixed3 c = m * (1.0 - front * F.a) + F.rgb * (front * F.a);
		c = c * (1.0 - back * Bk.a) + Bk.rgb * (back * Bk.a);
		if (_WebGLPortPageDebug > 2.5)
			c *= isFrontSteam ? fixed3(1.0, 0.35, 0.35) : fixed3(0.35, 1.0, 0.35);
		else if (_WebGLPortPageDebug > 1.5)
			debugOut = fixed4((front * F.a + back * Bk.a).xxx, 1.0);
		else if (_WebGLPortPageDebug > 0.5)
			c *= isFront ? fixed3(1.0, 0.35, 0.35) : fixed3(0.35, 1.0, 0.35);

		float3 n = float3(unity_WorldToObject[0].y, unity_WorldToObject[1].y, unity_WorldToObject[2].y);
		Nw = float3(dot(i.tSpace0.xyz, n), dot(i.tSpace1.xyz, n), dot(i.tSpace2.xyz, n));
		return c;
	}

	fixed4 frag (v2f i) : SV_Target
	{
		float3 worldPos = float3(i.tSpace0.w, i.tSpace1.w, i.tSpace2.w);
		#ifndef USING_DIRECTIONAL_LIGHT
		float3 L = normalize(UnityWorldSpaceLightDir(worldPos));
		#else
		float3 L = _WorldSpaceLightPos0.xyz;
		#endif
		float3 Nw;
		fixed4 debugOut;
		fixed3 c = pageSurface(i, Nw, debugOut);
		if (debugOut.a > 0.0)
		{
			#ifdef UNITY_PASS_FORWARDADD
			return 0;
			#else
			return debugOut;
			#endif
		}

		UNITY_LIGHT_ATTENUATION(atten, i, worldPos);

		fixed4 o;
		o.rgb = c * _LightColor0.rgb * (max(0.0, dot(Nw, L)) * atten);
		#ifdef UNITY_PASS_FORWARDBASE
		o.rgb += c * ShadeSHPerPixel(Nw, i.sh, worldPos);
		#endif
		o.a = 1.0;
		UNITY_APPLY_FOG(i.fogCoord, o);
		return o;
	}
	// [webgl-port] Deferred pass. At Dreamy the game renders with DeferredShading, and the original
	// (a Lambert surface shader) had a DEFERRED pass, so it went through the G-buffer like the rest
	// of the scene. Same G-buffer as a 5.x Lambert surface shader: page colour as diffuse, no
	// specular, smoothness 0, the same lighting normal, and colour x SH ambient (as in the forward
	// pass, evaluated per pixel) in the emission buffer.
	void fragDeferred (v2f i,
		out half4 outGBuffer0 : SV_Target0,
		out half4 outGBuffer1 : SV_Target1,
		out half4 outGBuffer2 : SV_Target2,
		out half4 outEmission : SV_Target3
	#if defined(SHADOWS_SHADOWMASK) && (UNITY_ALLOWED_MRT_COUNT > 4)
		, out half4 outShadowMask : SV_Target4
	#endif
		)
	{
		float3 worldPos = float3(i.tSpace0.w, i.tSpace1.w, i.tSpace2.w);
		float3 Nm = float3(i.tSpace0.z, i.tSpace1.z, i.tSpace2.z);
		float3 Nw;
		fixed4 debugOut;
		fixed3 c = pageSurface(i, Nw, debugOut);
		half3 ambient = c * ShadeSHPerPixel(Nw, ShadeSHPerVertex(normalize(Nm), half3(0, 0, 0)), worldPos);
		if (debugOut.a > 0.0)
		{
			c = 0;
			ambient = debugOut.rgb;
		}

		UnityStandardData data;
		data.diffuseColor = c;
		data.occlusion = 1.0;
		data.specularColor = half3(0, 0, 0);
		data.smoothness = 0.0;
		data.normalWorld = normalize(Nw);
		UnityStandardDataToGbuffer(data, outGBuffer0, outGBuffer1, outGBuffer2);

		half4 emission = half4(ambient, 1.0);
		#ifndef UNITY_HDR_ON
		emission.rgb = exp2(-emission.rgb);
		#endif
		outEmission = emission;
	#if defined(SHADOWS_SHADOWMASK) && (UNITY_ALLOWED_MRT_COUNT > 4)
		outShadowMask = 1.0;
	#endif
	}
	ENDCG

	SubShader {
		Tags { "RenderType"="Diffuse" }
		LOD 0

		Pass {
			Name "FORWARD"
			Tags { "LightMode"="ForwardBase" "SHADOWSUPPORT"="true" }
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile_fwdbase
			#pragma multi_compile_fog
			ENDCG
		}

		Pass {
			Name "FORWARD"
			Tags { "LightMode"="ForwardAdd" }
			ZWrite Off
			Blend One One
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 3.0
			#pragma multi_compile_fwdadd_fullshadows
			#pragma multi_compile_fog
			ENDCG
		}
		Pass {
			Name "DEFERRED"
			Tags { "LightMode"="Deferred" }
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment fragDeferred
			#pragma target 3.0
			#pragma exclude_renderers nomrt
			#pragma multi_compile_prepassfinal
			ENDCG
		}
	}

	Fallback "Diffuse"
}
