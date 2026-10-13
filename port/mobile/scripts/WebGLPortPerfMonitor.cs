// Perf monitor for the optimisation framework (port/mobile/README.md).
// Measures only; it changes nothing in the game. Started by WebGLPortPhone
// on phones or with ?perf=1.
//
// Every second it logs one line:
//   [webgl-port] perf scene=<name> fps=<avg> worstMs=<slowest frame>
//     heapMB=<wasm heap> usedMB=<Unity used> draws=<max> batches=<max>
//     setpass=<max> tris=<max> gcKB=<allocated> allocFrames=<n>/<frames>
//     hitches=<frames over 25 ms> lowFor=<seconds in a row under 55 fps>
//     over=<budgets exceeded: heap, draws, fps, hitch, sustained>
// Each frame over HitchLogMs also gets its own line at once:
//   [webgl-port] hitch scene=<name> ms=<frame> at=<seconds into the scene>
//     gcRun=<a garbage collection ran> gcKB=<allocated that frame>
//     setpass=<before>-><that frame> draws=<before>-><that frame> heapMB=<before>-><after>
// so a stutter can be matched to what caused it: a collection, an allocation
// burst, or new materials (SetPass jump: shaders compiled on first use).
// When a scene is replaced it logs a "perf-summary" line with the peaks of the
// scene that just ended. Counters a release player doesn't record show -1;
// a Development build has them all.
//
// Update() allocates nothing; the log line is built once a second.
// ProfilerRecorder needs Unity 2020.2+; older reference DLLs
// get a version where every counter reads -1.

using System.Text;
#if UNITY_2020_2_OR_NEWER
using Unity.Profiling;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;

public class WebGLPortPerfMonitor : MonoBehaviour
{
#if UNITY_2020_2_OR_NEWER
	private ProfilerRecorder _draws, _batches, _setPass, _tris, _gcAlloc, _used;
#else
	private readonly object _draws = null, _batches = null, _setPass = null, _tris = null, _gcAlloc = null, _used = null;
#endif

	// Current one-second window.
	private int _frames, _allocFrames, _hitches, _lowStreak;
	private float _time, _worst;
	private long _maxDraws = -1, _maxBatches = -1, _maxSetPass = -1, _maxTris = -1, _gcBytes;

	// Peaks for the whole scene.
	private string _scene = "";
	private float _sceneTime, _sceneWorst;
	private int _sceneFrames, _sceneAllocFrames, _sceneHeapPeak, _sceneHitches, _sceneLowStreakMax;
	private long _sceneDrawPeak, _sceneGcBytes;

	private readonly StringBuilder _line = new StringBuilder(256);

	// Frames slower than this get their own "hitch" line (a visible stutter at 60 FPS).
	private const float HitchLogMs = 50f;
	private int _gcCount;
	private long _prevSetPass = -1, _prevDraws = -1;
	private int _prevHeap = -1;

	private void OnEnable()
	{
#if UNITY_2020_2_OR_NEWER
		_draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
		_batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
		_setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
		_tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
		_gcAlloc = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
		_used = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
#endif
		_scene = SceneManager.GetActiveScene().name;
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void OnDisable()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
#if UNITY_2020_2_OR_NEWER
		_draws.Dispose();
		_batches.Dispose();
		_setPass.Dispose();
		_tris.Dispose();
		_gcAlloc.Dispose();
		_used.Dispose();
#endif
	}

#if UNITY_2020_2_OR_NEWER
	private static long Last(ProfilerRecorder r)
	{
		return r.Valid ? r.LastValue : -1;
	}
#else
	private static long Last(object r)
	{
		return -1;
	}
#endif

	private void Update()
	{
		float dt = Time.unscaledDeltaTime;
		_frames++;
		_time += dt;
		if (dt > _worst)
		{
			_worst = dt;
		}
		if (dt * 1000f > WebGLPortPhone.HitchMs)
		{
			_hitches++;
		}
		_maxDraws = System.Math.Max(_maxDraws, Last(_draws));
		_maxBatches = System.Math.Max(_maxBatches, Last(_batches));
		_maxSetPass = System.Math.Max(_maxSetPass, Last(_setPass));
		_maxTris = System.Math.Max(_maxTris, Last(_tris));
		long gc = Last(_gcAlloc);
		int gcCount = System.GC.CollectionCount(0);
		long setPass = Last(_setPass), draws = Last(_draws);
		if (dt * 1000f > HitchLogMs)
		{
			LogHitch(dt, gcCount != _gcCount, gc, setPass, draws);
		}
		_gcCount = gcCount;
		_prevSetPass = setPass;
		_prevDraws = draws;
		if (gc > 0)
		{
			_gcBytes += gc;
			_allocFrames++;
		}
		else if (gc < 0)
		{
			_gcBytes = -1;
		}

		if (_time >= 1f)
		{
			Flush();
		}
	}

