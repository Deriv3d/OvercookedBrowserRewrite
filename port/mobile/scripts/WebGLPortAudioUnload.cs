// Frees long audio that isn't playing, on phones. Web Audio keeps every loaded clip as raw PCM, and the game never
// lets go of music it has played: the menu music (49 MB) stayed loaded on the
// rooftop and the world map, and WorldMap grew from 97 to 187 MB of decoded
// audio after a few levels (phone draw reports).
//
// 5 s after each scene change, every loaded clip of 20 s or more that no
// AudioSource is playing gets AudioClip.UnloadAudioData(). A clip the game
// plays again later is loaded again by AudioSource.Play (a short load delay,
// no other change). Phones only; ?audiounload=0 turns it off, ?audiounload=1
// turns it on for a desktop test.

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WebGLPortAudioUnload
{
	private const float MinSeconds = 20f;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Init()
	{
		string flag = WebGLPortDevFlags.Query(Application.absoluteURL, "audiounload");
		bool on = flag == "1" || (flag != "0" && WebGLPortPhone.IsPhone);
		if (!on)
		{
			return;
		}
		Debug.Log("[webgl-port] audio unload: on (clips of " + MinSeconds + " s or more not playing, 5 s after each scene change)");
		GameObject host = new GameObject("WebGLPortAudioUnload");
		Object.DontDestroyOnLoad(host);
		host.AddComponent<Runner>();
	}

	private class Runner : MonoBehaviour
	{
		private IEnumerator Start()
		{
			string last = null;
			while (true)
			{
				string now = SceneManager.GetActiveScene().name + "/" + SceneManager.sceneCount;
				if (now != last)
				{
					last = now;
					yield return new WaitForSecondsRealtime(5f);
					Unload(SceneManager.GetActiveScene().name);
				}
				yield return new WaitForSecondsRealtime(1f);
			}
		}

		private static void Unload(string scene)
		{
			System.Collections.Generic.HashSet<AudioClip> playing = new System.Collections.Generic.HashSet<AudioClip>();
			foreach (AudioSource source in Object.FindObjectsOfType<AudioSource>())
			{
				// A paused source reports isPlaying false but keeps its position;
				// keep its clip so UnPause doesn't come back silent.
				if (source.clip != null && (source.isPlaying || source.time > 0f))
				{
					playing.Add(source.clip);
				}
			}
			int count = 0;
			double mb = 0;
			foreach (AudioClip clip in Resources.FindObjectsOfTypeAll<AudioClip>())
			{
				if (clip.length < MinSeconds || clip.loadState != AudioDataLoadState.Loaded || playing.Contains(clip))
				{
					continue;
				}
				double size = clip.samples * (double)clip.channels * 4 / 1048576;
				if (clip.UnloadAudioData())
				{
					count++;
					mb += size;
				}
			}
			Debug.Log("[webgl-port] audio unload: scene=" + scene + " freed " + count + " clips, about " + mb.ToString("0") + " MB decoded; kept " + playing.Count + " playing");
		}
	}
}
