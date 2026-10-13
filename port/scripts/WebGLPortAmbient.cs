// Ambient light for scenes set to a flat ambient colour.
//
// The game's scenes use Flat ambient (one colour). The shaders read
// ambient through the spherical-harmonics probe (unity_SHAr/g/b), which Unity
// keeps in the scene's lighting data. 2022.3 rejects the 5.5 lighting data,
// so the probe can be left at whatever the editor last computed,
// such as a sky-tinted probe, instead of the flat colour, and every object
// picks up that tint (suspected cause of the orange-washed rooftop).
// For Flat ambient the probe is just the colour, so rebuild it from
// RenderSettings on each scene load. Before/after values are logged.

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class WebGLPortAmbient
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Register()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static string L0(SphericalHarmonicsL2 sh)
	{
		return "(" + sh[0, 0].ToString("0.000") + ", " + sh[1, 0].ToString("0.000") + ", " + sh[2, 0].ToString("0.000") + ")";
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		SphericalHarmonicsL2 before = RenderSettings.ambientProbe;
		string line = "[webgl-port] ambient scene=" + scene.name
			+ " mode=" + RenderSettings.ambientMode
			+ " light=" + RenderSettings.ambientLight
			+ " sky=" + RenderSettings.ambientSkyColor
			+ " equator=" + RenderSettings.ambientEquatorColor
			+ " ground=" + RenderSettings.ambientGroundColor
			+ " intensity=" + RenderSettings.ambientIntensity
			+ " probeL0=" + L0(before);
		if (RenderSettings.ambientMode == AmbientMode.Flat)
		{
			// The probe holds linear values; the gamma-space shaders convert back.
			SphericalHarmonicsL2 flat = new SphericalHarmonicsL2();
			flat.AddAmbientLight(RenderSettings.ambientLight.linear);
			RenderSettings.ambientProbe = flat;
			line += " -> flat probeL0=" + L0(flat);
		}
		Debug.Log(line);
	}
}
