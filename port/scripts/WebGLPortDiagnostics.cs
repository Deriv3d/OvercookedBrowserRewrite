using System.Collections.Generic;
// Logs the render settings that matter for matching the PC look, once per
// scene load: quality level, shadow settings, each light's shadows, and the
// main camera. Read-only; it changes nothing. Lines start with
// "[webgl-port] diag" so they're easy to find in the browser console or in
// tools/headless_test.py's browser.log.

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class WebGLPortDiagnostics
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Register()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		int level = QualitySettings.GetQualityLevel();
		Debug.Log("[webgl-port] diag scene=" + scene.name + " mode=" + mode
			+ " quality=" + level + "(" + QualitySettings.names[level] + ")"
			+ " shadows=" + QualitySettings.shadows
			+ " shadowResolution=" + QualitySettings.shadowResolution
			+ " shadowDistance=" + QualitySettings.shadowDistance
			+ " cascades=" + QualitySettings.shadowCascades
			+ " pixelLights=" + QualitySettings.pixelLightCount
			+ " colorSpace=" + QualitySettings.activeColorSpace
			+ " ambient=" + RenderSettings.ambientMode + " " + RenderSettings.ambientLight);

		foreach (Light light in Object.FindObjectsOfType<Light>())
		{
			Debug.Log("[webgl-port] diag light=" + light.name
				+ " type=" + light.type
				+ " enabled=" + light.isActiveAndEnabled
				+ " color=" + light.color
				+ " intensity=" + light.intensity
				+ " shadows=" + light.shadows
				+ " strength=" + light.shadowStrength
				+ " bias=" + light.shadowBias
				+ " normalBias=" + light.shadowNormalBias
				+ " renderMode=" + light.renderMode
				+ " cullingMask=" + light.cullingMask
				// Baked lights add nothing at runtime; without the lightmaps
				// they vanish.
				+ " baking=" + BakingOf(light)
				+ " range=" + light.range
				// The menu sun's blind streaks come from a cookie; a cookie
				// imported without alpha can black out the whole light.
				+ " cookie=" + (light.cookie != null ? light.cookie.name + " " + light.cookie.GetType().Name
					+ (light.cookie is Texture2D ? " format=" + ((Texture2D)light.cookie).format : "")
					+ " " + light.cookie.width + "x" + light.cookie.height + " wrap=" + light.cookie.wrapMode + " size=" + light.cookieSize : "none"));
		}

		Camera cam = Camera.main;
		if (cam != null)
		{
			Debug.Log("[webgl-port] diag camera=" + cam.name
				+ " path=" + cam.renderingPath
				+ " actualPath=" + cam.actualRenderingPath
				+ " near=" + cam.nearClipPlane
				+ " far=" + cam.farClipPlane
				+ " depthTextureMode=" + cam.depthTextureMode
				+ " fog=" + RenderSettings.fog + (RenderSettings.fog ? " " + RenderSettings.fogMode + " " + RenderSettings.fogColor : "")
				+ " effects=" + string.Join(",", System.Array.ConvertAll(cam.GetComponents<MonoBehaviour>(), b => b.GetType().Name + (b.enabled ? "" : "(off)"))));
			// Each enabled image effect's simple fields (4-2 too dark;
			// suspect: ColorCorrectionCurves' depth mode).
			foreach (MonoBehaviour effect in cam.GetComponents<MonoBehaviour>())
			{
				if (effect.enabled && effect.GetType().Namespace != "UnityEngine")
				{
					Debug.Log("[webgl-port] diag fx=" + effect.GetType().Name + " " + SimpleFields(effect));
				}
			}
		}

		// The menus fill in their text a few seconds after the scene loads, so
		// list the visible text a little later (fonts).
		if (scene.name == "StartScreen")
		{
			GameObject host = new GameObject("WebGLPortDiagnostics");
			Object.DontDestroyOnLoad(host);
			host.AddComponent<Runner>().StartCoroutine(LogTextLater(host, 8f));
			// Once more after the book has opened (title Space plus the 4 s opening).
			GameObject host2 = new GameObject("WebGLPortDiagnostics");
			Object.DontDestroyOnLoad(host2);
			host2.AddComponent<Runner>().StartCoroutine(LogMenuLater(host2, 20f));
		}
		else if (mode == LoadSceneMode.Single && scene.name != "Loading" && scene.name != "Ident")
		{
			GameObject host = new GameObject("WebGLPortDiagnostics");
			host.AddComponent<Runner>().StartCoroutine(LogFirstProgressBar(host));
			GameObject host2 = new GameObject("WebGLPortDiagnostics");
			host2.AddComponent<Runner>().StartCoroutine(LogParticlesLater(host2, 20f));
			GameObject host4 = new GameObject("WebGLPortDiagnostics");
			host4.AddComponent<Runner>().StartCoroutine(LogFlashes(host4));
		}
	}

	// Lightning flashes (the 4-1 strikes are hard on the eyes). Lists
	// the animators whose clips sound like weather or flashes, then logs any
	// light whose intensity rises above twice its starting value, with the
	// peak, for 5 minutes (one line per light per flash).
	// Fields of simple types (numbers, bools, enums, colours, vectors, curves as key counts).
	private static string SimpleFields(object _target)
	{
		System.Text.StringBuilder line = new System.Text.StringBuilder();
		const System.Reflection.BindingFlags Fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
		foreach (System.Reflection.FieldInfo field in _target.GetType().GetFields(Fields))
		{
			System.Type t = field.FieldType;
			object value = field.GetValue(_target);
			if (t.IsPrimitive || t.IsEnum || t == typeof(Color) || t == typeof(Vector2) || t == typeof(Vector3) || t == typeof(Vector4))
			{
				line.Append(field.Name).Append('=').Append(value).Append(' ');
			}
			else if (t == typeof(AnimationCurve) && value != null)
			{
				AnimationCurve curve = (AnimationCurve)value;
				line.Append(field.Name).Append("=curve(");
				foreach (Keyframe key in curve.keys)
				{
					line.Append(key.time.ToString("F2")).Append(':').Append(key.value.ToString("F2")).Append(' ');
				}
				line.Append(") ");
			}
		}
		return line.ToString();
	}

	// What each spot light actually lands on (4-2: the carpet under the
	// flashlights isn't among the names above), with the material values
	// Shaders asked for. A ray along the spot's axis finds the hit point; every
	// renderer whose bounds hold that point is logged.
	private static void LogSpotPools()
	{
		HashSet<string> shown = new HashSet<string>();
		foreach (Light light in Object.FindObjectsOfType<Light>())
		{
			if (light.type != LightType.Spot || !light.isActiveAndEnabled)
			{
				continue;
			}
			RaycastHit hit;
			if (!Physics.Raycast(light.transform.position, light.transform.forward, out hit, light.range * 2f))
			{
				Debug.Log("[webgl-port] diag pool light=" + PathOf(light.transform) + " hit nothing within " + (light.range * 2f));
				continue;
			}
			System.Text.StringBuilder chain = new System.Text.StringBuilder();
			for (Transform t = light.transform; t != null; t = t.parent)
			{
				chain.Append(" | ").Append(t.name).Append(" lp=").Append(t.localPosition.ToString("F3")).Append(" lr=").Append(t.localEulerAngles.ToString("F0")).Append(" ls=").Append(t.localScale.ToString("F2"));
				if (t.GetComponent<Animator>() != null)
				{
					chain.Append(" (animator)");
				}
			}
			Debug.Log("[webgl-port] diag pool-light light=" + PathOf(light.transform) + " pos=" + light.transform.position.ToString("F3") + " fwd=" + light.transform.forward.ToString("F3") + " chain:" + chain);
			Debug.Log("[webgl-port] diag pool light=" + PathOf(light.transform) + " hit=" + PathOf(hit.collider.transform) + " at " + hit.point.ToString("F2") + " dist=" + hit.distance.ToString("F2") + " normal=" + hit.normal.ToString("F2"));
			foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>())
			{
				Bounds bounds = renderer.bounds;
				bounds.Expand(0.05f);
				if (!renderer.enabled || !bounds.Contains(hit.point))
				{
					continue;
				}
				foreach (Material material in renderer.sharedMaterials)
				{
					if (material == null || shown.Count > 40 || !shown.Add(renderer.GetInstanceID() + "/" + material.GetInstanceID()))
					{
						continue;
					}
					Debug.Log("[webgl-port] diag pool-surface renderer=" + PathOf(renderer.transform) + " material=" + material.name + " shader=" + material.shader.name + " " + MaterialValues(material));
				}
			}
		}
	}

	private static string MaterialValues(Material _material)
	{
		System.Text.StringBuilder line = new System.Text.StringBuilder();
		foreach (string colour in new string[] { "_Color", "_SpecColor", "_EmissionColor" })
		{
			if (_material.HasProperty(colour))
			{
				line.Append(colour).Append('=').Append(_material.GetColor(colour).ToString("F3")).Append(' ');
			}
		}
		foreach (string value in new string[] { "_Glossiness", "_GlossMapScale", "_SmoothnessTextureChannel", "_BumpScale", "_OcclusionStrength", "_Brightness", "_Mode" })
		{
			if (_material.HasProperty(value))
			{
				line.Append(value).Append('=').Append(_material.GetFloat(value).ToString("F3")).Append(' ');
			}
		}
		foreach (string map in new string[] { "_MainTex", "_SpecGlossMap", "_BumpMap", "_OcclusionMap", "_EmissionMap" })
		{
			if (!_material.HasProperty(map))
			{
				continue;
			}
			Texture texture = _material.GetTexture(map);
			line.Append(map).Append('=');
			if (texture == null)
			{
				line.Append("none ");
				continue;
			}
			line.Append(texture.name).Append('(').Append(texture.width).Append('x').Append(texture.height);
			Texture2D tex2d = texture as Texture2D;
			if (tex2d != null)
			{
				line.Append(' ').Append(tex2d.format);
			}
			line.Append(") scale=").Append(_material.GetTextureScale(map).ToString("F2")).Append(' ');
		}
		line.Append("keywords=").Append(string.Join(" ", _material.shaderKeywords));
		return line.ToString();
	}

	// Standard materials whose textures or emission need a shader keyword that
	// isn't set (keywords may have been lost when the export was
	// imported against stand-in shaders). Only logs; changes nothing.
	private static void LogMissingKeywords()
	{
		HashSet<int> seen = new HashSet<int>();
		int lines = 0;
		foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>())
		{
			foreach (Material material in renderer.sharedMaterials)
			{
				if (material == null || material.shader == null || !material.shader.name.StartsWith("Standard") || !seen.Add(material.GetInstanceID()))
				{
					continue;
				}
				string missing = "";
				if (material.HasProperty("_BumpMap") && material.GetTexture("_BumpMap") != null && !material.IsKeywordEnabled("_NORMALMAP"))
				{
					missing += " _NORMALMAP";
				}
				if (material.HasProperty("_SpecGlossMap") && material.GetTexture("_SpecGlossMap") != null && !material.IsKeywordEnabled("_SPECGLOSSMAP"))
				{
					missing += " _SPECGLOSSMAP";
				}
				if (material.HasProperty("_MetallicGlossMap") && material.GetTexture("_MetallicGlossMap") != null && !material.IsKeywordEnabled("_METALLICGLOSSMAP"))
				{
					missing += " _METALLICGLOSSMAP";
				}
				if (material.HasProperty("_EmissionColor") && material.GetColor("_EmissionColor").maxColorComponent > 0.001f && !material.IsKeywordEnabled("_EMISSION"))
				{
					missing += " _EMISSION(" + material.GetColor("_EmissionColor").ToString("F2") + ")";
				}
				if (material.HasProperty("_ParallaxMap") && material.GetTexture("_ParallaxMap") != null && !material.IsKeywordEnabled("_PARALLAXMAP"))
				{
					missing += " _PARALLAXMAP";
				}
				if (missing.Length > 0 && lines++ < 60)
				{
					Debug.Log("[webgl-port] diag keywords scene=" + SceneManager.GetActiveScene().name + " material=" + material.name + " shader=" + material.shader.name
						+ " missing:" + missing + " has=" + string.Join(" ", material.shaderKeywords) + " renderer=" + renderer.name);
				}
			}
		}
		Debug.Log("[webgl-port] diag keywords scene=" + SceneManager.GetActiveScene().name + " Standard materials=" + seen.Count + " with missing keywords=" + lines);
	}

	// Hob flames drawn beside the hob on most levels (lava level
	// screenshot; on moving stoves the flame and food stay behind) and
	// not bobbing with the pan. For the first three flame renderers:
	// each transform from the flame up to the scene root
	// with its local position, rotation and scale, and every Animator on the
	// way with its clips, so the export's FlameFlicker curve paths can be
	// matched against what is really there.
	private static void LogFlameChains()
	{
		int shown = 0;
		foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>())
		{
			if (shown >= 3 || !System.Array.Exists(renderer.sharedMaterials, m => m != null && m.name.ToLowerInvariant().Contains("flame")))
			{
				continue;
			}
			shown++;
			System.Text.StringBuilder line = new System.Text.StringBuilder("[webgl-port] diag flame-chain renderer=" + PathOf(renderer.transform) + " world=" + renderer.transform.position.ToString("F3") + " bounds=" + renderer.bounds.center.ToString("F3"));
			for (Transform t = renderer.transform; t != null; t = t.parent)
			{
				line.Append(" | ").Append(t.name).Append(" lp=").Append(t.localPosition.ToString("F3")).Append(" lr=").Append(t.localEulerAngles.ToString("F0")).Append(" ls=").Append(t.localScale.ToString("F2"));
				Animator animator = t.GetComponent<Animator>();
				if (animator != null)
				{
					line.Append(" animator(enabled=").Append(animator.enabled).Append(" root=").Append(animator.applyRootMotion);
					if (animator.runtimeAnimatorController != null)
					{
						foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
						{
							line.Append(" clip=").Append(clip.name);
						}
					}
					line.Append(')');
				}
			}
			Debug.Log(line.ToString());
		}
	}

	// Transparent materials whose main texture has no alpha channel at runtime
	// (the hob flame draws as opaque cyan where Steam blends it
	// at about a quarter). Only logs.
	private static void LogAlphaLess()
	{
		HashSet<int> seen = new HashSet<int>();
		int lines = 0;
		foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>())
		{
			foreach (Material material in renderer.sharedMaterials)
			{
				if (material == null || material.renderQueue < 2500 || !material.HasProperty("_MainTex") || !seen.Add(material.GetInstanceID()))
				{
					continue;
				}
				Texture2D tex = material.GetTexture("_MainTex") as Texture2D;
				if (tex == null)
				{
					continue;
				}
				string format = tex.format.ToString();
				bool noAlpha = format.StartsWith("DXT1") || format.StartsWith("RGB24") || format.StartsWith("RGB565") || format.StartsWith("ETC_RGB4") || format == "ETC2_RGB" || format.StartsWith("BC4") || format.StartsWith("R8") || format.StartsWith("R16") || format.StartsWith("PVRTC_RGB");
				if (lines < 40 && (noAlpha || material.name.ToLowerInvariant().Contains("flame")))
				{
					lines++;
					Debug.Log("[webgl-port] diag alpha material=" + material.name + " shader=" + material.shader.name + " queue=" + material.renderQueue + " tex=" + tex.name + " format=" + format + (noAlpha ? " NO ALPHA" : "") + " renderer=" + renderer.name);
				}
			}
		}
	}

	// How far a hob flame moves over 3 s, relative to its cooking station
	// (Steam's flame only flickers, ours bounces). Logs the min/max
	// of the flame's local y and scale y and of its offset from the station,
	// so a bob that 5.5 swallowed as root motion shows up as a range here.
	private static IEnumerator SampleFlameMotion()
	{
		List<Transform> flames = new List<Transform>();
		foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>())
		{
			if (flames.Count < 2 && System.Array.Exists(renderer.sharedMaterials, m => m != null && m.name.ToLowerInvariant().Contains("flame")))
			{
				Transform t = renderer.transform;
				while (t.parent != null && t.GetComponent<Animator>() == null)
				{
					t = t.parent;
				}
				flames.Add(t.GetComponent<Animator>() != null ? t : renderer.transform);
			}
		}
		foreach (Transform flame in flames)
		{
			Transform station = flame;
			while (station != null && !station.name.Contains("CookingStation"))
			{
				station = station.parent;
			}
			Vector3 lpMin = Vector3.one * float.MaxValue, lpMax = Vector3.one * float.MinValue;
			Vector3 offMin = lpMin, offMax = lpMax;
			float sMin = float.MaxValue, sMax = float.MinValue;
			int frames = 0;
			float end = Time.realtimeSinceStartup + 3f;
			while (flame != null && Time.realtimeSinceStartup < end)
			{
				Vector3 lp = flame.localPosition;
				Vector3 off = station != null ? station.InverseTransformPoint(flame.position) : flame.position;
				lpMin = Vector3.Min(lpMin, lp); lpMax = Vector3.Max(lpMax, lp);
				offMin = Vector3.Min(offMin, off); offMax = Vector3.Max(offMax, off);
				sMin = Mathf.Min(sMin, flame.localScale.y); sMax = Mathf.Max(sMax, flame.localScale.y);
				frames++;
				yield return new WaitForEndOfFrame();
			}
			if (flame != null)
			{
				Animator animator = flame.GetComponent<Animator>();
				Debug.Log("[webgl-port] diag flame-motion " + PathOf(flame) + " frames=" + frames + " root=" + (animator != null ? animator.applyRootMotion.ToString() : "none")
					+ " localPos " + lpMin.ToString("F3") + ".." + lpMax.ToString("F3") + " scaleY " + sMin.ToString("F3") + ".." + sMax.ToString("F3")
					+ " fromStation " + offMin.ToString("F3") + ".." + offMax.ToString("F3") + " station=" + (station != null ? station.name : "none"));
			}
		}
	}

	private static IEnumerator LogFlashes(GameObject host)
	{
		yield return new WaitForSecondsRealtime(2f);
		// Shaders on the big surfaces and the chefs (4-2 too dark).
		HashSet<string> shown = new HashSet<string>();
		foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>())
		{
			string name = renderer.name.ToLowerInvariant();
			bool chef = renderer is SkinnedMeshRenderer && PathOf(renderer.transform).ToLowerInvariant().Contains("chef");
			bool flame = System.Array.Exists(renderer.sharedMaterials, m => m != null && m.name.ToLowerInvariant().Contains("flame"));
			// The pot or pan on a hob and the hob itself (flames show through pots).
			bool hob = PathOf(renderer.transform).Contains("CookingStation");
			if (!chef && !flame && !hob && !name.Contains("carpet") && !name.Contains("rug") && !name.Contains("floor") && !name.Contains("wall") && !name.Contains("stove") && !name.Contains("hob"))
			{
				continue;
			}
			foreach (Material material in renderer.sharedMaterials)
			{
				string key = renderer.name + "/" + (material != null ? material.name : "null");
				if (material == null || !shown.Add(key) || (shown.Count > 40 && !flame && !hob))
				{
					continue;
				}
				Debug.Log("[webgl-port] diag surface renderer=" + PathOf(renderer.transform) + " material=" + material.name + " shader=" + material.shader.name
					+ " queue=" + material.renderQueue + (material.HasProperty("_ZTest") ? " zTest=" + material.GetFloat("_ZTest") : "") + " shaderQueue=" + material.shader.renderQueue + (material.HasProperty("_ZWrite") ? " zWrite=" + material.GetFloat("_ZWrite") : "") + " sortingOrder=" + renderer.sortingOrder + " keywords=" + string.Join(" ", material.shaderKeywords) + " lightmapIndex=" + renderer.lightmapIndex);
			}
		}
		foreach (Animator animator in Object.FindObjectsOfType<Animator>())
		{
			RuntimeAnimatorController controller = animator.runtimeAnimatorController;
			if (controller == null)
			{
				continue;
			}
			foreach (AnimationClip clip in controller.animationClips)
			{
				string name = clip.name.ToLowerInvariant();
				if (name.Contains("light") || name.Contains("thunder") || name.Contains("storm") || name.Contains("flash") || name.Contains("strike"))
				{
					Debug.Log("[webgl-port] diag flash animator=" + PathOf(animator.transform) + " clip=" + clip.name + " length=" + clip.length.ToString("F2") + " events=" + clip.events.Length);
				}
			}
		}
		LogSpotPools();
		LogMissingKeywords();
		LogFlameChains();
		LogAlphaLess();
		host.GetComponent<Runner>().StartCoroutine(SampleFlameMotion());
		Light[] lights = Object.FindObjectsOfType<Light>();
		// Each light as it is 2 s after load, after any Animator has written it
		// (the load-time "diag light" lines come before that; 4-2 black).
		foreach (Light light in lights)
		{
			Debug.Log("[webgl-port] diag light-now=" + PathOf(light.transform) + " type=" + light.type + " active=" + light.isActiveAndEnabled
				+ " intensity=" + light.intensity.ToString("F2") + " color=" + light.color + " range=" + light.range + " spotAngle=" + light.spotAngle + " shadows=" + light.shadows + " cullingMask=" + light.cullingMask + " cookie=" + (light.cookie != null ? light.cookie.name : "none") + " baking=" + BakingOf(light) + " renderMode=" + light.renderMode + " pixelLights=" + QualitySettings.pixelLightCount);
		}
		float[] start = new float[lights.Length];
		float[] peak = new float[lights.Length];
		for (int i = 0; i < lights.Length; i++)
		{
			start[i] = lights[i].intensity;
		}
		int lines = 0;
		float until = Time.realtimeSinceStartup + 300f;
		while (Time.realtimeSinceStartup < until && lines < 60)
		{
			yield return null;
			for (int i = 0; i < lights.Length; i++)
			{
				Light light = lights[i];
				if (light == null)
				{
					continue;
				}
				float value = light.enabled && light.gameObject.activeInHierarchy ? light.intensity : 0f;
				bool flashing = value > Mathf.Max(2f * start[i], start[i] + 0.5f);
				if (flashing)
				{
					peak[i] = Mathf.Max(peak[i], value);
				}
				else if (peak[i] > 0f)
				{
					lines++;
					Debug.Log("[webgl-port] diag flash light=" + PathOf(light.transform) + " type=" + light.type + " start=" + start[i].ToString("F2") + " peak=" + peak[i].ToString("F2") + " color=" + light.color + " range=" + light.range + " t=" + Time.realtimeSinceStartup.ToString("F2"));
					peak[i] = 0f;
				}
			}
		}
		Object.Destroy(host);
	}

	// Particle systems in a level a little after it starts (rooftop ash, fire
	// glow): whether each plays, how many particles it has and what it
	// draws with.
	private static IEnumerator LogParticlesLater(GameObject host, float delay)
	{
		yield return new WaitForSecondsRealtime(delay);
		foreach (ParticleSystem ps in Object.FindObjectsOfType<ParticleSystem>())
		{
			ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
			Material m = r != null ? r.sharedMaterial : null;
			Debug.Log("[webgl-port] diag particles=" + PathOf(ps.transform)
				+ " active=" + ps.gameObject.activeInHierarchy + " playing=" + ps.isPlaying + " count=" + ps.particleCount
				+ " max=" + ps.main.maxParticles + " sim=" + ps.main.simulationSpace
				+ " renderer=" + (r != null ? r.enabled + " " + r.renderMode : "none")
				+ " material=" + (m != null ? m.name + "/" + m.shader.name + " queue=" + m.renderQueue
					+ (m.HasProperty("_TintColor") ? " tint=" + m.GetColor("_TintColor") : "")
					+ (m.mainTexture != null ? " tex=" + m.mainTexture.name : "") : "none"));
		}
		Object.Destroy(host);
	}

	// Progress bars in levels (chopping, cooking, washing): how their images
	// are sized and scaled, for the missing outline. The loading-screen bar
	// (FlowManager/.../LoadProgress) is a different prefab and is skipped.
	// Each bar instance is logged once, up to 6, for 30 minutes.
	private static IEnumerator LogFirstProgressBar(GameObject host)
	{
		HashSet<int> seen = new HashSet<int>();
		for (int tries = 0; tries < 1800 && seen.Count < 6; tries++)
		{
			yield return new WaitForSecondsRealtime(1f);
			foreach (ProgressBarUI bar in Object.FindObjectsOfType<ProgressBarUI>())
			{
				if (!bar.isActiveAndEnabled || PathOf(bar.transform).Contains("LoadProgress") || !seen.Add(bar.GetInstanceID()))
				{
					continue;
				}
				CanvasScaler scaler = bar.GetComponentInParent<CanvasScaler>();
				Debug.Log("[webgl-port] diag bar root=" + PathOf(bar.transform)
					+ " scaler=" + (scaler != null ? scaler.name + " mode=" + scaler.uiScaleMode + " dynPPU=" + scaler.dynamicPixelsPerUnit + " refPPU=" + scaler.referencePixelsPerUnit : "none"));
				foreach (Image image in bar.GetComponentsInChildren<Image>(true))
				{
					Canvas canvas = image.canvas;
					Vector3[] corners = new Vector3[4];
					image.rectTransform.GetWorldCorners(corners);
					Debug.Log("[webgl-port] diag bar image=" + PathOf(image.transform)
						+ " sprite=" + (image.sprite != null ? image.sprite.name + " ppu=" + image.sprite.pixelsPerUnit + " border=" + image.sprite.border + " rect=" + image.sprite.rect + " tex=" + image.sprite.texture.name + " " + image.sprite.texture.width + "x" + image.sprite.texture.height + " packed=" + image.sprite.packed : "none")
						+ " type=" + image.type + " fillCenter=" + image.fillCenter
						+ " imagePPU=" + image.pixelsPerUnit
						+ " size=" + image.rectTransform.rect.size + " lossyScale=" + image.transform.lossyScale.ToString("G4")
						+ " worldSize=" + (corners[2] - corners[0]).ToString("G4")
						+ " color=" + image.color + " active=" + image.isActiveAndEnabled
						+ " material=" + (image.material != null ? image.material.name + "/" + image.material.shader.name : "none")
						+ " canvas=" + (canvas != null ? canvas.name + " refPPU=" + canvas.referencePixelsPerUnit + " " : "") + DescribeCanvas(canvas));
				}
			}
		}
		Object.Destroy(host);
	}

	private class Runner : MonoBehaviour
	{
	}

	private static IEnumerator LogMenuLater(GameObject host, float delay)
	{
		yield return new WaitForSecondsRealtime(delay);
		LogMenuRenderers();
		Object.Destroy(host);
	}

	private static IEnumerator LogTextLater(GameObject host, float delay)
	{
		yield return new WaitForSecondsRealtime(delay);
		foreach (Text text in Object.FindObjectsOfType<Text>())
		{
			if (!text.isActiveAndEnabled || string.IsNullOrEmpty(text.text))
			{
				continue;
			}
			Font font = text.font;
			string shown = text.text.Replace("\n", " ");
			Debug.Log("[webgl-port] diag text=\"" + (shown.Length > 24 ? shown.Substring(0, 24) : shown) + "\""
				+ " path=" + PathOf(text.transform)
				+ " font=" + (font != null ? font.name + " dynamic=" + font.dynamic + " names=" + string.Join("/", font.fontNames ?? new string[0]) : "none")
				+ " style=" + text.fontStyle
				+ " size=" + text.fontSize + (text.resizeTextForBestFit ? " bestFit" : "")
				+ " color=" + text.color
				+ " material=" + (text.material != null ? text.material.name + "/" + text.material.shader.name : "none")
				+ " effects=" + string.Join(",", System.Array.ConvertAll(text.GetComponents<BaseMeshEffect>(), DescribeEffect))
				+ " canvas=" + DescribeCanvas(text.canvas));
		}
		foreach (TextMesh mesh in Object.FindObjectsOfType<TextMesh>())
		{
			Debug.Log("[webgl-port] diag textmesh=\"" + mesh.text + "\" path=" + PathOf(mesh.transform)
				+ " font=" + (mesh.font != null ? mesh.font.name : "none") + " style=" + mesh.fontStyle + " size=" + mesh.fontSize);
		}
		LogMenuRenderers();
		Object.Destroy(host);
	}

	// Menu book: the curled page is a skinned mesh, the
	// magenta sliver is some renderer on the error shader, and the halo is
	// SSAO, which the game turns off at Standard. Logged once the menu runs.
	private static void LogMenuRenderers()
	{
		Camera cam = Camera.main;
		if (cam != null)
		{
			Debug.Log("[webgl-port] diag menu-camera path=" + cam.renderingPath + " actualPath=" + cam.actualRenderingPath
				+ HdrOf(cam) + " target=" + (cam.targetTexture != null ? cam.targetTexture.format.ToString() : "screen")
				+ " effects=" + string.Join(",", System.Array.ConvertAll(cam.GetComponents<MonoBehaviour>(), b => b.GetType().Name + (b.enabled ? "" : "(off)"))));
		}
		// Cover and camera after the book opens (Steam's cover lands
		// curled on the left; ours swings out of view).
		if (cam != null)
		{
			Debug.Log("[webgl-port] diag menu-camera-pose pos=" + cam.transform.localPosition.ToString("F3") + " rot=" + cam.transform.localRotation.ToString("F4")
				+ " fov=" + cam.fieldOfView + " aspect=" + cam.aspect + " rect=" + cam.rect);
		}
		GameObject cover = GameObject.Find("RecipeBook_Front");
		if (cover != null)
		{
			Animator coverAnimator = cover.GetComponent<Animator>();
			string state = "";
			if (coverAnimator != null)
			{
				AnimatorStateInfo info = coverAnimator.GetCurrentAnimatorStateInfo(0);
				state = " stateHash=" + info.shortNameHash + " t=" + info.normalizedTime.ToString("0.00") + " clips="
					+ string.Join(",", System.Array.ConvertAll(coverAnimator.GetCurrentAnimatorClipInfo(0), c => c.clip != null ? c.clip.name : "null"));
			}
			Debug.Log("[webgl-port] diag cover path=" + PathOf(cover.transform) + " pos=" + cover.transform.localPosition.ToString("F3")
				+ " rot=" + cover.transform.localRotation.ToString("F4") + " scale=" + cover.transform.localScale.ToString("F3")
				+ " world=" + cover.transform.position.ToString("F3") + state);
		}
		foreach (SkinnedMeshRenderer skin in Object.FindObjectsOfType<SkinnedMeshRenderer>())
		{
			Transform[] bones = skin.bones;
			int missing = 0;
			foreach (Transform bone in bones)
			{
				if (bone == null)
					missing++;
			}
			Shader shader = skin.sharedMaterial != null ? skin.sharedMaterial.shader : null;
			Animator animator = skin.GetComponentInParent<Animator>();
			Debug.Log("[webgl-port] diag skin=" + PathOf(skin.transform)
				+ " enabled=" + skin.enabled + " active=" + skin.gameObject.activeInHierarchy + " visible=" + skin.isVisible
				+ " bounds=" + skin.bounds.center + "/" + skin.bounds.size
				+ " mesh=" + (skin.sharedMesh != null ? skin.sharedMesh.name + " verts=" + skin.sharedMesh.vertexCount + " bindposes=" + skin.sharedMesh.bindposes.Length : "none")
				+ " bones=" + bones.Length + " nullBones=" + missing
				+ " root=" + (skin.rootBone != null ? skin.rootBone.name : "none")
				+ " offscreen=" + skin.updateWhenOffscreen
				+ " shader=" + (shader != null ? shader.name + " supported=" + shader.isSupported : "none")
				+ " animator=" + (animator != null ? animator.name + " enabled=" + animator.enabled + " controller=" + (animator.runtimeAnimatorController != null) : "none"));
			// Whether the page-turn animation poses the bones: the
			// playing clips and each bone's local rotation.
			if (animator != null && animator.runtimeAnimatorController != null)
			{
				for (int layer = 0; layer < animator.layerCount; layer++)
				{
					AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
					string clips = string.Join(",", System.Array.ConvertAll(animator.GetCurrentAnimatorClipInfo(layer), c => c.clip != null ? c.clip.name + "(" + c.clip.length.ToString("0.00") + "s w" + c.weight.ToString("0.00") + ")" : "null"));
					Debug.Log("[webgl-port] diag skin-anim=" + animator.name + " layer=" + layer + " stateHash=" + state.shortNameHash
						+ " t=" + state.normalizedTime.ToString("0.00") + " clips=" + clips
						+ " cullingMode=" + animator.cullingMode + " updateMode=" + animator.updateMode + " applyRootMotion=" + animator.applyRootMotion);
				}
			}
			Debug.Log("[webgl-port] diag skin-bones=" + skin.name + " " + string.Join(" ", System.Array.ConvertAll(bones, b => b == null ? "null" : b.name + b.localEulerAngles.ToString("F0") + b.localPosition.ToString("F2"))));
		}
		foreach (Renderer renderer in Object.FindObjectsOfType<Renderer>())
		{
			foreach (Material material in renderer.sharedMaterials)
			{
				if (material == null || !material.shader.isSupported || material.shader.name.Contains("Error"))
				{
					Debug.Log("[webgl-port] diag bad-material renderer=" + PathOf(renderer.transform)
						+ " material=" + (material != null ? material.name + " shader=" + material.shader.name + " supported=" + material.shader.isSupported : "null")
						+ " bounds=" + renderer.bounds.center + "/" + renderer.bounds.size);
				}
			}
		}
	}

	private static string DescribeEffect(BaseMeshEffect effect)
	{
		string s = effect.GetType().Name + (effect.enabled ? "" : "(off)");
		Shadow shadow = effect as Shadow;
		if (shadow != null)
		{
			s += "[dist=" + shadow.effectDistance + " color=" + shadow.effectColor + " graphicAlpha=" + shadow.useGraphicAlpha + "]";
		}
		return s;
	}

	private static string DescribeCanvas(Canvas canvas)
	{
		if (canvas == null)
		{
			return "none";
		}
		Canvas root = canvas.rootCanvas;
		Camera cam = root.worldCamera;
		return root.name + " mode=" + root.renderMode + " scale=" + root.scaleFactor
			+ " refPPU=" + root.referencePixelsPerUnit
			+ (cam != null ? " camera=" + cam.name + (cam.targetTexture != null ? " rt=" + cam.targetTexture.width + "x" + cam.targetTexture.height
				+ " " + cam.targetTexture.format + " clear=" + cam.clearFlags + " " + cam.backgroundColor : "") : "")
			+ " uiShader=" + Canvas.GetDefaultCanvasMaterial().shader.name;
	}

	// Light.bakingOutput only exists in 2017.3+; older reference DLLs
	// (Unity 5.5) don't have it.
	// Bloom picks its HDR path from the camera; 5.5 called it hdr.
	private static string HdrOf(Camera cam)
	{
#if UNITY_5_6_OR_NEWER
		return " allowHDR=" + cam.allowHDR + " allowMSAA=" + cam.allowMSAA;
#else
		return " hdr=" + cam.hdr;
#endif
	}

	private static string BakingOf(Light light)
	{
#if UNITY_2017_3_OR_NEWER
		return light.bakingOutput.lightmapBakeType + "/isBaked=" + light.bakingOutput.isBaked;
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
