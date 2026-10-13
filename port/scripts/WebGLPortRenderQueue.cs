// Water drawn over the boat on 3-1 and 3-2 (screenshot: the sea
// paints over the deck, barrels, stairs and counters).
//
// Custom/WaveyLiquid draws with ZTest Always and relies on its shader's
// Queue=Background (1000) to be drawn before everything else, so the boat
// covers it. A material that carries its own queue instead (for example 2000,
// the value a material picks up when it is imported against a stand-in
// shader) sorts the water among the opaque boat objects, and whatever was
// drawn before it is painted over.
//
// On each scene load, materials on Custom/WaveyLiquid whose queue differs from
// the shader's go back to the shader's queue (renderQueue = -1). Every other
// material whose queue differs from its shader's is only logged
// ("[webgl-port] diag queue"), because the game does set some queues on
// purpose; those are checked against the export before anything changes.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WebGLPortRenderQueue
{
	private static readonly HashSet<string> s_useShaderQueue = new HashSet<string> { "Custom/WaveyLiquid" };
	private const int MaxLogLines = 60;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Register()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene _scene, LoadSceneMode _mode)
	{
		HashSet<int> seen = new HashSet<int>();
		int fixedCount = 0;
		int lines = 0;
		foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>())
		{
			foreach (Material material in renderer.sharedMaterials)
			{
				if (material == null || material.shader == null || !seen.Add(material.GetInstanceID()))
				{
					continue;
				}
				int own = material.renderQueue;
				int fromShader = material.shader.renderQueue;
				if (own == fromShader)
				{
					continue;
				}
				bool fix = s_useShaderQueue.Contains(material.shader.name);
				if (fix)
				{
					material.renderQueue = -1;
					fixedCount++;
				}
				if (lines < MaxLogLines)
				{
					lines++;
					Debug.Log("[webgl-port] diag queue scene=" + _scene.name + " material=" + material.name + " shader=" + material.shader.name
						+ " queue=" + own + " shaderQueue=" + fromShader + " renderer=" + renderer.name + (fix ? " -> fixed to shader queue" : ""));
				}
			}
		}
		if (fixedCount > 0 || lines > 0)
		{
			Debug.Log("[webgl-port] render queue: scene " + _scene.name + ", " + fixedCount + " water material(s) put back on the shader's queue, " + lines + " mismatch line(s)");
		}
	}
}
