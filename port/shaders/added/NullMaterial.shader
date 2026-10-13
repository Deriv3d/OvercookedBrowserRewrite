// Draws nothing. WebGLPortNullMaterials puts it in material slots that are null in the game's own
// data (e.g. RecipeBook_Front slot 1): Unity 5.x players skipped such submeshes, while
// 2022 draws them with the magenta error shader.
// No ShadowCaster pass and no Fallback, so it casts no shadow and stays out of the depth texture;
// the RenderType matches no replacement shader, so depth-normals (SSAO) skip it too.
Shader "WebGLPort/NullMaterial"
{
	SubShader
	{
		Tags { "Queue"="Geometry" "IgnoreProjector"="True" "RenderType"="WebGLPortNull" }

		Pass
		{
			ColorMask 0
			ZWrite Off
			ZTest Always
			Cull Off

			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			float4 vert (float4 vertex : POSITION) : SV_POSITION
			{
				return UnityObjectToClipPos(vertex);
			}

			fixed4 frag () : SV_Target
			{
				discard;
				return 0;
			}
			ENDCG
		}
	}
}
