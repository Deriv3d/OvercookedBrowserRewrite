// Lights the scene data marks as baked, in scenes that have no lightmaps.
//
// Build11 diag: every light in every scene reports bakingOutput Baked /
// isBaked=True at runtime, including lights that are Realtime in the scene
// YAML. The exported LightingData lists the lights, and 2022 takes that
// as "already in the lightmaps", so it skips their direct light and shadows.
// The game ships no lightmaps, so those lights drew nothing:
// no menu sun or blind shadows, no rooftop key lights.
// In 5.5, Realtime lights and Mixed lights without lightmaps lit at runtime;
// marking them unbaked restores that.

using UnityEngine;
using UnityEngine.SceneManagement;

public static class WebGLPortRealtimeLights
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Register()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
#if UNITY_2017_3_OR_NEWER
		if (LightmapSettings.lightmaps.Length > 0)
		{
			return;
		}
		int changed = 0;
		foreach (Light light in Object.FindObjectsOfType<Light>(true))
		{
			if (light.gameObject.scene != scene || !light.bakingOutput.isBaked)
			{
				continue;
			}
			LightBakingOutput output = new LightBakingOutput();
			output.isBaked = false;
			output.lightmapBakeType = LightmapBakeType.Realtime;
			output.mixedLightingMode = MixedLightingMode.IndirectOnly;
			output.occlusionMaskChannel = -1;
			output.probeOcclusionLightIndex = -1;
			light.bakingOutput = output;
			changed++;
		}
		Debug.Log("[webgl-port] realtime lights scene=" + scene.name + " unbaked=" + changed);
#endif
	}
}
