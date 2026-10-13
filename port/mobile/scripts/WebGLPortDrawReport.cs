// What a scene draws, for cutting draw calls on phones (port/mobile/README.md,
// "Finding the slow part"). The world map is the heaviest scene (about 1330
// draws, 480 SetPass on desktop), and the phone dies loading it.
//
// ?drawreport=1 logs, 1 s and 5 s after each scene load (the world map
// animates in), the following lines:
//   [webgl-port] draws scene=... t=... renderers, visible ones, their
//                submeshes (draws before batching), static-batched, skinned,
//                particle systems, shadow casters, lights, materials, shaders
//   [webgl-port] draws-camera ...  one per enabled camera: path, depth,
//                culling mask, render texture (extra cameras redraw the scene)
//   [webgl-port] draws-shader ...  visible submeshes per shader, top 12
//   [webgl-port] draws-root ...    visible submeshes per top-level object, top 12
//   [webgl-port] draws-pairs ...   visible mesh+material pairs: how many are
//                distinct, how many have instancing on, top 12 by count (the
//                same mesh and material many times is what instancing batches)
//   [webgl-port] draws-light ...   one per enabled light: type, render mode
//                (Important lights are per pixel whatever the limit, one
//                extra pass per object they touch), shadows, range
//   [webgl-port] memory ...        (5 s only) everything loaded, not just this
//                scene: textures and render textures (GPU memory, estimated from
//                size and format), meshes, and audio clips with the size Safari
//                holds once decoded (Web Audio keeps clips as 32-bit PCM outside
//                the heap, so music the heap counters never show)
//   [webgl-port] memory-top ...    the 8 biggest textures and audio clips
//   [webgl-port] memory-audio ...  the 10 biggest loaded clips with load type,
//                length and whether their data is loaded, and the compressed ones
// Reading only: it changes nothing. It allocates when it reports, never per frame.

