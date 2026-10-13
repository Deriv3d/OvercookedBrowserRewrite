// Test switches for finding what costs frame time on a phone
// (port/mobile/README.md, "Finding the slow part"). Each one is off unless it
// is in the page address, and each one changes the look, so they are for
// measuring only, never defaults.
//
//   ?pixellights=N   at most N per-pixel lights (the port lifts the limit to
//                    64 while the game asks for deferred lighting; in Forward
//                    every extra light redraws the objects it touches)
//   ?shadows=0       no realtime shadows
//
// Camera effects already have ?fx=-SSAO,-Bloom,... (WebGLPortEffectToggle)
// and render density ?dpr= (phone page).
//
// Applied one frame after each scene load, after the port's own sceneLoaded
// handlers (WebGLPortDeferredLighting sets the light limit there).

using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.SceneManagement;

public class WebGLPortPhoneExperiments : MonoBehaviour
{
	private int _pixelLights = -1;
	private bool _noShadows;

#if UNITY_WEBGL && !UNITY_EDITOR
	[DllImport("__Internal")] private static extern int OvercookedPhone_QueryInt(string name, int fallback);
#endif

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		int pixelLights = OvercookedPhone_QueryInt("pixellights", -1);
		bool noShadows = OvercookedPhone_QueryInt("shadows", 1) == 0;
		if (pixelLights < 0 && !noShadows)
		{
			return;
		}
		GameObject host = new GameObject("WebGLPortPhoneExperiments");
		DontDestroyOnLoad(host);
		WebGLPortPhoneExperiments runner = host.AddComponent<WebGLPortPhoneExperiments>();
		runner._pixelLights = pixelLights;
		runner._noShadows = noShadows;
		Debug.Log("[webgl-port] phone test: pixellights=" + pixelLights + " shadows=" + (noShadows ? "off" : "game"));
#endif
	}

	private void OnEnable()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void OnDisable()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		StartCoroutine(ApplyNextFrame(scene.name));
	}

	private IEnumerator ApplyNextFrame(string scene)
	{
		yield return null;
		if (_pixelLights >= 0)
		{
			QualitySettings.pixelLightCount = _pixelLights;
		}
		if (_noShadows)
		{
			QualitySettings.shadows = ShadowQuality.Disable;
		}
		Debug.Log("[webgl-port] phone test scene=" + scene + " pixelLights=" + QualitySettings.pixelLightCount
			+ " shadows=" + QualitySettings.shadows + " lights=" + FindObjectsOfType<Light>().Length);
	}
}
