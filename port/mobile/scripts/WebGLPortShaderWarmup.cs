// Shader warm-up for the optimisation framework (port/mobile/README.md).
// Safari turns each WebGL shader variant into Metal the first time it is
// drawn, which can freeze a frame mid-level. Compiling the variants a scene
// uses while it loads moves that cost to the loading screen. Nothing on
// screen changes.
//
// Collections are recorded in the editor (Project Settings >
// Graphics > "Save to asset" after playing a level) and saved as
//   Assets/WebGLPort/Resources/WebGLPortWarmup/<scene name>.shadervariants
//   Assets/WebGLPort/Resources/WebGLPortWarmup/_shared.shadervariants
// (lab files, not in the repo). A scene without one is skipped; ?warmup=0
// skips all of them, to measure what warm-up saves.

using UnityEngine;
using UnityEngine.SceneManagement;

public static class WebGLPortShaderWarmup
{
	private const string Folder = "WebGLPortWarmup/";
	private static bool s_registered;
	private static bool s_sharedDone;

	public static void Register()
	{
		if (s_registered)
		{
			return;
		}
		s_registered = true;
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (!WebGLPortPhone.WarmupEnabled)
		{
			return;
		}
		if (!s_sharedDone)
		{
			s_sharedDone = true;
			WarmUp("_shared");
		}
		WarmUp(scene.name);
	}

	private static void WarmUp(string name)
	{
		ShaderVariantCollection collection = Resources.Load<ShaderVariantCollection>(Folder + name);
		if (collection == null)
		{
			return;
		}
		float start = Time.realtimeSinceStartup;
		collection.WarmUp();
		Debug.Log("[webgl-port] warmup " + name + " variants=" + collection.variantCount
			+ " ms=" + ((Time.realtimeSinceStartup - start) * 1000f).ToString("F0"));
		Resources.UnloadAsset(collection);
	}
}
