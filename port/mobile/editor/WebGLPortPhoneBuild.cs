// Phone build for the optimisation framework (port/mobile/README.md).
// Menu "Overcooked Port > Build WebGL (Phone)", or from the command line:
//   Unity.exe -batchmode -quit -projectPath <project> -executeMethod WebGLPortPhoneBuild.BuildFromCommandLine
//
// Same game and scenes as the desktop build, written to Builds/WebGL-Phone with
// the phone page template and phone limits (ASTC textures, a capped heap, size-
// optimised code, no lightmap shader variants). Every setting it changes is put
// back afterwards, so the next desktop build is untouched.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;

public class WebGLPortPhoneBuild : IPreprocessShaders, IPostprocessBuildWithReport
{
	public const string OutputDir = "Builds/WebGL-Phone";

	// Heap limits in MB. 512 while the whole game is one data file (the first
	// phone measurements need room); 384 once levels load one at a time.
	// The working budget is 350 (WebGLPortPhone.HeapBudgetMB).
	// Initial 256: the phone heap starts at ~153 MB and grew to 216-217 on
	// every rooftop load, and the iPhone died during those loads (phone test
	// runs). Growing a wasm heap means Safari holds the old and the new
	// buffer at once, at the worst moment. Starting above the measured peak
	// means no growth on the scenes measured so far; untouched heap pages
	// aren't backed by memory, so the higher start costs little.
	public const int InitialMemoryMB = 256;
	public const int MaximumMemoryMB = 512;

	// Off until a side-by-side check confirms no difference: drops vertex channels that
	// no shader in the build reads, which would break a script that reads them.
	public const bool OptimizeMeshData = false;

	// Clips at least this long load on first play on phones (see
	// KeepLongAudioCompressed). The phone logs showed every clip DecompressOnLoad:
	// Web Audio holds them as 32-bit PCM outside the heap, 241 MB on the rooftop
	// (intro music 81 MB, menu music 49 MB, fire loop 22 MB).
	public const float CompressedAudioSeconds = 20f;

	private static readonly List<Action> s_restore = new List<Action>();

