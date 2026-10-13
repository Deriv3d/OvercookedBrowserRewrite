// Test aid: switch camera image effects off from the page URL, to find which
// effect draws something (for example the halo on the menu).
//
//   ?fx=-Bloom,-Antialiasing     keeps those components on Camera.main disabled
//   ?lightdebug=N                light term test in the ForwardAdd passes
//                                (_WebGLPortLightDebug: 1 spot cookie, 2 falloff, 3 N.L)
//   ?pagedebug=1                 recipe-book page side test (the shader work's
//                                _WebGLPortPageDebug: 1 side tint, 2 RT alpha)
//
// Without an fx parameter this does nothing. Names are component type names.

using System;
using System.Collections.Generic;
using UnityEngine;

public static class WebGLPortEffectToggle
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Register()
	{
		List<string> off = new List<string>();
		string url = Application.absoluteURL ?? "";
		int q = url.IndexOf('?');
		if (q < 0)
		{
			return;
		}
		foreach (string pair in url.Substring(q + 1).Split('&'))
		{
			float pageDebug;
			if (pair.StartsWith("pagedebug=", StringComparison.Ordinal)
				&& float.TryParse(pair.Substring(10), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pageDebug))
			{
				Shader.SetGlobalFloat("_WebGLPortPageDebug", pageDebug);
				Debug.Log("[webgl-port] page debug " + pageDebug);
				continue;
			}
			float lightDebug;
			if (pair.StartsWith("lightdebug=", StringComparison.Ordinal)
				&& float.TryParse(pair.Substring(11), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lightDebug))
			{
				Shader.SetGlobalFloat("_WebGLPortLightDebug", lightDebug);
				Debug.Log("[webgl-port] light debug " + lightDebug);
				continue;
			}
			if (!pair.StartsWith("fx=", StringComparison.Ordinal))
			{
				continue;
			}
			foreach (string item in Uri.UnescapeDataString(pair.Substring(3)).Split(','))
			{
				string name = item.Trim();
				if (name.StartsWith("-", StringComparison.Ordinal) && name.Length > 1)
				{
					off.Add(name.Substring(1));
				}
			}
		}
		if (off.Count == 0)
		{
			return;
		}
		Debug.Log("[webgl-port] fx test: keeping off " + string.Join(",", off.ToArray()));
		GameObject host = new GameObject("WebGLPortEffectToggle");
		UnityEngine.Object.DontDestroyOnLoad(host);
		host.AddComponent<Runner>().Off = off;
	}

	private class Runner : MonoBehaviour
	{
		public List<string> Off;
		private readonly HashSet<string> m_logged = new HashSet<string>();

		// The game turns some effects back on (RecipeBook toggles SSAO), so
		// check every frame before the camera renders.
		private void LateUpdate()
		{
			Camera cam = Camera.main;
			if (cam == null)
			{
				return;
			}
			foreach (MonoBehaviour behaviour in cam.GetComponents<MonoBehaviour>())
			{
				if (behaviour != null && behaviour.enabled && Off.Contains(behaviour.GetType().Name))
				{
					behaviour.enabled = false;
					if (m_logged.Add(cam.name + "/" + behaviour.GetType().Name))
						Debug.Log("[webgl-port] fx test: " + behaviour.GetType().Name + " off on " + cam.name);
				}
			}
		}
	}
}
