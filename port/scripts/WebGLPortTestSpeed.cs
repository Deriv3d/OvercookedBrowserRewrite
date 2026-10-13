// Test-only fast-forward for headless checks (each check spent about
// 2 minutes reaching a level).
// Inactive unless the page address has ?fast=N (2-8): then everything outside
// a level's round (title, book, cutscenes, intro dialogue, level start) runs N
// times faster, and each round runs at normal speed so timings measured there
// (chop rate, popups) stay true. Uses the game's own time multiplier
// (GameDebugManager, which rewrites Time.timeScale every frame).

using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

public class WebGLPortTestSpeed : MonoBehaviour
{
	private float m_fast;
	private float m_current = -1f;
	private FlowControllerBase m_flow;
	private GameDebugManager m_debug;
	private static readonly FieldInfo s_timeMod = typeof(GameDebugManager).GetField("m_manualTimeMod", BindingFlags.Instance | BindingFlags.NonPublic);

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void Init()
	{
		float fast = ParseFast(Application.absoluteURL);
		if (fast <= 1f)
		{
			return;
		}
		GameObject host = new GameObject("WebGLPortTestSpeed");
		DontDestroyOnLoad(host);
		host.AddComponent<WebGLPortTestSpeed>().m_fast = fast;
		Debug.Log("[webgl-port] test speed: ?fast=" + fast + " (outside rounds only)");
	}

	private static float ParseFast(string _url)
	{
		int query = string.IsNullOrEmpty(_url) ? -1 : _url.IndexOf('?');
		if (query < 0)
		{
			return 0f;
		}
		foreach (string pair in _url.Substring(query + 1).Split('&', '#'))
		{
			float value;
			if (pair.StartsWith("fast=") && float.TryParse(pair.Substring(5), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
			{
				return Mathf.Clamp(value, 1f, 8f);
			}
		}
		return 0f;
	}

	private void OnEnable()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void OnDisable()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}

	private void OnSceneLoaded(Scene _scene, LoadSceneMode _mode)
	{
		m_flow = null;
	}

	private void Update()
	{
		if (m_flow == null)
		{
			m_flow = FindObjectOfType<FlowControllerBase>();
		}
		if (m_debug == null)
		{
			m_debug = FindObjectOfType<GameDebugManager>();
		}
		float want = m_flow != null && m_flow.InRound ? 1f : m_fast;
		if (m_debug != null && s_timeMod != null)
		{
			s_timeMod.SetValue(m_debug, want);
		}
		else if (Time.timeScale != 0f)
		{
			Time.timeScale = want;
		}
		if (want != m_current)
		{
			m_current = want;
			Debug.Log("[webgl-port] test speed x" + want + " scene=" + SceneManager.GetActiveScene().name + " inRound=" + (m_flow != null && m_flow.InRound) + " via=" + (m_debug != null && s_timeMod != null ? "GameDebugManager" : "Time.timeScale"));
		}
	}

	private void OnDestroy()
	{
		if (m_debug != null && s_timeMod != null)
		{
			s_timeMod.SetValue(m_debug, 1f);
		}
	}
}
