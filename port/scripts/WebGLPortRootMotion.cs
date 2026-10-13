// Things riding a moving platform get left behind (the chef
// disappears on 6-4 and the 3-3 trucks, and the hob flame and the food in a
// pot stay put while a lava rock carries the stove away).
//
// Everything that stays behind has an Animator with Apply Root Motion on: the
// chef's model (Player/Chef), HobFlames (FlameFlicker), the pot and pan
// contents (Content/Soup), and plates. Each sits under a platform moved by
// another Animator: FoodTruck_Folder, or Hell_Platform_Animated*. With root
// motion on, the Animator writes the object's WORLD position, as the position
// it read at the start of the animation pass plus the clip's delta. In 2022 every
// Animator reads before any of them writes, so that write puts the object back
// where it was before the platform's Animator moved it this frame. Frame after
// frame the object stays pinned in the world. Things moved by physics, like a
// chef walking or a pusher shoving, move before the animation pass, so they are
// not affected. Steam's 5.5 carried these objects along.
//
// The fix gives each root-motion Animator an OnAnimatorMove handler that
// applies the same delta in the parent's space. With a still parent the
// result is identical; with a moving parent the object rides along. An
// Animator with a Rigidbody on its object is left to Unity.
// ?rootfix=0 turns this off, for comparison.

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WebGLPortRootMotion
{
	private static bool s_on;
	// Per-animator lines, up to 120 per scene (the counts
	// alone don't show whether HobFlames and Soup are among them).
	private static int s_named;
	private static bool s_flameFix;
	// ?flamefix=pin: fallback for the hob flame, in case Unity 2022 still moves it
	// with root motion off. Not the default.
	private static bool s_flamePin;

	// ?rootflat=1: drop the vertical part of each root-motion delta (measured in
	// the parent's space). Test switch for the hob flame bob (Steam's
	// flame only flickers in place). FlameFlicker bakes Y into the pose, so its
	// Y should never arrive as motion; this tells whether ours does.
	public static bool Flat;

	private class Scanner : MonoBehaviour
	{
		private IEnumerator Start()
		{
			string scene = null;
			float since = 0f;
			while (true)
			{
				// Plates and ingredients spawned during play bring their own Animators.
				// For the first 10 s of a scene, inactive ones too: the hob flames and
				// pot contents start switched off and must be hooked before they first
				// show, or a moving stove would leave them behind from the start.
				// (The sceneLoaded scan found none of a level's animators on build 41,
				// so the kitchen can arrive after that callback.)
				yield return new WaitForSecondsRealtime(1f);
				string now = SceneManager.GetActiveScene().name + "/" + SceneManager.sceneCount;
				if (now != scene)
				{
					scene = now;
					s_named = 0;
					since = Time.realtimeSinceStartup;
				}
				Hook(Time.realtimeSinceStartup - since < 10f ? Resources.FindObjectsOfTypeAll<Animator>() : Object.FindObjectsOfType<Animator>(), true);
			}
		}
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		s_on = WebGLPortDevFlags.Query(Application.absoluteURL, "rootfix") != "0";
		s_flameFix = WebGLPortDevFlags.Query(Application.absoluteURL, "flamefix") != "0";
		s_flamePin = WebGLPortDevFlags.Query(Application.absoluteURL, "flamefix") == "pin";
		Flat = WebGLPortDevFlags.Query(Application.absoluteURL, "rootflat") == "1";
		Debug.Log("[webgl-port] root motion: " + (Flat ? "vertical deltas dropped (?rootflat=1), " : "") + (s_on ? "applied in the parent's space (riders follow moving platforms)" : "Unity's own (?rootfix=0)"));
		if (!s_on)
		{
			return;
		}
		SceneManager.sceneLoaded += OnSceneLoaded;
		GameObject host = new GameObject("WebGLPortRootMotion");
		Object.DontDestroyOnLoad(host);
		host.AddComponent<Scanner>();
	}

	private static void OnSceneLoaded(Scene _scene, LoadSceneMode _mode)
	{
		// Inactive ones too (the flames and pot contents start switched off).
		Hook(Resources.FindObjectsOfTypeAll<Animator>(), true);
	}

	private static string PathOf(Transform _t)
	{
		string path = _t.name;
		for (Transform p = _t.parent; p != null; p = p.parent)
		{
			path = p.name + "/" + path;
		}
		return path;
	}

	private static void Hook(Animator[] _animators, bool _log)
	{
		int hooked = 0;
		int flames = 0;
		foreach (Animator animator in _animators)
		{
			// The hob flame (FlameFlicker) is meant to flicker in place on its
			// stove. Its clip only scales it and
			// nudges it by 2 cm, so play it as plain local curves: the flame then
			// rides its stove as an ordinary child, with nothing to accumulate.
			// Run as root motion, it drifted up over the pan and was left behind on
			// a moving rock. This must happen before any
			// handler goes on: an OnAnimatorMove script stops Unity writing the
			// curves to the transform. ?flamefix=0 keeps it as root motion.
			if (s_flameFix && animator != null && animator.applyRootMotion && animator.GetComponent<ParentSpaceRootMotion>() == null && animator.GetComponent<PinnedRootMotion>() == null
				&& animator.runtimeAnimatorController != null && animator.runtimeAnimatorController.name == "FlameFlicker")
			{
				if (s_flamePin)
				{
					// Fallback: keep root motion, but hold the flame at the local spot it
					// had when the kitchen loaded; the scale flicker still plays.
					animator.gameObject.AddComponent<PinnedRootMotion>();
				}
				else
				{
					animator.applyRootMotion = false;
				}
				flames++;
				continue;
			}
			// A body that moves through physics keeps Unity's own handling.
			if (animator == null || !animator.applyRootMotion || animator.hideFlags != HideFlags.None || !animator.gameObject.scene.IsValid()
				|| animator.GetComponent<ParentSpaceRootMotion>() != null || animator.GetComponent<PinnedRootMotion>() != null || animator.GetComponent<Rigidbody>() != null)
			{
				continue;
			}
			animator.gameObject.AddComponent<ParentSpaceRootMotion>();
			hooked++;
			if (_log && s_named < 120)
			{
				s_named++;
				Debug.Log("[webgl-port] root motion: hooked " + PathOf(animator.transform) + (animator.gameObject.activeInHierarchy ? "" : " (inactive)"));
			}
		}
		if (_log && flames > 0)
		{
			Debug.Log("[webgl-port] root motion: " + flames + " hob flames play FlameFlicker as local curves (scene " + SceneManager.GetActiveScene().name + ")");
		}
		if (_log && hooked > 0)
		{
			Debug.Log("[webgl-port] root motion: " + hooked + " root-motion animators apply their motion in the parent's space (scene " + SceneManager.GetActiveScene().name + ")");
		}
	}
}