	[MenuItem("Overcooked Port/Build WebGL (Phone)")]
	public static void Build()
	{
		WebGLPortBuild.ApplySettings();
		try
		{
			ApplyPhoneSettings();
			string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
			BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
			{
				scenes = scenes,
				locationPathName = OutputDir,
				target = BuildTarget.WebGL,
				options = BuildOptions.None,
			});
			Debug.Log($"[webgl-port] Phone build {report.summary.result}: {report.summary.totalSize / (1024 * 1024)} MB, " +
			          $"{report.summary.totalErrors} errors, {report.summary.totalTime}");
			if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded)
			{
				RestoreSettings();
				EditorApplication.Exit(1);
			}
		}
		finally
		{
			RestoreSettings();
		}
	}

	public static void BuildFromCommandLine()
	{
		Build();
	}

	// Only called by Build(), which always puts the settings back.
	private static void ApplyPhoneSettings()
	{
		s_restore.Clear();

		// iPhones can't sample DXT; Safari would unpack every texture to raw RGBA.
		Change("texture subtarget", () => EditorUserBuildSettings.webGLBuildSubtarget,
			v => EditorUserBuildSettings.webGLBuildSubtarget = v, WebGLTextureSubtarget.ASTC);

		Change("template", () => PlayerSettings.WebGL.template, v => PlayerSettings.WebGL.template = v, "PROJECT:OvercookedPhone");
		Change("initialMemorySize", () => PlayerSettings.WebGL.initialMemorySize, v => PlayerSettings.WebGL.initialMemorySize = v, InitialMemoryMB);
		Change("maximumMemorySize", () => PlayerSettings.WebGL.maximumMemorySize, v => PlayerSettings.WebGL.maximumMemorySize = v, MaximumMemoryMB);
		// Small growth steps: while the heap grows Safari briefly holds the old
		// and the new copy, which is when it kills tabs.
		Change("memoryGrowthMode", () => PlayerSettings.WebGL.memoryGrowthMode, v => PlayerSettings.WebGL.memoryGrowthMode = v, WebGLMemoryGrowthMode.Geometric);
		Change("geometricMemoryGrowthStep", () => PlayerSettings.WebGL.geometricMemoryGrowthStep, v => PlayerSettings.WebGL.geometricMemoryGrowthStep = v, 0.2f);
		Change("memoryGeometricGrowthCap", () => PlayerSettings.WebGL.memoryGeometricGrowthCap, v => PlayerSettings.WebGL.memoryGeometricGrowthCap = v, 32);
		Change("powerPreference", () => PlayerSettings.WebGL.powerPreference, v => PlayerSettings.WebGL.powerPreference = v, WebGLPowerPreference.HighPerformance);
		// Gzip, not Brotli: browsers only take Brotli over https, and the first
		// phone tests run over plain http on the home network. Safari decodes
		// gzip natively either way; files are ~20 % bigger (estimate).
		Change("compressionFormat", () => PlayerSettings.WebGL.compressionFormat, v => PlayerSettings.WebGL.compressionFormat = v, WebGLCompressionFormat.Gzip);
		// The phone page's service worker (template/sw.js) keeps the offline copy;
		// Unity's own IndexedDB cache would store the data file a second time.
		Change("dataCaching", () => PlayerSettings.WebGL.dataCaching, v => PlayerSettings.WebGL.dataCaching = v, false);
		// Hashed names: a phone can't serve itself a stale file after a rebuild,
		// and the offline copy can tell a new build's files from the old ones.
		Change("nameFilesAsHashes", () => PlayerSettings.WebGL.nameFilesAsHashes, v => PlayerSettings.WebGL.nameFilesAsHashes = v, true);

		// Smaller code means less heap; no behaviour change.
		Change("il2cpp code generation", () => PlayerSettings.GetIl2CppCodeGeneration(NamedBuildTarget.WebGL),
			v => PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, v), Il2CppCodeGeneration.OptimizeSize);
		Change("stripEngineCode", () => PlayerSettings.stripEngineCode, v => PlayerSettings.stripEngineCode = v, true);
		if (OptimizeMeshData)
		{
			Change("stripUnusedMeshComponents", () => PlayerSettings.stripUnusedMeshComponents, v => PlayerSettings.stripUnusedMeshComponents = v, true);
		}

		// The game ships no lightmaps, so lightmap variants are dead weight.
		StripLightmapVariants();
		KeepLongAudioCompressed();

		Debug.Log("[webgl-port] WebGL phone settings applied (" + s_restore.Count + " changed)");
	}

	// Sets one setting and records how to put it back.
	private static void Change<T>(string label, Func<T> get, Action<T> set, T value)
	{
		T old = get();
		if (EqualityComparer<T>.Default.Equals(old, value))
		{
			return;
		}
		set(value);
		s_restore.Add(() => set(old));
		Debug.Log("[webgl-port] phone: " + label + " " + old + " -> " + value);
	}

	private static void RestoreSettings()
	{
		for (int i = s_restore.Count - 1; i >= 0; i--)
		{
			s_restore[i]();
		}
		if (s_restore.Count > 0)
		{
			Debug.Log("[webgl-port] WebGL phone settings restored (" + s_restore.Count + ")");
		}
		s_restore.Clear();
	}

	// Long clips load when first played, not with the scene, on phones
	// (sound unchanged). On WebGL every
	// loaded clip is decoded by Web Audio to 32-bit float PCM whatever its load
	// type (Unity refused a CompressedInMemory WebGL override for all
	// 24 long clips; ApocalypseIntro 221 s stereo = 80.9 MB), and every clip a
	// scene references was loaded with it. preloadAudioData off leaves a clip
	// unloaded until an AudioSource plays it; WebGLPortAudioUnload frees long
	// clips that stopped playing. Only preloadAudioData changes (an importer
	// setting, not per platform), and it is put back after the build.
	private static void KeepLongAudioCompressed()
	{
		int changed = 0;
		double mb = 0;
		foreach (string guid in AssetDatabase.FindAssets("t:AudioClip"))
		{
			string path = AssetDatabase.GUIDToAssetPath(guid);
			AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
			AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
			if (importer == null || clip == null)
			{
				Debug.Log("[webgl-port] phone audio: skipped " + path + " (importer " + (AssetImporter.GetAtPath(path)?.GetType().Name ?? "none")
					+ ", " + (clip != null ? clip.length.ToString("0") + " s" : "no clip") + ")");
				continue;
			}
			// Unity 2022: preloadAudioData lives in the sample settings (the old
			// importer property is a compile error). The WebGL build uses the
			// default settings unless the clip has a WebGL override.
			AudioImporterSampleSettings settings = importer.defaultSampleSettings;
			if (clip.length < CompressedAudioSeconds || !settings.preloadAudioData)
			{
				continue;
			}
			if (importer.ContainsSampleSettingsOverride("WebGL"))
			{
				Debug.LogWarning("[webgl-port] phone audio: " + path + " has its own WebGL settings (preload " + importer.GetOverrideSampleSettings("WebGL").preloadAudioData + "); left as is");
				continue;
			}
			settings.preloadAudioData = false;
			importer.defaultSampleSettings = settings;
			importer.SaveAndReimport();
			double size = (double)clip.samples * clip.channels * 4 / 1048576;
			mb += size;
			changed++;
			Debug.Log("[webgl-port] phone audio: " + path + " " + clip.length.ToString("0") + " s loads on first play (" + size.ToString("0.0") + " MB decoded)");
			s_restore.Add(() =>
			{
				AudioImporter i = AssetImporter.GetAtPath(path) as AudioImporter;
				if (i == null)
				{
					return;
				}
				AudioImporterSampleSettings back = i.defaultSampleSettings;
				back.preloadAudioData = true;
				i.defaultSampleSettings = back;
				i.SaveAndReimport();
			});
		}
		Debug.Log("[webgl-port] phone: " + changed + " clips of " + CompressedAudioSeconds + " s or more load on first play (" + mb.ToString("0") + " MB decoded if all were loaded)");
	}

	private static void StripLightmapVariants()
	{
		UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
		if (assets.Length == 0)
		{
			Debug.LogWarning("[webgl-port] phone: GraphicsSettings.asset not found; lightmap variants kept");
			return;
		}
		SerializedObject graphics = new SerializedObject(assets[0]);
		var values = new Dictionary<string, int>
		{
			{ "m_LightmapStripping", 1 },
			{ "m_LightmapKeepPlain", 0 },
			{ "m_LightmapKeepDirCombined", 0 },
			{ "m_LightmapKeepDynamicPlain", 0 },
			{ "m_LightmapKeepDynamicDirCombined", 0 },
			{ "m_LightmapKeepShadowMask", 0 },
			{ "m_LightmapKeepSubtractive", 0 },
		};
		foreach (KeyValuePair<string, int> kv in values)
		{
			SerializedProperty p = graphics.FindProperty(kv.Key);
			if (p == null)
			{
				Debug.LogWarning("[webgl-port] phone: GraphicsSettings has no " + kv.Key);
				continue;
			}
			int old = p.propertyType == SerializedPropertyType.Boolean ? (p.boolValue ? 1 : 0) : p.intValue;
			if (old == kv.Value)
			{
				continue;
			}
			string name = kv.Key;
			Set(p, kv.Value);
			s_restore.Add(() =>
			{
				SerializedObject g = new SerializedObject(assets[0]);
				Set(g.FindProperty(name), old);
				g.ApplyModifiedPropertiesWithoutUndo();
			});
		}
		graphics.ApplyModifiedPropertiesWithoutUndo();
	}

	private static void Set(SerializedProperty p, int value)
	{
		if (p.propertyType == SerializedPropertyType.Boolean)
		{
			p.boolValue = value != 0;
		}
		else
		{
			p.intValue = value;
		}
	}

	// Shader variant count per build, logged after every WebGL build (desktop
	// too), so stripping can be measured. Counting only; nothing is removed here.
	private static int s_variants;
	private static readonly Dictionary<string, int> s_perShader = new Dictionary<string, int>();

	public int callbackOrder { get { return 1000; } }

	public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
	{
		s_variants += data.Count;
		s_perShader.TryGetValue(shader.name, out int n);
		s_perShader[shader.name] = n + data.Count;
	}

	public void OnPostprocessBuild(BuildReport report)
	{
		if (report.summary.platform != BuildTarget.WebGL)
		{
			return;
		}
		Debug.Log("[webgl-port] shader variants compiled: " + s_variants + "; top: "
			+ string.Join(", ", s_perShader.OrderByDescending(kv => kv.Value).Take(10).Select(kv => kv.Key + "=" + kv.Value)));
		s_variants = 0;
		s_perShader.Clear();
	}
}