using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public class WebGLPortDrawReport : MonoBehaviour
{
	private const int Top = 12;

#if UNITY_WEBGL && !UNITY_EDITOR
	[DllImport("__Internal")] private static extern int OvercookedPhone_QueryInt(string name, int fallback);
#endif

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		if (OvercookedPhone_QueryInt("drawreport", 0) != 1)
		{
			return;
		}
		GameObject host = new GameObject("WebGLPortDrawReport");
		DontDestroyOnLoad(host);
		host.AddComponent<WebGLPortDrawReport>();
#endif
	}

	private void OnEnable()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private void OnDisable()
	{
		SceneManager.sceneLoaded -= OnSceneLoaded;
	}

	private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		StartCoroutine(ReportAfter(scene.name));
	}

	private IEnumerator ReportAfter(string scene)
	{
		yield return new WaitForSecondsRealtime(1f);
		Report(scene, 1);
		yield return new WaitForSecondsRealtime(4f);
		Report(scene, 5);
		MemoryReport(scene);
	}

	private static void MemoryReport(string scene)
	{
		long texBytes = 0, rtBytes = 0, meshBytes = 0, audioBytes = 0;
		int textures = 0, rts = 0, meshes = 0, clips = 0, compressedClips = 0, unloadedClips = 0;
		List<KeyValuePair<string, long>> big = new List<KeyValuePair<string, long>>();
		foreach (Texture t in Resources.FindObjectsOfTypeAll<Texture>())
		{
			long bytes = TextureBytes(t);
			if (t is RenderTexture) { rts++; rtBytes += bytes; }
			else { textures++; texBytes += bytes; }
			big.Add(new KeyValuePair<string, long>("tex:" + t.name + "(" + t.width + "x" + t.height + " " + t.graphicsFormat + ")", bytes));
		}
		foreach (Mesh m in Resources.FindObjectsOfTypeAll<Mesh>())
		{
			meshes++;
			long bytes = 0;
			for (int s = 0; s < m.vertexBufferCount; s++)
			{
				bytes += (long)m.vertexCount * m.GetVertexBufferStride(s);
			}
			long indices = 0;
			for (int s = 0; s < m.subMeshCount; s++)
			{
				indices += m.GetIndexCount(s);
			}
			meshBytes += bytes + indices * (m.indexFormat == UnityEngine.Rendering.IndexFormat.UInt32 ? 4 : 2);
		}
		List<KeyValuePair<string, long>> audio = new List<KeyValuePair<string, long>>();
		StringBuilder compressed = new StringBuilder();
		foreach (AudioClip c in Resources.FindObjectsOfTypeAll<AudioClip>())
		{
			clips++;
			audio.Add(new KeyValuePair<string, long>(c.name + "(" + c.length.ToString("0") + "s " + c.loadType + " " + c.loadState + ")",
				(long)c.samples * c.channels * 4));
			if (c.loadType != AudioClipLoadType.DecompressOnLoad)
			{
				if (compressed.Length > 0) compressed.Append(", ");
				compressed.Append(c.name);
			}
			if (c.loadType != AudioClipLoadType.DecompressOnLoad)
			{
				// Played from a media element, kept compressed; not decoded.
				compressedClips++;
				continue;
			}
			if (c.loadState != AudioDataLoadState.Loaded)
			{
				// Clip object only; its sound data isn't in memory.
				unloadedClips++;
				continue;
			}
			long bytes = (long)c.samples * c.channels * 4;
			audioBytes += bytes;
			big.Add(new KeyValuePair<string, long>("audio:" + c.name + "(" + c.length.ToString("0") + "s " + c.loadType + ")", bytes));
		}
		Debug.Log("[webgl-port] memory scene=" + scene + " textures=" + textures + " texMB=" + MB(texBytes)
			+ " renderTextures=" + rts + " rtMB=" + MB(rtBytes) + " meshes=" + meshes + " meshMB=" + MB(meshBytes)
			+ " clips=" + clips + " compressedClips=" + compressedClips + " unloadedClips=" + unloadedClips + " audioDecodedMB=" + MB(audioBytes));
		big.Sort((a, b) => b.Value.CompareTo(a.Value));
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < big.Count && i < 8; i++)
		{
			if (i > 0) sb.Append(", ");
			sb.Append(big[i].Key).Append('=').Append(MB(big[i].Value)).Append("MB");
		}
		Debug.Log("[webgl-port] memory-top scene=" + scene + " " + sb);
		audio.Sort((a, b) => b.Value.CompareTo(a.Value));
		sb.Length = 0;
		for (int i = 0; i < audio.Count && i < 10; i++)
		{
			if (i > 0) sb.Append(", ");
			sb.Append(audio[i].Key).Append('=').Append(MB(audio[i].Value)).Append("MB");
		}
		Debug.Log("[webgl-port] memory-audio scene=" + scene + " " + sb + " | compressed: " + (compressed.Length > 0 ? compressed.ToString() : "none"));
	}

	// Estimate: block size of the format, all mips, six faces for cubemaps.
	// Formats with no colour data (GraphicsFormat.None: depth-only render
	// textures, such as the deferred path's) report a block size of 0; they are
	// counted as 4 bytes per pixel. Integer division by zero traps in WebGL
	// (wasm "Division by zero", a phone test run), so never divide by them.
	private static long TextureBytes(Texture t)
	{
		UnityEngine.Experimental.Rendering.GraphicsFormat f = t.graphicsFormat;
		if (f == UnityEngine.Experimental.Rendering.GraphicsFormat.None)
		{
			return (long)t.width * t.height * 4;
		}
		long bw = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockWidth(f);
		long bh = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockHeight(f);
		long bs = UnityEngine.Experimental.Rendering.GraphicsFormatUtility.GetBlockSize(f);
		if (bw <= 0 || bh <= 0)
		{
			return (long)t.width * t.height * 4;
		}
		long bytes = ((t.width + bw - 1) / bw) * ((t.height + bh - 1) / bh) * bs;
		if (t.mipmapCount > 1) bytes = bytes * 4 / 3;
		if (t.dimension == UnityEngine.Rendering.TextureDimension.Cube) bytes *= 6;
		RenderTexture rt = t as RenderTexture;
		if (rt != null && rt.depth > 0) bytes += (long)t.width * t.height * 4;
		return bytes;
	}

	private static string MB(long bytes)
	{
		return (bytes / 1048576f).ToString("0.0");
	}

	private static void Report(string scene, int seconds)
	{
		Renderer[] renderers = FindObjectsOfType<Renderer>();
		int visible = 0, submeshes = 0, staticBatched = 0, skinned = 0, particles = 0, casters = 0;
		HashSet<Material> materials = new HashSet<Material>();
		Dictionary<string, int> byShader = new Dictionary<string, int>();
		Dictionary<string, int> byRoot = new Dictionary<string, int>();
		Dictionary<string, int> byPair = new Dictionary<string, int>();
		int instanced = 0;
		foreach (Renderer r in renderers)
		{
			if (!r.enabled || !r.gameObject.activeInHierarchy)
			{
				continue;
			}
			if (r is SkinnedMeshRenderer) skinned++;
			if (r is ParticleSystemRenderer) particles++;
			if (r.isPartOfStaticBatch) staticBatched++;
			if (r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off) casters++;
			if (!r.isVisible)
			{
				continue;
			}
			visible++;
			Material[] mats = r.sharedMaterials;
			submeshes += mats.Length;
			string root = r.transform.root.name;
			Add(byRoot, root, mats.Length);
			foreach (Material m in mats)
			{
				if (m == null)
				{
					continue;
				}
				materials.Add(m);
				Add(byShader, m.shader != null ? m.shader.name : "(none)", 1);
				if (m.enableInstancing) instanced++;
				MeshFilter mf = r.GetComponent<MeshFilter>();
				if (mf != null && mf.sharedMesh != null)
				{
					Add(byPair, mf.sharedMesh.name + "|" + m.name, 1);
				}
			}
		}
		HashSet<Shader> shaders = new HashSet<Shader>();
		foreach (Material m in materials)
		{
			if (m.shader != null) shaders.Add(m.shader);
		}

		Debug.Log("[webgl-port] draws scene=" + scene + " t=" + seconds + "s renderers=" + renderers.Length + " visible=" + visible
			+ " submeshes=" + submeshes + " static=" + staticBatched + " skinned=" + skinned + " particles=" + particles
			+ " casters=" + casters + " lights=" + FindObjectsOfType<Light>().Length + " materials=" + materials.Count
			+ " shaders=" + shaders.Count + " shadows=" + QualitySettings.shadows + " pixelLights=" + QualitySettings.pixelLightCount);

		foreach (Camera cam in Camera.allCameras)
		{
			Debug.Log("[webgl-port] draws-camera scene=" + scene + " name=" + cam.name + " depth=" + cam.depth
				+ " path=" + cam.actualRenderingPath + " mask=0x" + cam.cullingMask.ToString("X")
				+ " rt=" + (cam.targetTexture != null ? cam.targetTexture.width + "x" + cam.targetTexture.height : "screen")
				+ " depthTexture=" + cam.depthTextureMode + " clear=" + cam.clearFlags + " components=" + ComponentsOf(cam));
		}
		foreach (Light l in FindObjectsOfType<Light>())
		{
			if (!l.enabled || !l.gameObject.activeInHierarchy) continue;
			Debug.Log("[webgl-port] draws-light scene=" + scene + " name=" + l.name + " type=" + l.type + " mode=" + l.renderMode
				+ " shadows=" + l.shadows + " range=" + l.range.ToString("0.#") + " intensity=" + l.intensity.ToString("0.##")
				+ " mask=0x" + l.cullingMask.ToString("X"));
		}
		Debug.Log("[webgl-port] draws-pairs scene=" + scene + " t=" + seconds + "s distinct=" + byPair.Count + " instanced=" + instanced
			+ " " + TopOf(byPair));
		Debug.Log("[webgl-port] draws-shader scene=" + scene + " t=" + seconds + "s " + TopOf(byShader));
		Debug.Log("[webgl-port] draws-root scene=" + scene + " t=" + seconds + "s " + TopOf(byRoot));
	}

	private static string ComponentsOf(Camera cam)
	{
		StringBuilder sb = new StringBuilder();
		foreach (Behaviour b in cam.GetComponents<Behaviour>())
		{
			if (b == null || b is Camera || !b.enabled) continue;
			if (sb.Length > 0) sb.Append('+');
			sb.Append(b.GetType().Name);
		}
		return sb.Length > 0 ? sb.ToString() : "none";
	}

	private static void Add(Dictionary<string, int> counts, string key, int n)
	{
		int c;
		counts.TryGetValue(key, out c);
		counts[key] = c + n;
	}

	private static string TopOf(Dictionary<string, int> counts)
	{
		List<KeyValuePair<string, int>> list = new List<KeyValuePair<string, int>>(counts);
		list.Sort((a, b) => b.Value.CompareTo(a.Value));
		StringBuilder sb = new StringBuilder();
		for (int i = 0; i < list.Count && i < Top; i++)
		{
			if (i > 0) sb.Append(", ");
			sb.Append(list[i].Key).Append('=').Append(list[i].Value);
		}
		if (list.Count > Top)
		{
			sb.Append(", +").Append(list.Count - Top).Append(" more");
		}
		return sb.ToString();
	}
}
