// Softer lightning in the haunted-house levels (the 4-1
// strikes are hard on the eyes). A deliberate difference from Steam, listed in
// notes/symbol-map.md.
//
// In the export, the strike is an Animator clip named LightningFlash on a
// directional light (CampaignGameEnvironment/Light2, resting at 0.5) that
// drives the light's intensity up to 8 twice in about a second, with curve
// overshoot near 10. The same controller is used by all six haunted scenes.
//
// After the Animator has written the frame's value, this squeezes anything
// above 1 through a soft knee that levels off below 1.6 (8 becomes about 1.5;
// the first cut, which peaked at 2.6, was still too bright),
// so the double flash, its timing and the thunder sound are unchanged and a
// strike still reads clearly. Values at or below 1 (the resting light and the
// dark gaps) are untouched.
// ?lightning=full turns this off, for comparing with Steam.

using UnityEngine;
using UnityEngine.SceneManagement;

public class WebGLPortLightning : MonoBehaviour
{
	private const string ClipName = "LightningFlash";
	private const float Knee = 1f;
	private const float Headroom = 0.6f; // the softened value never reaches Knee + Headroom
	private const float Bend = 2f;     // how fast it levels off: 8 -> about 1.47

	private Light m_light;
	private float m_peakIn;
	private float m_peakOut;

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		if (WebGLPortDevFlags.Query(Application.absoluteURL, "lightning") == "full")
		{
			Debug.Log("[webgl-port] lightning: ?lightning=full, flashes at Steam strength");
			return;
		}
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene _scene, LoadSceneMode _mode)
	{
		foreach (Animator animator in Object.FindObjectsOfType<Animator>())
		{
			Light light = animator.GetComponent<Light>();
			if (light == null || animator.GetComponent<WebGLPortLightning>() != null || !HasFlashClip(animator))
			{
				continue;
			}
			animator.gameObject.AddComponent<WebGLPortLightning>().m_light = light;
			Debug.Log("[webgl-port] lightning: softening " + animator.name + " in " + _scene.name + " (rest " + light.intensity.ToString("F2") + ")");
		}
	}

	private static bool HasFlashClip(Animator _animator)
	{
		RuntimeAnimatorController controller = _animator.runtimeAnimatorController;
		if (controller == null)
		{
			return false;
		}
		foreach (AnimationClip clip in controller.animationClips)
		{
			if (clip != null && clip.name == ClipName)
			{
				return true;
			}
		}
		return false;
	}

	public static float Soften(float _value)
	{
		if (_value <= Knee)
		{
			return _value;
		}
		float over = _value - Knee;
		return Knee + Headroom * over / (over + Bend);
	}

	// LateUpdate runs after the Animator has written this frame's intensity and
	// before rendering, so the softened value is the one drawn.
	private void LateUpdate()
	{
		if (m_light == null)
		{
			return;
		}
		float value = m_light.intensity;
		if (value <= Knee)
		{
			if (m_peakIn > 0f)
			{
				Debug.Log("[webgl-port] lightning: flash peak " + m_peakIn.ToString("F2") + " shown as " + m_peakOut.ToString("F2"));
				m_peakIn = 0f;
			}
			return;
		}
		float soft = Soften(value);
		m_light.intensity = soft;
		if (value > m_peakIn)
		{
			m_peakIn = value;
			m_peakOut = soft;
		}
	}
}
