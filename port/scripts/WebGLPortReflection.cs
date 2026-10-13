// The scene's reflection and ambient settings, for 4-2's missing moonlight
// (4-2 should look like the wiki picture; Standard
// stone at smoothness ~0.32 takes a faint blue sheen from the skybox's
// reflection cubemap, unity_SpecCube0). In 5.5 that cubemap lived in the
// scene's lighting data, which 2022 rejects, so it may be black.
//
// Every scene load logs RenderSettings ("[webgl-port] diag env"). Test switch:
//   ?envrefl=1  reflection source = Skybox and DynamicGI.UpdateEnvironment(),
//               so the cubemap is rebuilt from the scene's skybox at load
// Inactive unless in the page address.
// Builds 41-46 never printed the line from a sceneLoaded handler (one of the
// many sceneLoaded subscribers may throw first and cut the list short), so a
// DontDestroyOnLoad poller now notices each loaded scene instead (Scene.GetHashCode
// is its handle, so a reload counts as new).

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class WebGLPortReflection
{
	private static bool s_rebuild;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		s_rebuild = WebGLPortDevFlags.Query(Application.absoluteURL, "envrefl") == "1";
		GameObject host = new GameObject("WebGLPortReflectionPoller");
		Object.DontDestroyOnLoad(host);
		host.AddComponent<Poller>();
	}

	private class Poller : MonoBehaviour
	{
		private readonly System.Collections.Generic.HashSet<int> m_seen = new System.Collections.Generic.HashSet<int>();

		private void Update()
		{
			for (int i = 0; i < SceneManager.sceneCount; i++)
			{
				Scene scene = SceneManager.GetSceneAt(i);
				if (scene.isLoaded && m_seen.Add(scene.GetHashCode()))
				{
					OnSceneLoaded(scene, i == 0 ? LoadSceneMode.Single : LoadSceneMode.Additive);
				}
			}
		}
	}

	// Reading RenderSettings.customReflection throws (ArgumentException "not
	// referencing a cubemap") when the scene's custom reflection is unset, which
	// cut this log line short in every scene.
	private static string CustomReflectionName()
	{
		try
		{
			return RenderSettings.customReflection != null ? RenderSettings.customReflection.name : "none";
		}
		catch (System.ArgumentException)
		{
			return "none";
		}
	}

	private static void OnSceneLoaded(Scene _scene, LoadSceneMode _mode)
	{
		Debug.Log("[webgl-port] diag env scene=" + _scene.name + " mode=" + _mode + " active=" + SceneManager.GetActiveScene().name + " reflectionMode=" + RenderSettings.defaultReflectionMode
			+ " customReflection=" + CustomReflectionName()
			+ " reflectionIntensity=" + RenderSettings.reflectionIntensity.ToString("F2") + " reflectionBounces=" + RenderSettings.reflectionBounces
			+ " skybox=" + (RenderSettings.skybox != null ? RenderSettings.skybox.name + "/" + RenderSettings.skybox.shader.name : "none")
			+ " ambientMode=" + RenderSettings.ambientMode + " ambientLight=" + RenderSettings.ambientLight + " ambientSky=" + RenderSettings.ambientSkyColor
			+ " ambientIntensity=" + RenderSettings.ambientIntensity.ToString("F2") + " fog=" + RenderSettings.fog + " fogColor=" + RenderSettings.fogColor
			+ " probes=" + Object.FindObjectsOfType<ReflectionProbe>().Length + (s_rebuild ? " -> ?envrefl=1: Skybox + UpdateEnvironment" : ""));
		if (s_rebuild)
		{
			RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
			DynamicGI.UpdateEnvironment();
		}
	}
}
