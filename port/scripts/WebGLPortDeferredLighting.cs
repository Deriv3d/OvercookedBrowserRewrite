// Stand-in for Unity 5.5's legacy Deferred Lighting path.
//
// The game's performance settings put the camera on RenderPath 2 (legacy
// DeferredLighting) at Standard, and on 3 (DeferredShading) at Dreamy and in
// the SuperQuick menu. Unity 2022 no longer has legacy deferred, and
// on WebGL the camera falls back to Forward (build10 diag: path=DeferredLighting
// actualPath=Forward). Deferred lights every light per pixel; Forward lights
// only the pixelLightCount most important ones (3 at Standard) per pixel and
// folds the rest into the ambient probe. So on the rooftop the blue "Light2"
// is dropped and only the orange ambient and fires remain, and on
// the menu the strong-shadow sun goes to the add pass.
//
// While a camera asks for a deferred path that does not run, lift the pixel
// light limit so every light is drawn per pixel, as it was in 5.5. The level's
// own limit comes back when the path is Forward again.
//
// Real DeferredShading (Dreamy) runs on WebGL2. Our replacement shaders were
// forward-only at first, and under deferred Unity draws forward-only objects
// after the deferred geometry whatever their queue, so Custom/WaveyLiquid
// (ZTest Always) painted the sea and lava over floors and counters at Dreamy,
// and DeferredShading was turned into the Forward stand-in.
// The shaders now have deferred passes, and build 37 showed real
// deferred correct on 3-1, 3-2, 5-2, 5-4, 4-1, 4-2 and the menu book, with
// smoother loads, so on desktop Dreamy runs real deferred again, as on Steam.
// ?deferred=0 forces the stand-in, for comparison.
//
// Phones keep the stand-in by default: real deferred crashed a phone in testing
// on Continue while the stand-in got through. ?deferred=1 keeps
// real deferred there.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WebGLPortDeferredLighting
{
	// More than any scene has lights (the rooftop has 53).
	private const int DeferredPixelLights = 64;

	private static readonly Dictionary<int, int> s_ownLimit = new Dictionary<int, int>();

	private static bool s_keepDeferred;

	// Catches the path being set again mid-level (Options > Graphics).
	private class Watcher : MonoBehaviour
	{
		private void LateUpdate()
		{
			Camera cam = Camera.main;
			if (cam != null && cam.renderingPath == RenderingPath.DeferredShading && !s_keepDeferred)
			{
				Apply(cam, cam.gameObject.scene.name);
			}
		}
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Register()
	{
		string flag = WebGLPortDevFlags.Query(Application.absoluteURL, "deferred");
		s_keepDeferred = Application.isMobilePlatform ? flag == "1" : flag != "0";
		Debug.Log("[webgl-port] lighting: " + (s_keepDeferred ? "real DeferredShading kept" : "DeferredShading runs the Forward stand-in")
			+ " (mobile=" + Application.isMobilePlatform + ", ?deferred=" + (flag ?? "unset") + ")");
		// Runs after the scene's Awake calls, so PerformanceManager has set the path.
		SceneManager.sceneLoaded += OnSceneLoaded;
		if (!s_keepDeferred)
		{
			GameObject host = new GameObject("WebGLPortDeferredLighting");
			Object.DontDestroyOnLoad(host);
			host.AddComponent<Watcher>();
		}
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		Camera cam = Camera.main;
		if (cam == null)
		{
			return;
		}
		Apply(cam, scene.name);
	}

	// By name: Camera.allowHDR in 2022 (Camera.hdr in Unity 5.5).
	private static string HdrOf(Camera cam)
	{
		System.Reflection.PropertyInfo hdr = typeof(Camera).GetProperty("allowHDR") ?? typeof(Camera).GetProperty("hdr");
		return hdr != null ? hdr.GetValue(cam, null).ToString() : "?";
	}

	private static void Apply(Camera cam, string sceneName)
	{
		string swapped = "";
		if (cam.renderingPath == RenderingPath.DeferredShading && !s_keepDeferred)
		{
			// Legacy deferred falls back to Forward on 2022, which is the stand-in below.
			cam.renderingPath = RenderingPath.DeferredLighting;
			swapped = " (from DeferredShading)";
		}
		int level = QualitySettings.GetQualityLevel();
		if (!s_ownLimit.ContainsKey(level))
		{
			s_ownLimit[level] = QualitySettings.pixelLightCount;
		}
		bool wantsDeferred = cam.renderingPath == RenderingPath.DeferredLighting || cam.renderingPath == RenderingPath.DeferredShading;
		bool runsForward = cam.actualRenderingPath == RenderingPath.Forward;
		int limit = wantsDeferred && runsForward ? DeferredPixelLights : s_ownLimit[level];
		if (QualitySettings.pixelLightCount != limit)
		{
			QualitySettings.pixelLightCount = limit;
		}
		Debug.Log("[webgl-port] lighting scene=" + sceneName + " quality=" + QualitySettings.names[level] + " path=" + cam.renderingPath + swapped + " actual=" + cam.actualRenderingPath
			+ " pixelLights=" + QualitySettings.pixelLightCount + " hdr=" + HdrOf(cam) + (limit == DeferredPixelLights ? " (deferred stand-in)" : ""));
	}
}
