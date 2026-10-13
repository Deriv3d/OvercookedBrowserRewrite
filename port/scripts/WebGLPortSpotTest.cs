// Test switches for the dim spot lights in 4-2 (the flashlight pools
// are about 3-4x dimmer than Steam's). Inactive unless in the page address.
//   ?spotinner=1     each spot's innerSpotAngle = its spotAngle (no inner falloff)
//   ?spotcookie=flat each spot without a cookie gets a plain white one
//   ?spotcookie=55   each spot without a cookie gets 5.5's default spot cookie
//                    profile (WebGLPortSpotCookie55; hard pool edge)
//   ?spotgain=K      each spot's intensity times K (once per light), to measure
//                    how far 4-2's pools are from the wiki picture
//   ?spotrange=R     each spot's range set to R (Steam's pool
//                    brightness and hard edge fit a range of ~15, ours is 4);
//                    re-applied every second, since FlickeringLight rewrites
//                    range each frame from the value it saw in Awake
//   ?lightwatch=1    (also on with spotrange) for 5 s after each scene load,
//                    one line a second per spot and per light carrying a game
//                    LightModifier: range, intensity, the modifiers in order and
//                    the particle fill ParticleLightIntensity multiplies by (it
//                    scales intensity by particleCount/maxParticles every frame,
//                    so an empty particle system leaves the hob light at 0)
// Applied at every scene load and to spots that appear later (the chefs'
// flashlights come with the players), checked once a second.

using System.Collections;
using UnityEngine;

public static class WebGLPortSpotTest
{
	private static bool s_inner;
	private static bool s_flat;
	private static bool s_cookie55;
	private static Texture2D s_white;
	private static float s_gain = 1f;
	private static float s_range;
	private static bool s_watch;
	private static readonly System.Collections.Generic.HashSet<int> s_gained = new System.Collections.Generic.HashSet<int>();

	private class Runner : MonoBehaviour
	{
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		s_inner = WebGLPortDevFlags.Query(Application.absoluteURL, "spotinner") == "1";
		s_flat = WebGLPortDevFlags.Query(Application.absoluteURL, "spotcookie") == "flat";
		s_cookie55 = WebGLPortDevFlags.Query(Application.absoluteURL, "spotcookie") == "55";
		string gain = WebGLPortDevFlags.Query(Application.absoluteURL, "spotgain");
		if (gain != null)
		{
			float.TryParse(gain, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s_gain);
			if (s_gain <= 0f)
			{
				s_gain = 1f;
			}
		}
		string range = WebGLPortDevFlags.Query(Application.absoluteURL, "spotrange");
		if (range != null)
		{
			float.TryParse(range, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s_range);
		}
		s_watch = s_range > 0f || WebGLPortDevFlags.Query(Application.absoluteURL, "lightwatch") == "1";
		if (!s_inner && !s_flat && !s_cookie55 && s_gain == 1f && s_range <= 0f && !s_watch)
		{
			return;
		}
		Debug.Log("[webgl-port] spot test: inner=" + s_inner + " flatCookie=" + s_flat + " cookie55=" + s_cookie55 + " gain=" + s_gain + " range=" + s_range + " watch=" + s_watch);
		GameObject host = new GameObject("WebGLPortSpotTest");
		Object.DontDestroyOnLoad(host);
		Runner runner = host.AddComponent<Runner>();
		runner.StartCoroutine(Apply());
		if (s_watch)
		{
			runner.StartCoroutine(Watch());
		}
	}

	private static IEnumerator Apply()
	{
		while (true)
		{
			if (s_cookie55)
			{
				WebGLPortSpotCookie55.Apply();
			}
			foreach (Light light in Object.FindObjectsOfType<Light>())
			{
				if (light.type != LightType.Spot)
				{
					continue;
				}
				// By name, so a compile against older UnityEngine reference DLLs passes too.
				System.Reflection.PropertyInfo inner = typeof(Light).GetProperty("innerSpotAngle");
				if (s_inner && inner != null && (float)inner.GetValue(light, null) != light.spotAngle)
				{
					inner.SetValue(light, light.spotAngle, null);
					Debug.Log("[webgl-port] spot test: " + light.name + " innerSpotAngle=" + light.spotAngle);
				}
				if (s_gain != 1f && s_gained.Add(light.GetInstanceID()))
				{
					light.intensity *= s_gain;
					Debug.Log("[webgl-port] spot test: " + light.name + " intensity x" + s_gain + " = " + light.intensity.ToString("F2"));
				}
				if (s_range > 0f && light.range != s_range)
				{
					light.range = s_range;
				}
				if (s_flat && light.cookie == null)
				{
					if (s_white == null)
					{
						s_white = new Texture2D(4, 4, TextureFormat.RGBA32, false);
						Color[] pixels = new Color[16];
						for (int i = 0; i < pixels.Length; i++)
						{
							pixels[i] = Color.white;
						}
						s_white.SetPixels(pixels);
						s_white.wrapMode = TextureWrapMode.Clamp;
						s_white.Apply();
					}
					light.cookie = s_white;
					Debug.Log("[webgl-port] spot test: " + light.name + " flat cookie");
				}
			}
			yield return new WaitForSecondsRealtime(1f);
		}
	}

	// Five one-second samples after every scene change.
	private static IEnumerator Watch()
	{
		string scene = null;
		while (true)
		{
			string now = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "/" + UnityEngine.SceneManagement.SceneManager.sceneCount;
			if (now != scene)
			{
				scene = now;
				for (int second = 0; second < 5; second++)
				{
					yield return new WaitForSecondsRealtime(1f);
					int lines = 0;
					foreach (Light light in Object.FindObjectsOfType<Light>())
					{
						LightModifier[] modifiers = light.GetComponents<LightModifier>();
						if ((light.type != LightType.Spot && modifiers.Length == 0) || lines >= 40)
						{
							continue;
						}
						lines++;
						System.Text.StringBuilder line = new System.Text.StringBuilder("[webgl-port] diag lightwatch t=" + (second + 1) + "s light=" + light.name + " parent=" + (light.transform.parent != null ? light.transform.parent.name : "none")
							+ " type=" + light.type + " active=" + light.isActiveAndEnabled + " range=" + light.range.ToString("F2") + " intensity=" + light.intensity.ToString("F3") + " spotAngle=" + light.spotAngle.ToString("F1") + " modifiers=");
						foreach (LightModifier modifier in modifiers)
						{
							line.Append(modifier.GetType().Name).Append(modifier.enabled ? "" : "(off)").Append(',');
						}
						ParticleSystem particles = light.GetComponentInParent<ParticleSystem>();
						if (particles == null)
						{
							particles = light.GetComponentInChildren<ParticleSystem>();
						}
						if (particles != null)
						{
							line.Append(" particles=").Append(particles.name).Append(' ').Append(particles.particleCount).Append('/').Append(particles.maxParticles).Append(" playing=").Append(particles.isPlaying);
						}
						Debug.Log(line.ToString());
					}
				}
			}
			yield return new WaitForSecondsRealtime(1f);
		}
	}
}