	// Only on frames that already stuttered, so its allocation costs nothing visible.
	private void LogHitch(float dt, bool gcRan, long gcBytes, long setPass, long draws)
	{
		int heap = WebGLPortPhone.HeapMB();
		Debug.Log("[webgl-port] hitch scene=" + _scene + " ms=" + (dt * 1000f).ToString("F0")
			+ " at=" + (_sceneTime + _time).ToString("F1") + " gcRun=" + (gcRan ? "yes" : "no")
			+ " gcKB=" + (gcBytes < 0 ? "-1" : (gcBytes / 1024f).ToString("F0"))
			+ " setpass=" + _prevSetPass + "->" + setPass + " draws=" + _prevDraws + "->" + draws
			+ " heapMB=" + _prevHeap + "->" + heap);
		_prevHeap = heap;
	}

	private void Flush()
	{
		int heap = WebGLPortPhone.HeapMB();
		_prevHeap = heap;
		long used = Last(_used);
		float fps = _frames / _time;
		_lowStreak = fps < WebGLPortPhone.MinSteadyFps ? _lowStreak + 1 : 0;
		_sceneHitches += _hitches;
		_sceneLowStreakMax = Mathf.Max(_sceneLowStreakMax, _lowStreak);

		_sceneTime += _time;
		_sceneFrames += _frames;
		_sceneAllocFrames += _allocFrames;
		_sceneWorst = Mathf.Max(_sceneWorst, _worst);
		_sceneHeapPeak = Mathf.Max(_sceneHeapPeak, heap);
		_sceneDrawPeak = System.Math.Max(_sceneDrawPeak, _maxDraws);
		_sceneGcBytes = _gcBytes < 0 || _sceneGcBytes < 0 ? -1 : _sceneGcBytes + _gcBytes;

		_line.Length = 0;
		_line.Append("[webgl-port] perf scene=").Append(_scene)
			.Append(" fps=").Append(fps.ToString("F1"))
			.Append(" worstMs=").Append((_worst * 1000f).ToString("F1"))
			.Append(" heapMB=").Append(heap)
			.Append(" usedMB=").Append(used < 0 ? -1 : used / (1024 * 1024))
			.Append(" draws=").Append(_maxDraws)
			.Append(" batches=").Append(_maxBatches)
			.Append(" setpass=").Append(_maxSetPass)
			.Append(" tris=").Append(_maxTris)
			.Append(" gcKB=").Append(_gcBytes < 0 ? "-1" : (_gcBytes / 1024f).ToString("F1"))
			.Append(" allocFrames=").Append(_allocFrames).Append('/').Append(_frames)
			.Append(" hitches=").Append(_hitches)
			.Append(" lowFor=").Append(_lowStreak);
		AppendOver(heap, _maxDraws, fps);
		string text = _line.ToString();
		Debug.Log(text);
		if (WebGLPortPhone.PerfLevel >= 2)
		{
			WebGLPortPhone.SetOverlay(text.Substring("[webgl-port] perf ".Length).Replace(" ", "\n"));
		}

		_frames = 0;
		_allocFrames = 0;
		_hitches = 0;
		_time = 0f;
		_worst = 0f;
		_maxDraws = _maxBatches = _maxSetPass = _maxTris = -1;
		_gcBytes = 0;
	}

	private void AppendOver(int heapMB, long draws, float fps)
	{
		int start = _line.Length;
		AppendFlag(start, heapMB > WebGLPortPhone.HeapBudgetMB, "heap");
		AppendFlag(start, draws > WebGLPortPhone.DrawCallBudget, "draws");
		AppendFlag(start, fps < WebGLPortPhone.MinSteadyFps, "fps");
		AppendFlag(start, _hitches > 0, "hitch");
		AppendFlag(start, _lowStreak >= WebGLPortPhone.SustainedLowSeconds, "sustained");
	}

	private void AppendFlag(int start, bool on, string name)
	{
		if (!on)
		{
			return;
		}
		_line.Append(_line.Length == start ? " over=" : ",").Append(name);
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (mode != LoadSceneMode.Single)
		{
			return;
		}
		if (_sceneFrames > 0)
		{
			Debug.Log("[webgl-port] perf-summary scene=" + _scene
				+ " seconds=" + _sceneTime.ToString("F0")
				+ " fps=" + (_sceneFrames / _sceneTime).ToString("F1")
				+ " worstMs=" + (_sceneWorst * 1000f).ToString("F1")
				+ " heapPeakMB=" + _sceneHeapPeak
				+ " drawPeak=" + _sceneDrawPeak
				+ " gcKB=" + (_sceneGcBytes < 0 ? "-1" : (_sceneGcBytes / 1024f).ToString("F0"))
				+ " allocFrames=" + _sceneAllocFrames + "/" + _sceneFrames
				+ " hitches=" + _sceneHitches
				+ " longestLowStreak=" + _sceneLowStreakMax);
		}
		_scene = scene.name;
		_sceneTime = 0f;
		_sceneWorst = 0f;
		_sceneFrames = 0;
		_sceneAllocFrames = 0;
		_sceneHeapPeak = 0;
		_sceneDrawPeak = 0;
		_sceneGcBytes = 0;
		_sceneHitches = 0;
		_sceneLowStreakMax = 0;
		_lowStreak = 0;
	}
}
