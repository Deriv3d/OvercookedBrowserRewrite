// UI drawn with the default UI material uses the Unity 5.5 version of
// UI/Default on WebGL.
//
// Since 2020 Unity's UI/Default blends premultiplied (Blend One
// OneMinusSrcAlpha, rgb *= a); 5.5 blended straight alpha (Blend SrcAlpha
// OneMinusSrcAlpha) on colour and alpha alike. On screen the two look the
// same, but the recipe book renders its menus into render textures cleared to
// white with alpha 0, and the page shader blends them by that alpha. With the
// 5.5 blend the half-alpha outlines on the menu words leave a low, white-tinted
// alpha (a thin, light halo, as on Steam); with the new blend they build up to
// a heavy black outline. The shader is port/shaders/added/UI-Default-55.shader.

using UnityEngine;

public static class WebGLPortUIShader
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Apply()
	{
		Shader shader = Resources.Load<Shader>("UI-Default-55");
		Material material = Canvas.GetDefaultCanvasMaterial();
		if (shader == null || !shader.isSupported || material == null)
		{
			Debug.LogWarning("[webgl-port] UI-Default-55 not applied (shader " + (shader == null ? "missing" : "unsupported") + ")");
			return;
		}
		material.shader = shader;
		Debug.Log("[webgl-port] default UI material now uses " + shader.name);
	}
}
