// Editor menu "Overcooked Port": WebGL settings and a one-click build.
// Also callable from the command line:
//   Unity.exe -batchmode -quit -projectPath <project> -executeMethod WebGLPortBuild.BuildFromCommandLine

using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

public static class WebGLPortBuild
{
	public const string OutputDir = "Builds/WebGL";

	[MenuItem("Overcooked Port/Apply WebGL settings")]
	public static void ApplySettings()
	{
		EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.WebGL, BuildTarget.WebGL);

		// The desktop build is only ever served from localhost, where download
		// size doesn't matter, so it skips compression: Brotli on the data file
		// was a slow step of every build. serve.py serves plain, .br and .gz files alike; the
		// phone build sets its own compression.
		PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
		PlayerSettings.WebGL.decompressionFallback = false;
		PlayerSettings.WebGL.dataCaching = true;
		// Every build used the same file names, so Chrome's cache (Unity's
		// IndexedDB data cache included) could pair an old WebGL.data with a
		// new wasm and crash at start ("memory access out of bounds"). Hashed names give each build's files their own URLs.
		PlayerSettings.WebGL.nameFilesAsHashes = true;
		PlayerSettings.WebGL.template = "PROJECT:Overcooked";

		// The original game throws and survives the odd NullReferenceException
		// (one shows in its own output_log.txt). Explicit-only exception support
		// would turn those into a hard crash in the browser.
		PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.FullWithoutStacktrace;

		// InControl and the save code rely on reflection; link.xml keeps the
		// game assemblies whole, and minimal stripping keeps the rest safe.
		PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Minimal);

		// The Steam game ran the Standard shader at its full-quality BRDF on PC.
		// WebGL tiers can default lower, which swaps in the cheaper BRDF and changes
		// how shiny surfaces look, so pin every WebGL tier to High.
		foreach (GraphicsTier tier in new[] { GraphicsTier.Tier1, GraphicsTier.Tier2, GraphicsTier.Tier3 })
		{
			TierSettings tierSettings = EditorGraphicsSettings.GetTierSettings(BuildTargetGroup.WebGL, tier);
			tierSettings.standardShaderQuality = ShaderQuality.High;
			EditorGraphicsSettings.SetTierSettings(BuildTargetGroup.WebGL, tier, tierSettings);
		}

		PlayerSettings.runInBackground = true;
		PlayerSettings.productName = "Overcooked";

		Debug.Log("[webgl-port] WebGL settings applied");
	}

	[MenuItem("Overcooked Port/Build WebGL")]
	public static void Build()
	{
		ApplySettings();
		// Shown under "Click to play" (template {{{ PRODUCT_VERSION }}}); the
		// game never reads Application.version.
		PlayerSettings.bundleVersion = "built " + System.DateTime.Now.ToString("d MMM HH:mm");
		string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
		BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
		{
			scenes = scenes,
			locationPathName = OutputDir,
			target = BuildTarget.WebGL,
			options = BuildOptions.None,
		});
		Debug.Log($"[webgl-port] Build {report.summary.result}: {report.summary.totalSize / (1024 * 1024)} MB, " +
		          $"{report.summary.totalErrors} errors, {report.summary.totalTime}");
		// Where the build time goes, so the slow steps can be cut.
		foreach (BuildStep step in report.steps.Where(s => s.depth <= 1).OrderByDescending(s => s.duration).Take(12))
		{
			Debug.Log($"[webgl-port] build step {step.duration:mm\\:ss} {new string(' ', step.depth * 2)}{step.name}");
		}
		if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded)
		{
			EditorApplication.Exit(1);
		}
	}

	public static void BuildFromCommandLine()
	{
		Build();
	}
}
