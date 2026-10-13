// ?cooktest=1 (test only). Headless runs can't cook, so the hob flame and pot food
// on moving stoves were only ever seen by hand. With this switch, 3 s after a
// level's cooking stations appear, every station's flame effect is switched on
// and the pot contents under it are shown, re-applied every 2 s, and each
// station logs where it, its flame and its contents are, in world space and on
// screen, so close-up stills of each stove can be cropped from a screenshot.
//
// It bypasses the game's cooking (nothing is cooked, no order changes); off
// unless in the address.

using System.Collections;
using UnityEngine;

public static class WebGLPortCookTest
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		if (WebGLPortDevFlags.Query(Application.absoluteURL, "cooktest") != "1")
		{
			return;
		}
		Debug.Log("[webgl-port] cooktest: on");
		GameObject host = new GameObject("WebGLPortCookTest");
		Object.DontDestroyOnLoad(host);
		host.AddComponent<Runner>();
	}

	private class Runner : MonoBehaviour
	{
		private IEnumerator Start()
		{
			while (true)
			{
				yield return new WaitForSecondsRealtime(1f);
				CookingStation[] stations = Object.FindObjectsOfType<CookingStation>();
				if (stations.Length == 0)
				{
					continue;
				}
				yield return new WaitForSecondsRealtime(3f);
				int tick = 0;
				// Until the level unloads (no stations left).
				while ((stations = Object.FindObjectsOfType<CookingStation>()).Length > 0)
				{
					foreach (CookingStation station in stations)
					{
						if (station.m_flameEffect != null && !station.m_flameEffect.activeSelf)
						{
							station.m_flameEffect.SetActive(true);
						}
						Transform content = null;
						foreach (Transform t in station.GetComponentsInChildren<Transform>(true))
						{
							if (t.name == "Content")
							{
								content = t;
								foreach (Transform child in t)
								{
									child.gameObject.SetActive(true);
								}
							}
						}
						if (tick <= 10)
						{
							Camera cam = Camera.main;
							Vector3 pos = station.transform.position;
							Debug.Log("[webgl-port] cooktest t=" + tick * 2 + "s station=" + Path(station.transform) + " pos=" + pos.ToString("F2")
								+ " screen=" + (cam != null ? cam.WorldToScreenPoint(pos).ToString("F0") : "none") + " screenSize=" + Screen.width + "x" + Screen.height
								+ " flame=" + (station.m_flameEffect != null ? station.m_flameEffect.transform.position.ToString("F3") + " fromStation=" + station.transform.InverseTransformPoint(station.m_flameEffect.transform.position).ToString("F3") : "none")
								+ " content=" + (content != null ? content.position.ToString("F3") + " fromStation=" + station.transform.InverseTransformPoint(content.position).ToString("F3") : "none"));
						}
					}
					tick++;
					yield return new WaitForSecondsRealtime(2f);
				}
				Debug.Log("[webgl-port] cooktest: level left");
			}
		}

		private static string Path(Transform t)
		{
			string path = t.name;
			for (Transform p = t.parent; p != null; p = p.parent)
			{
				path = p.name + "/" + path;
			}
			return path;
		}
	}
}
