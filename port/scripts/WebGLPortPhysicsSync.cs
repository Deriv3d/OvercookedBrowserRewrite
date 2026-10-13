// Chefs thrown about on the moving truck kitchens in 3-3.
//
// The game parents a chef to whatever ground it stands on (a raycast down
// from the chef each Update, DynamicLandscapeParenting), so a moving truck
// carries its chefs along through the transform hierarchy, and the same
// raycast decides when a chef has stepped onto or off a truck.
//
// Unity 5.5 pushed every transform change to the physics scene at once, so
// those raycasts and the chef's rigidbody always saw the truck where it is
// drawn. From Unity 2018.3 that is off by default (Physics.autoSyncTransforms
// false): a moved transform only reaches physics at the next physics step, so
// between steps the raycast sees the truck where it was, and the chef's body
// is pushed back to its last physics pose. Turning the setting on restores
// the 5.5 behaviour. ?physsync=0 leaves it off, for comparison.
//
// The first time a level parents a chef, its rigidbody and the movers above
// it are logged ("[webgl-port] diag ride"), in case more is needed.

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WebGLPortPhysicsSync
{
	private class Runner : MonoBehaviour
	{
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		bool on = WebGLPortDevFlags.Query(Application.absoluteURL, "physsync") != "0";
		// By name, so a compile against older UnityEngine reference DLLs passes too.
		System.Reflection.PropertyInfo sync = typeof(Physics).GetProperty("autoSyncTransforms");
		if (sync == null)
		{
			Debug.LogWarning("[webgl-port] physics: no Physics.autoSyncTransforms in this Unity");
			return;
		}
		sync.SetValue(null, on, null);
		Debug.Log("[webgl-port] physics: autoSyncTransforms=" + on + (on ? " (as in Unity 5.5)" : " (?physsync=0)"));
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene _scene, LoadSceneMode _mode)
	{
		if (_mode != LoadSceneMode.Single || Object.FindObjectOfType<DynamicLandscapeParenting>() == null)
		{
			return;
		}
		GameObject host = new GameObject("WebGLPortPhysicsSync");
		host.AddComponent<Runner>().StartCoroutine(LogRide(host, _scene.name));
	}

	// Waits (up to 5 minutes, checking once a second) for a chef standing on
	// ground that moved since the last check, then logs it once.
	private static IEnumerator LogRide(GameObject _host, string _scene)
	{
		System.Collections.Generic.Dictionary<int, Vector3> lastPos = new System.Collections.Generic.Dictionary<int, Vector3>();
		for (int tries = 0; tries < 300 && _host != null; tries++)
		{
			yield return new WaitForSecondsRealtime(1f);
			foreach (DynamicLandscapeParenting rider in Object.FindObjectsOfType<DynamicLandscapeParenting>())
			{
				Transform ground = rider.transform.parent;
				if (ground == null)
				{
					continue;
				}
				Vector3 before;
				bool moved = lastPos.TryGetValue(ground.GetInstanceID(), out before) && (ground.position - before).sqrMagnitude > 0.0001f;
				lastPos[ground.GetInstanceID()] = ground.position;
				if (!moved)
				{
					continue;
				}
				Animator animator = ground.GetComponentInParent<Animator>();
				Rigidbody groundBody = ground.GetComponentInParent<Rigidbody>();
				Rigidbody body = rider.GetComponent<Rigidbody>();
				Debug.Log("[webgl-port] diag ride scene=" + _scene + " chef=" + rider.name + " ground=" + ground.name
					+ " body interp=" + (body != null ? body.interpolation.ToString() : "none") + " kinematic=" + (body != null && body.isKinematic)
					+ " animator=" + (animator != null ? animator.name + " update=" + animator.updateMode + " root=" + animator.applyRootMotion : "none")
					+ " groundBody=" + (groundBody != null ? groundBody.name + " kinematic=" + groundBody.isKinematic + " interp=" + groundBody.interpolation : "none")
					+ " fixedDt=" + Time.fixedDeltaTime);
				Object.Destroy(_host);
				yield break;
			}
		}
		if (_host != null)
		{
			Object.Destroy(_host);
		}
	}
}
