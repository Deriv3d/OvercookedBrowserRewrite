// Replacement for the game's "Custom/TransitionTexture" (AssetRipper Free exports every shader as a dummy).
// Matches the Steam build's compiled shader (reimplemented for this port):
// a Standard (metallic) surface shader, Metallic 0 and Smoothness 0 as constants, albedo blended
// from _Texture1 * _Color to _Texture2 * _Color by Prop ("Prop" really has no leading
// underscore), each texture with its own tiling. 2022's built-in Standard BRDF and SH/probe
// code follow the same terms as the 5.5 original (BRDF1 needs the WebGL graphics tier's
// Standard Shader Quality on High).
// [webgl-port] fullforwardshadows: the game renders this through the deferred path
// (DeferredLighting at Standard, DeferredShading at Dreamy; DeferredLighting at Standard, DeferredShading at Dreamy), where 5.5 lit it
// with every light per pixel, each with its shadows. We run Forward, so the add pass needs shadows too.
Shader "Custom/TransitionTexture" {
	Properties {
		_Texture1 ("Texture 1", 2D) = "white" {}
		_Texture2 ("Texture 2", 2D) = "white" {}
		Prop ("Prop", Range(0,1)) = 0
		_Color ("Color", Color) = (1,1,1,1)
	}

	SubShader {
		Tags { "RenderType"="Opaque" }
		LOD 200

		CGPROGRAM
		#pragma surface surf Standard fullforwardshadows
		#pragma target 3.0

		sampler2D _Texture1;
		sampler2D _Texture2;
		half Prop;
		fixed4 _Color;

		struct Input {
			float2 uv_Texture1;
			float2 uv_Texture2;
		};

		void surf (Input IN, inout SurfaceOutputStandard o)
		{
			fixed4 c = lerp(tex2D(_Texture1, IN.uv_Texture1) * _Color, tex2D(_Texture2, IN.uv_Texture2) * _Color, Prop);
			o.Albedo = c.rgb;
			o.Metallic = 0.0;
			o.Smoothness = 0.0;
			o.Alpha = c.a;
		}
		ENDCG
	}

	Fallback "Diffuse"
}
