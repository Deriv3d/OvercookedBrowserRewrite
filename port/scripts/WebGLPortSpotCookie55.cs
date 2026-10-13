// [webgl-port] Adopted as a test switch: ?spotcookie=55,
// applied by WebGLPortSpotTest once a second (scene loads and late spawns).
// Why: Unity 5.5's default spot cookie (read from the 5.5 install)
// is flat out to ~55% of the radius and then drops hard to 0 by 94%, which gives Steam's
// hard-edged flashlight pools. 2022's default spot cookie is softer. This builds the 5.5 profile
// once (128x128 Alpha8, clamped) and assigns it to every spot light that has no cookie of its own,
// including spots spawned later.
using UnityEngine;

public static class WebGLPortSpotCookie55
{
	// (radius as a fraction of the texture half-size, cookie alpha), piecewise linear.
	static readonly Vector2[] kProfile = {
		new Vector2(0.000f, 1.00f), new Vector2(0.550f, 1.00f), new Vector2(0.625f, 0.95f),
		new Vector2(0.690f, 0.80f), new Vector2(0.750f, 0.52f), new Vector2(0.810f, 0.24f),
		new Vector2(0.875f, 0.06f), new Vector2(0.940f, 0.00f),
	};

	static Texture2D s_cookie;

	public static float Profile (float r)
	{
		if (r <= kProfile[0].x) return kProfile[0].y;
		for (int i = 1; i < kProfile.Length; i++)
		{
			if (r <= kProfile[i].x)
			{
				float t = (r - kProfile[i - 1].x) / (kProfile[i].x - kProfile[i - 1].x);
				return Mathf.Lerp(kProfile[i - 1].y, kProfile[i].y, t);
			}
		}
		return 0f;
	}

	public static Texture2D Cookie
	{
		get
		{
			if (s_cookie != null) return s_cookie;
			const int N = 128;
			var tex = new Texture2D(N, N, TextureFormat.Alpha8, false, true);
			tex.name = "WebGLPort_SpotCookie55";
			tex.wrapMode = TextureWrapMode.Clamp;
			tex.filterMode = FilterMode.Bilinear;
			var px = new Color32[N * N];
			for (int y = 0; y < N; y++)
			{
				for (int x = 0; x < N; x++)
				{
					// Texel centre in [-1, 1]; the border ring stays 0 so clamping can't leak light.
					float u = (x + 0.5f) / N * 2f - 1f;
					float v = (y + 0.5f) / N * 2f - 1f;
					float a = (x == 0 || y == 0 || x == N - 1 || y == N - 1) ? 0f : Profile(Mathf.Sqrt(u * u + v * v));
					px[y * N + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
				}
			}
			tex.SetPixels32(px);
			tex.Apply(false, true);
			s_cookie = tex;
			return s_cookie;
		}
	}

	// Call at scene load and again for late spawns (as ?spotinner does).
	public static void Apply ()
	{
		foreach (var l in Object.FindObjectsOfType<Light>())
		{
			if (l.type == LightType.Spot && l.cookie == null)
				l.cookie = Cookie;
		}
	}
}
