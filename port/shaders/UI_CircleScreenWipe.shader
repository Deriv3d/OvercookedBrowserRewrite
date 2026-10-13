// Replacement for the game's "UI/CircleScreenWipe" (AssetRipper Free exports every shader as a dummy).
// Matches the Steam build's compiled shader (reimplemented for this port):
// UI/Default's state and properties, with a fragment that cuts a hole in the middle:
//   d = (uv - 0.5) with y scaled by screen height / width
//   discard where dot(d, d) < (1 - _Prop) * 0.5   (squared distance against a linear threshold)
//   rgb = _MainTex.rgb * vertex colour * _Color;  a = 1 (texture and vertex alpha ignored)
// No clip rect or alpha clip, as in the original.
Shader "UI/CircleScreenWipe"
{
	Properties
	{
		[PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
		_Color ("Tint", Color) = (1,1,1,1)

		_StencilComp ("Stencil Comparison", Float) = 8
		_Stencil ("Stencil ID", Float) = 0
		_StencilOp ("Stencil Operation", Float) = 0
		_StencilWriteMask ("Stencil Write Mask", Float) = 255
		_StencilReadMask ("Stencil Read Mask", Float) = 255

		_ColorMask ("Color Mask", Float) = 15

		_Prop ("Prop", Range(0,1)) = 0
	}

	SubShader
	{
		Tags
		{
			"Queue"="Transparent"
			"IgnoreProjector"="True"
			"RenderType"="Transparent"
			"PreviewType"="Plane"
			"CanUseSpriteAtlas"="True"
		}

		Stencil
		{
			Ref [_Stencil]
			Comp [_StencilComp]
			Pass [_StencilOp]
			ReadMask [_StencilReadMask]
			WriteMask [_StencilWriteMask]
		}

		Cull Off
		Lighting Off
		ZWrite Off
		ZTest [unity_GUIZTestMode]
		Blend SrcAlpha OneMinusSrcAlpha
		ColorMask [_ColorMask]

		Pass
		{
			Name "Default"
		CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#pragma target 2.0

			#include "UnityCG.cginc"

			struct appdata_t
			{
				float4 vertex   : POSITION;
				float4 color    : COLOR;
				float2 texcoord : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex   : SV_POSITION;
				fixed4 color    : COLOR;
				float2 texcoord : TEXCOORD0;
			};

			sampler2D _MainTex;
			fixed4 _Color;
			float _Prop;

			v2f vert(appdata_t v)
			{
				v2f OUT;
				OUT.vertex = UnityObjectToClipPos(v.vertex);
				OUT.texcoord = v.texcoord;
				OUT.color = v.color * _Color;
				return OUT;
			}

			fixed4 frag(v2f IN) : SV_Target
			{
				float aspect = _ScreenParams.y / _ScreenParams.x;
				float2 d = float2(IN.texcoord.x - 0.5, (IN.texcoord.y - 0.5) * aspect);
				if (dot(d, d) < (1.0 - _Prop) * 0.5)
					discard;
				fixed4 c;
				c.rgb = tex2D(_MainTex, IN.texcoord).rgb * IN.color.rgb;
				c.a = 1.0;
				return c;
			}
		ENDCG
		}
	}
}