// On a GameObject with a root-motion Animator: applies the frame's root motion
// relative to the parent instead of as an absolute world pose. HobFlames
// carries two FlameFlicker Animators; Unity calls this once for each, so the
// first call of a frame applies both deltas and the second does nothing.
public class ParentSpaceRootMotion : MonoBehaviour
{
	private Animator[] m_animators;
	private int m_frame = -1;

	private void Awake()
	{
		m_animators = GetComponents<Animator>();
	}

	private void OnAnimatorMove()
	{
		if (m_frame == Time.frameCount)
		{
			return;
		}
		m_frame = Time.frameCount;
		Vector3 delta = Vector3.zero;
		Quaternion turn = Quaternion.identity;
		foreach (Animator animator in m_animators)
		{
			if (animator != null && animator.isActiveAndEnabled && animator.applyRootMotion)
			{
				delta += animator.deltaPosition;
				turn = animator.deltaRotation * turn;
			}
		}
		Transform t = transform;
		Transform parent = t.parent;
		if (parent == null)
		{
			if (WebGLPortRootMotion.Flat)
			{
				delta.y = 0f;
			}
			t.position += delta;
			t.rotation = turn * t.rotation;
			return;
		}
		Vector3 local = parent.InverseTransformVector(delta);
		if (WebGLPortRootMotion.Flat)
		{
			local.y = 0f;
		}
		t.localPosition += local;
		Quaternion parentRot = parent.rotation;
		t.localRotation = Quaternion.Inverse(parentRot) * turn * parentRot * t.localRotation;
	}
}

// ?flamefix=pin only: takes over root motion and ignores it, holding the object
// at the local pose it had when added. Scale curves still play.
public class PinnedRootMotion : MonoBehaviour
{
	private Vector3 m_position;
	private Quaternion m_rotation;

	private void Awake()
	{
		m_position = transform.localPosition;
		m_rotation = transform.localRotation;
	}

	private void OnAnimatorMove()
	{
		transform.localPosition = m_position;
		transform.localRotation = m_rotation;
	}
}
