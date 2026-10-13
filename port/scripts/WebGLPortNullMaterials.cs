// Empty material slots draw nothing, as in the 5.5 player.
//
// RecipeBook_Front on the menu has a material slot that is empty in the
// game's own data. Steam draws nothing for it; 2022 draws the slot
// with the magenta error shader, a sliver at the book's left edge.
// On scene load, empty slots get a material whose shader draws nothing.

using UnityEngine;
using UnityEngine.SceneManagement;

public static class WebGLPortNullMaterials
{
	private static Material s_nothing;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Register()
	{
		Shader shader = Resources.Load<Shader>("NullMaterial");
		if (shader == null)
		{
			Debug.LogWarning("[webgl-port] NullMaterial shader missing; empty material slots stay magenta");
			return;
		}
		s_nothing = new Material(shader);
		s_nothing.name = "WebGLPort empty slot";
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		foreach (GameObject root in scene.GetRootGameObjects())
		{
			foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
			{
				Material[] materials = renderer.sharedMaterials;
				bool changed = false;
				for (int i = 0; i < materials.Length; i++)
				{
					if (materials[i] == null)
					{
						materials[i] = s_nothing;
						changed = true;
						Debug.Log("[webgl-port] empty material slot " + i + " on " + renderer.name + " draws nothing");
					}
				}
				if (changed)
				{
					renderer.sharedMaterials = materials;
				}
			}
		}
	}
}
