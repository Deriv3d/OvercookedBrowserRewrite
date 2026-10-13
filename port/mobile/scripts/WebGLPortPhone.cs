// Phone profile for the optimisation framework (port/mobile/README.md).
// Decides once, before the first scene, whether this is a phone and which
// measuring tools run. It changes nothing the player sees: phone-only cuts
// (resolution, texture format, memory cap) live in the phone page template
// and the phone build, so the desktop build stays exactly as it is.
//
// Page URL switches:
//   ?phone=0/1   override the phone guess
//   ?perf=1      log a perf line every second (always on for phones)
//   ?perf=2      also show it in a small box on screen
//   ?perf=0      no perf lines, even on a phone
//   ?warmup=0    skip shader warm-up (to measure what it saves)
//   ?fps=30      battery saver, opt-in only: caps the frame rate (less smooth
//                than Steam, so never the default)
//
// Phones start on Super Low graphics: the game's own
// lowest preset, SuperQuick (Forward, 1 pixel light, no shadows in
// kitchens). Only when the save has no graphics choice yet; a choice made in
// Options is kept. Desktop keeps Standard (webgl-quality-standard patch).

using System.Runtime.InteropServices;
using UnityEngine;

public static class WebGLPortPhone
{
	// iPhone budgets. The perf monitor flags anything over them.
	public const int HeapBudgetMB = 350;
	public const int DrawCallBudget = 100;
	// Target: a steady 60 FPS. A frame over HitchMs is a visible stutter;
	// FPS under MinSteadyFps for SustainedLowSeconds in a row usually means the
	// phone is too hot and slowing down (Safari exposes no GPU load or temperature).
	public const int TargetFps = 60;
	public const int MinSteadyFps = 55;
	public const float HitchMs = 25f;
	public const int SustainedLowSeconds = 10;

	// Quality level 0 is SuperQuick, shown as Super Low in Options.
	private const int PhoneDefaultQuality = 0;
	// The game's own options key and Unity's quality key, as in a Steam
	// registry. The game falls back to the platform default quality
	// when neither is saved (build7).
	private static readonly string[] QualityKeys = { "Quality", "UnityGraphicsQuality" };

	public static bool IsPhone { get; private set; }
	public static int PerfLevel { get; private set; }
	public static bool WarmupEnabled { get; private set; }

#if UNITY_WEBGL && !UNITY_EDITOR
	[DllImport("__Internal")] private static extern int OvercookedPhone_IsPhone();
	[DllImport("__Internal")] private static extern int OvercookedPhone_QueryInt(string name, int fallback);
	[DllImport("__Internal")] private static extern int OvercookedPhone_HeapMB();
	[DllImport("__Internal")] private static extern void OvercookedPhone_SetOverlay(string text);
#endif

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		IsPhone = OvercookedPhone_IsPhone() != 0;
		PerfLevel = OvercookedPhone_QueryInt("perf", IsPhone ? 1 : 0);
		WarmupEnabled = OvercookedPhone_QueryInt("warmup", 1) != 0;
		if (IsPhone)
		{
			ApplyPhoneDefaultQuality();
		}
		int fpsCap = OvercookedPhone_QueryInt("fps", 0);
		if (fpsCap > 0)
		{
			Application.targetFrameRate = fpsCap;
		}
#else
		IsPhone = false;
		PerfLevel = 0;
		WarmupEnabled = true;
#endif
		Debug.Log("[webgl-port] phone profile phone=" + IsPhone + " perf=" + PerfLevel + " warmup=" + WarmupEnabled + " targetFrameRate=" + Application.targetFrameRate
			+ " screen=" + Screen.width + "x" + Screen.height + " heapMB=" + HeapMB());

		WebGLPortShaderWarmup.Register();
		if (PerfLevel > 0)
		{
			GameObject host = new GameObject("WebGLPortPerfMonitor");
			Object.DontDestroyOnLoad(host);
			host.AddComponent<WebGLPortPerfMonitor>();
		}
	}

	private static void ApplyPhoneDefaultQuality()
	{
		foreach (string key in QualityKeys)
		{
			if (PlayerPrefs.HasKey(key))
			{
				Debug.Log("[webgl-port] phone graphics: kept saved choice " + key + "=" + PlayerPrefs.GetInt(key, -1)
					+ " level=" + QualitySettings.GetQualityLevel());
				return;
			}
		}
		QualitySettings.SetQualityLevel(PhoneDefaultQuality, true);
		Debug.Log("[webgl-port] phone graphics: no saved choice, starting on Super Low level=" + QualitySettings.GetQualityLevel());
	}

	// Size of the WebAssembly heap; -1 outside the browser.
	public static int HeapMB()
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		return OvercookedPhone_HeapMB();
#else
		return -1;
#endif
	}

	public static void SetOverlay(string text)
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		OvercookedPhone_SetOverlay(text);
#endif
	}
}
