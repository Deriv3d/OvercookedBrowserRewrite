// Things riding moving ground: on 6-4 the chef disappears
// on a moving platform; on the lava levels food put into a moving pot
// and the hob flame stay where they were while the stove moves on.
//
// Log only, nothing in the game changes. In every level scene it:
//  - indexes the moving ground: every transform that moves during the first
//    four minutes and whose parent doesn't ("diag mover" lines: path, how far
//    it moved and turned, what drives it, colliders and cooking stations on it);
//  - checks that what sits on a mover follows it: a renderer whose transform
//    moves while its drawn bounds don't (a static batch), a renderer under the
//    mover that stays put, or a renderer that was on the mover but isn't parented
//    to it ("diag left-behind", with its transform chain and what drives each
//    link);
//  - watches each chef every frame and logs when it stops being drawn and why
//    ("diag vanish": inactive, renderers off, culled while on screen, scale,
//    fell, layer not drawn), and every change of the ground it is parented to
//    ("diag ride-parent").
// ?ridetest=1 also puts the first chef on each moving ground in turn (20 s each),
// so a run with no input covers every mover. Where nothing moves on its own
// (6-4's rooms turn on a switch), it fires the triggers of the Animators that
// carry walkable ground, and fires them again each time a chef is placed.
//
// Two test switches for the cause (off unless in the address, not fixes yet):
//   ?rideinterp=0  turns rigidbody interpolation off on every body in the level
//                  (checked once a second, so food spawned later is covered).
//                  Suspect: an interpolated body writes its last physics pose
//                  back over its transform each frame, so a body parented to a
//                  mover after load (the chef, food put in a pot) never takes
//                  the mover's motion. 5.5 pushed every transform change to
//                  physics at once; 2022's autoSyncTransforms only syncs before
//                  the next physics query or step.
//   ?ridesync=1    calls Physics.SyncTransforms() at the end of every frame,
//                  so physics sees where the movers were drawn.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WebGLPortRideWatch
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		// Only on request (?ridewatch=1, ?ridetest=1 or a test switch below): it
		// scans every renderer once a second, which normal play (and phones)
		// shouldn't pay for.
		string url = Application.absoluteURL;
		if (WebGLPortDevFlags.Query(url, "ridewatch") != "1" && WebGLPortDevFlags.Query(url, "ridetest") != "1"
			&& WebGLPortDevFlags.Query(url, "rideinterp") != "0" && WebGLPortDevFlags.Query(url, "ridesync") != "1")
		{
			return;
		}
		Debug.Log("[webgl-port] diag ride-watch: on");
		GameObject host = new GameObject("WebGLPortRideWatchPoller");
		Object.DontDestroyOnLoad(host);
		host.AddComponent<Poller>();
	}

	// Builds 40 and 41 logged nothing from a sceneLoaded hook, and the root-motion
	// hook found none of the level's animators at sceneLoaded either, so the
	// kitchen can arrive after that callback. Instead, check once a second for
	// a scene with chefs in it that hasn't been watched yet.
	private class Poller : MonoBehaviour
	{
		private IEnumerator Start()
		{
			string watched = null;
			string waiting = null;
			while (true)
			{
				yield return new WaitForSecondsRealtime(1f);
				string scene = SceneManager.GetActiveScene().name;
				if (scene == watched)
				{
					continue;
				}
				if (Object.FindObjectOfType<DynamicLandscapeParenting>() == null)
				{
					if (scene != waiting)
					{
						waiting = scene;
						Debug.Log("[webgl-port] diag ride-watch waiting scene=" + scene + " (no chefs yet)");
					}
					continue;
				}
				watched = scene;
				Watcher watcher = new GameObject("WebGLPortRideWatch").AddComponent<Watcher>();
				string url = Application.absoluteURL;
				watcher.Begin(scene, WebGLPortDevFlags.Query(url, "ridetest") == "1", WebGLPortDevFlags.Query(url, "rideinterp") == "0", WebGLPortDevFlags.Query(url, "ridesync") == "1");
			}
		}
	}

	private class Mover
	{
		public Transform root;
		public string path;
		public Bounds bounds;
		public bool walkable;
		// Per renderer: position and bounds centre in the mover's space, then in the world.
		public Dictionary<int, Vector3[]> local = new Dictionary<int, Vector3[]>();
	}

	private class Chef
	{
		public DynamicLandscapeParenting rider;
		public Renderer[] renderers;
		public Transform parent;
		public string reasons = "";
		public float startY;
	}

	private class Watcher : MonoBehaviour
	{
		private const float Window = 240f;
		private const int MaxLines = 80;

		private string m_scene;
		private bool m_rideTest;
		private bool m_noInterp;
		private bool m_sync;
		private int m_interpOff;
		private int m_lines;
		private float m_start;
		private Transform[] m_all;
		private Vector3[] m_pos0;
		private Quaternion[] m_rot0;
		private bool[] m_moved;
		private readonly List<Mover> m_movers = new List<Mover>();
		private readonly HashSet<int> m_reported = new HashSet<int>();
		private readonly HashSet<int> m_initial = new HashSet<int>();
		private readonly List<Chef> m_chefs = new List<Chef>();

		public void Begin(string _scene, bool _rideTest, bool _noInterp, bool _sync)
		{
			m_scene = _scene;
			m_rideTest = _rideTest;
			m_noInterp = _noInterp;
			m_sync = _sync;
			StartCoroutine(Run());
		}

		private bool Log(string _line)
		{
			if (m_lines >= MaxLines)
			{
				return false;
			}
			m_lines++;
			Debug.Log("[webgl-port] diag " + _line + (m_lines == MaxLines ? " (ride watch: line limit reached)" : ""));
			return true;
		}

		private IEnumerator Run()
		{
			yield return new WaitForSecondsRealtime(1f);
			m_start = Time.realtimeSinceStartup;
			foreach (DynamicLandscapeParenting rider in Object.FindObjectsOfType<DynamicLandscapeParenting>())
			{
				// Only the chefs: items like FireExtinguisher_Rigidbody ride the
				// ground the same way (build 42: ridetest moved the extinguisher).
				if (!IsPlayer(rider))
				{
					continue;
				}
				Chef chef = new Chef();
				chef.rider = rider;
				chef.renderers = rider.GetComponentsInChildren<Renderer>(true);
				chef.parent = rider.transform.parent;
				chef.startY = rider.transform.position.y;
				m_chefs.Add(chef);
			}
			Log("ride-watch scene=" + m_scene + " chefs=" + m_chefs.Count + " ridetest=" + m_rideTest + " rideinterp=" + (m_noInterp ? "0" : "game") + " ridesync=" + m_sync + " autoSyncTransforms=" + AutoSync());
			Snapshot();
			int nextTest = 0;
			float nextTestAt = 25f;
			while (Time.realtimeSinceStartup - m_start < Window)
			{
				yield return new WaitForSecondsRealtime(1f);
				InterpolationOff();
				FindMovers();
				CheckFollow();
				if (m_rideTest && m_movers.Count == 0 && Time.realtimeSinceStartup - m_start >= nextTestAt)
				{
					// Nothing moves on its own (6-4: the rooms turn only when a chef
					// hits a switch), so fire the movers' own triggers.
					KickMovers();
					nextTestAt += 20f;
				}
				else if (m_rideTest && m_movers.Count > 0 && Time.realtimeSinceStartup - m_start >= nextTestAt)
				{
					PlaceChef(m_movers[nextTest % m_movers.Count]);
					if (m_kicks > 0)
					{
						// Movers that only move on a switch: move it again with the chef aboard.
						KickMovers();
					}
					nextTest++;
					nextTestAt += 20f;
				}
			}
			Log("ride-watch done scene=" + m_scene + " movers=" + m_movers.Count);
		}

		private void InterpolationOff()
		{
			if (!m_noInterp)
			{
				return;
			}
			int before = m_interpOff;
			foreach (Rigidbody body in Object.FindObjectsOfType<Rigidbody>())
			{
				if (body.interpolation != RigidbodyInterpolation.None)
				{
					body.interpolation = RigidbodyInterpolation.None;
					m_interpOff++;
				}
			}
			if (m_interpOff > before)
			{
				Log("rideinterp scene=" + m_scene + " interpolation turned off on " + (m_interpOff - before) + " bodies (total " + m_interpOff + ")");
			}
		}

		private static bool IsPlayer(DynamicLandscapeParenting _rider)
		{
			foreach (MonoBehaviour behaviour in _rider.GetComponents<MonoBehaviour>())
			{
				if (behaviour != null && behaviour.GetType().Name == "PlayerControls")
				{
					return true;
				}
			}
			return false;
		}

		// ?ridetest only. Fires the triggers of every Animator that carries walkable
		// "Ground" colliders, the way a level's switch would (6-4: WestRoom's
		// Rotate_Clockwise). Alternates between the triggers on each kick.
		private int m_kicks;

		private void KickMovers()
		{
			int ground = LayerMask.NameToLayer("Ground");
			foreach (Animator animator in Object.FindObjectsOfType<Animator>())
			{
				if (animator.runtimeAnimatorController == null || animator.GetComponentInParent<DynamicLandscapeParenting>() != null)
				{
					continue;
				}
				bool walkable = false;
				foreach (Collider c in animator.GetComponentsInChildren<Collider>())
				{
					if (!c.isTrigger && c.gameObject.layer == ground && c.bounds.size.x > 0.8f && c.bounds.size.z > 0.8f)
					{
						walkable = true;
						break;
					}
				}
				if (!walkable)
				{
					continue;
				}
				List<string> triggers = new List<string>();
				foreach (AnimatorControllerParameter parameter in animator.parameters)
				{
					if (parameter.type == AnimatorControllerParameterType.Trigger)
					{
						triggers.Add(parameter.name);
					}
				}
				if (triggers.Count == 0)
				{
					continue;
				}
				string fire = triggers[m_kicks % triggers.Count];
				animator.SetTrigger(fire);
				Log("ridetest scene=" + m_scene + " kick " + PathOf(animator.transform) + " trigger=" + fire);
			}
			m_kicks++;
		}

		private static string AutoSync()
		{
			System.Reflection.PropertyInfo sync = typeof(Physics).GetProperty("autoSyncTransforms");
			return sync != null ? sync.GetValue(null, null).ToString() : "n/a";
		}

		private void Snapshot()
		{
			List<Transform> keep = new List<Transform>();
			foreach (Transform t in Object.FindObjectsOfType<Transform>())
			{
				if (!(t is RectTransform) && t.GetComponentInParent<DynamicLandscapeParenting>() == null && t.GetComponentInParent<Camera>() == null)
				{
					keep.Add(t);
				}
			}
			m_all = keep.ToArray();
			foreach (Renderer r in Object.FindObjectsOfType<Renderer>())
			{
				m_initial.Add(r.GetInstanceID());
			}
			m_pos0 = new Vector3[m_all.Length];
			m_rot0 = new Quaternion[m_all.Length];
			m_moved = new bool[m_all.Length];
			for (int i = 0; i < m_all.Length; i++)
			{
				m_pos0[i] = m_all[i].position;
				m_rot0[i] = m_all[i].rotation;
			}
		}

		// A mover is a transform that moved or turned since the start while its
		// parent didn't: the top of whatever the level moves.
		private void FindMovers()
		{
			HashSet<int> movedIds = new HashSet<int>();
			for (int i = 0; i < m_all.Length; i++)
			{
				Transform t = m_all[i];
				if (t == null)
				{
					continue;
				}
				if (!m_moved[i] && ((t.position - m_pos0[i]).sqrMagnitude > 0.0025f || Quaternion.Angle(t.rotation, m_rot0[i]) > 2f))
				{
					m_moved[i] = true;
				}
				if (m_moved[i])
				{
					movedIds.Add(t.GetInstanceID());
				}
			}
			for (int i = 0; i < m_all.Length; i++)
			{
				Transform t = m_all[i];
				if (t == null || !m_moved[i] || m_reported.Contains(t.GetInstanceID()))
				{
					continue;
				}
				if (t.parent != null && movedIds.Contains(t.parent.GetInstanceID()))
				{
					continue;
				}
				Rigidbody body = t.GetComponentInParent<Rigidbody>();
				if (body != null && !body.isKinematic)
				{
					continue; // loose items (plates, ingredients) moved by the players
				}
				if (t.GetComponent<ParticleSystem>() != null || t.GetComponentInChildren<Collider>() == null || t.GetComponentInParent<DynamicLandscapeParenting>() != null)
				{
					continue; // effects, decoration nobody can stand on, things a chef holds
				}
				if (t.GetComponent<Rigidbody>() != null && SizeOf(t) < 1.2f)
				{
					continue; // a pot, pan or plate that was carried somewhere else
				}
				m_reported.Add(t.GetInstanceID());
				AddMover(t, m_pos0[i], m_rot0[i]);
			}
		}

		private void AddMover(Transform _root, Vector3 _start, Quaternion _startRot)
		{
			Mover mover = new Mover();
			mover.root = _root;
			mover.path = PathOf(_root);
			int colliders = 0;
			bool first = true;
			foreach (Collider c in _root.GetComponentsInChildren<Collider>(true))
			{
				if (c.isTrigger)
				{
					continue;
				}
				colliders++;
				if (first)
				{
					mover.bounds = c.bounds;
					first = false;
				}
				else
				{
					mover.bounds.Encapsulate(c.bounds);
				}
				// Floor-like: wide, flat and facing up (a chef can stand on it).
				Vector3 size = c.bounds.size;
				if (size.x > 0.8f && size.z > 0.8f)
				{
					mover.walkable = true;
				}
			}
			int stations = 0;
			foreach (MonoBehaviour behaviour in _root.GetComponentsInChildren<MonoBehaviour>(true))
			{
				if (behaviour != null && behaviour.GetType().Name == "CookingStation")
				{
					stations++;
				}
			}
			m_movers.Add(mover);
			Log("mover scene=" + m_scene + " root=" + mover.path + " t=" + (Time.realtimeSinceStartup - m_start).ToString("F0")
				+ " move=" + (_root.position - _start).magnitude.ToString("F2") + " turn=" + Quaternion.Angle(_root.rotation, _startRot).ToString("F0") + " driver=" + DriverOf(_root)
				+ " colliders=" + colliders + " walkable=" + mover.walkable + " stations=" + stations
				+ " size=" + mover.bounds.size.ToString("F1"));
		}

		// What sits on a mover must move with it. Each renderer's position (and its
		// drawn bounds' centre) is kept in the mover's own space, so a mover that
		// only turns (6-4's ring) is checked as well as one that slides.
		private void CheckFollow()
		{
			Renderer[] all = null;
			foreach (Mover mover in m_movers)
			{
				if (mover.root == null)
				{
					continue;
				}
				foreach (Renderer r in mover.root.GetComponentsInChildren<Renderer>())
				{
					Follow(mover, r, null);
				}
				// Renderers that turned up on the mover after the level started (food,
				// flames, a chef's held item) but aren't parented to it.
				if (all == null)
				{
					all = Object.FindObjectsOfType<Renderer>();
				}
				Bounds area = MoverBounds(mover);
				area.Expand(0.5f);
				foreach (Renderer r in all)
				{
					if (r != null && !m_initial.Contains(r.GetInstanceID()) && MoverOf(r.transform) == "none"
						&& (mover.local.ContainsKey(r.GetInstanceID()) || area.Contains(r.bounds.center)))
					{
						Follow(mover, r, "not-parented");
					}
				}
			}
		}

		private void Follow(Mover _mover, Renderer _r, string _why)
		{
			int id = _r.GetInstanceID();
			if (!_r.enabled || _r is ParticleSystemRenderer || m_reported.Contains(id) || _r.GetComponentInParent<DynamicLandscapeParenting>() != null)
			{
				return;
			}
			Transform root = _mover.root;
			Vector3 nowT = _r.transform.position, nowB = _r.bounds.center;
			Vector3[] last;
			bool known = _mover.local.TryGetValue(id, out last);
			_mover.local[id] = new Vector3[] { root.InverseTransformPoint(nowT), root.InverseTransformPoint(nowB), nowT, nowB };
			if (!known)
			{
				return;
			}
			// Where it would be now had it moved with the mover since the last check.
			float shouldT = (root.TransformPoint(last[0]) - last[2]).magnitude, shouldB = (root.TransformPoint(last[1]) - last[3]).magnitude;
			float dT = (nowT - last[2]).magnitude, dB = (nowB - last[3]).magnitude;
			if (_why == null)
			{
				if (shouldT > 0.1f && dT < 0.02f)
				{
					_why = "transform-stays";
				}
				else if (shouldB > 0.1f && dT > 0.1f && dB < 0.02f)
				{
					_why = "bounds-stay";
				}
			}
			else if (!(shouldB > 0.1f && dB < 0.02f))
			{
				_why = null;
			}
			if (_why == null)
			{
				return;
			}
			m_reported.Add(id);
			Log("left-behind scene=" + m_scene + " why=" + _why + " renderer=" + PathOf(_r.transform) + " mover=" + _mover.path + " staticBatch=" + StaticBatchOf(_r)
				+ " should=" + shouldB.ToString("F2") + " dT=" + dT.ToString("F2") + " dB=" + dB.ToString("F2") + " chain=" + ChainOf(_r.transform, null));
		}

		private static float SizeOf(Transform _t)
		{
			Mover probe = new Mover();
			probe.root = _t;
			Vector3 size = MoverBounds(probe).size;
			return Mathf.Max(size.x, size.z);
		}

		private static Bounds MoverBounds(Mover _mover)
		{
			Bounds b = new Bounds(_mover.root.position, Vector3.zero);
			foreach (Collider c in _mover.root.GetComponentsInChildren<Collider>())
			{
				if (!c.isTrigger)
				{
					b.Encapsulate(c.bounds);
				}
			}
			return b;
		}

		private void LateUpdate()
		{
			if (m_sync)
			{
#if UNITY_2017_2_OR_NEWER
				Physics.SyncTransforms();
#endif
			}
			foreach (Chef chef in m_chefs)
			{
				if (chef.rider == null)
				{
					continue;
				}
				Transform t = chef.rider.transform;
				if (t.parent != chef.parent)
				{
					chef.parent = t.parent;
					Log("ride-parent scene=" + m_scene + " chef=" + t.name + " ground=" + (t.parent != null ? PathOf(t.parent) : "none")
						+ " mover=" + MoverOf(t) + " pos=" + t.position.ToString("F2") + " lossyScale=" + t.lossyScale.ToString("F2"));
				}
				string reasons = VanishReasons(chef);
				if (reasons != chef.reasons)
				{
					chef.reasons = reasons;
					Rigidbody body = chef.rider.GetComponent<Rigidbody>();
					Log("vanish scene=" + m_scene + " chef=" + t.name + " reasons=" + (reasons.Length > 0 ? reasons : "none (drawn again)")
						+ " pos=" + t.position.ToString("F2") + " lossyScale=" + t.lossyScale.ToString("F2") + " ground=" + (t.parent != null ? PathOf(t.parent) : "none")
						+ " mover=" + MoverOf(t) + " body=" + (body != null ? "kinematic=" + body.isKinematic + " interp=" + body.interpolation + " vel=" + body.velocity.ToString("F1") : "none")
						+ " renderers=" + RendererStates(chef) + " chain=" + ChainOf(t, null));
				}
			}
		}

		private static string VanishReasons(Chef _chef)
		{
			Transform t = _chef.rider.transform;
			List<string> reasons = new List<string>();
			if (!_chef.rider.gameObject.activeInHierarchy)
			{
				reasons.Add("inactive");
			}
			Vector3 p = t.position, s = t.lossyScale;
			if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z))
			{
				reasons.Add("position-nan");
			}
			if (Mathf.Min(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)) < 0.05f || Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z)) > 20f)
			{
				reasons.Add("scale");
			}
			if (p.y < _chef.startY - 3f)
			{
				reasons.Add("fell");
			}
			int enabled = 0, visible = 0;
			int layers = 0;
			foreach (Renderer r in _chef.renderers)
			{
				if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r is ParticleSystemRenderer)
				{
					continue;
				}
				enabled++;
				layers |= 1 << r.gameObject.layer;
				if (r.isVisible)
				{
					visible++;
				}
			}
			if (_chef.rider.gameObject.activeInHierarchy && enabled == 0)
			{
				reasons.Add("renderers-off");
			}
			Camera cam = Camera.main;
			if (cam != null)
			{
				Vector3 v = cam.WorldToViewportPoint(p);
				bool onScreen = v.z > 0f && v.x > 0f && v.x < 1f && v.y > 0f && v.y < 1f;
				if (!onScreen)
				{
					reasons.Add("off-screen");
				}
				else if (enabled > 0 && visible == 0)
				{
					reasons.Add("culled-on-screen");
				}
				if (enabled > 0 && (cam.cullingMask & layers) == 0)
				{
					reasons.Add("layer-not-drawn");
				}
			}
			return string.Join(",", reasons.ToArray());
		}

		private static string RendererStates(Chef _chef)
		{
			System.Text.StringBuilder s = new System.Text.StringBuilder();
			int shown = 0;
			foreach (Renderer r in _chef.renderers)
			{
				if (r == null || r is ParticleSystemRenderer || shown++ >= 3)
				{
					continue;
				}
				s.Append(r.name + "(en=" + r.enabled + " act=" + r.gameObject.activeInHierarchy + " vis=" + r.isVisible + " layer=" + r.gameObject.layer
					+ " bounds=" + r.bounds.center.ToString("F1") + "/" + r.bounds.size.ToString("F1") + ") ");
			}
			return s.ToString().TrimEnd();
		}

		private string MoverOf(Transform _t)
		{
			foreach (Mover mover in m_movers)
			{
				if (mover.root != null && _t.IsChildOf(mover.root))
				{
					return mover.path;
				}
			}
			return "none";
		}

		// Test placement: the first chef onto the top of a moving floor.
		private void PlaceChef(Mover _mover)
		{
			if (m_chefs.Count == 0 || m_chefs[0].rider == null || _mover.root == null || !_mover.walkable)
			{
				return;
			}
			Bounds b = _mover.bounds;
			RaycastHit hit;
			Vector3 from = new Vector3(b.center.x, b.max.y + 5f, b.center.z);
			foreach (Collider c in _mover.root.GetComponentsInChildren<Collider>())
			{
				Vector3 size = c.bounds.size;
				if (c.isTrigger || size.x < 0.8f || size.z < 0.8f)
				{
					continue;
				}
				from = new Vector3(c.bounds.center.x, c.bounds.max.y + 5f, c.bounds.center.z);
				if (c.Raycast(new Ray(from, Vector3.down), out hit, 20f))
				{
					Transform chef = m_chefs[0].rider.transform;
					Rigidbody body = chef.GetComponent<Rigidbody>();
					Vector3 to = hit.point + Vector3.up * 0.1f;
					chef.position = to;
					if (body != null)
					{
						body.position = to;
						body.velocity = Vector3.zero;
					}
					Log("ridetest scene=" + m_scene + " chef=" + chef.name + " placed on " + PathOf(c.transform) + " at " + to.ToString("F2"));
					return;
				}
			}
		}

		private static string DriverOf(Transform _t)
		{
			List<string> parts = new List<string>();
			for (Transform p = _t; p != null; p = p.parent)
			{
				Animator animator = p.GetComponent<Animator>();
				if (animator != null)
				{
					parts.Add("Animator@" + p.name + "(" + (animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "none")
						+ " update=" + animator.updateMode + " root=" + animator.applyRootMotion + ")");
				}
				if (p.GetComponent<Animation>() != null)
				{
					parts.Add("Animation@" + p.name);
				}
				Rigidbody body = p.GetComponent<Rigidbody>();
				if (body != null)
				{
					parts.Add("Rigidbody@" + p.name + "(kinematic=" + body.isKinematic + " interp=" + body.interpolation + ")");
				}
				if (p == _t)
				{
					foreach (MonoBehaviour b in p.GetComponents<MonoBehaviour>())
					{
						if (b != null)
						{
							parts.Add(b.GetType().Name);
						}
					}
				}
			}
			return parts.Count > 0 ? string.Join(";", parts.ToArray()) : "none";
		}

		// From the transform up to _stop (or the scene root): name, local position and
		// what could drive that link.
		private static string ChainOf(Transform _t, Transform _stop)
		{
			System.Text.StringBuilder s = new System.Text.StringBuilder();
			for (Transform p = _t; p != null; p = p.parent)
			{
				s.Append(p.name + p.localPosition.ToString("F2"));
				if (p.GetComponent<Animator>() != null)
				{
					s.Append("[Animator]");
				}
				if (p.GetComponent<Animation>() != null)
				{
					s.Append("[Animation]");
				}
				Rigidbody body = p.GetComponent<Rigidbody>();
				if (body != null)
				{
					s.Append("[Rigidbody kin=" + body.isKinematic + " interp=" + body.interpolation + "]");
				}
				if (p == _stop)
				{
					break;
				}
				s.Append(" < ");
			}
			return s.ToString();
		}

		private static string StaticBatchOf(Renderer _r)
		{
#if UNITY_5_6_OR_NEWER
			return _r.isPartOfStaticBatch.ToString();
#else
			return "n/a";
#endif
		}

		private static string PathOf(Transform t)
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
