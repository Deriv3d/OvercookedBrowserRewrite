// Small helpers the patched game scripts call (see tools/apply_patches.py).
// Copied into the Assembly-CSharp scripts folder so the game code can see them.

using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UI;

public static class WebGLPort
{
	// Base URL for StreamingAssets. The game builds "file:///" URLs, which a
	// browser page can't open; on WebGL streamingAssetsPath is already an
	// http(s) URL next to the page.
	public static string StreamingAssetsUrl()
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		return Application.streamingAssetsPath + "/";
#else
		return "file:///" + Application.streamingAssetsPath + "/";
#endif
	}

#if UNITY_WEBGL && !UNITY_EDITOR
	[DllImport("__Internal")]
	private static extern void OvercookedPort_SyncFS();
#endif

	// Saves go to Application.persistentDataPath, which the browser keeps in
	// IndexedDB. Writes land in memory first; this pushes them to IndexedDB so
	// progress survives a reload. Call after every save or delete.
	public static void FlushSaves()
	{
#if UNITY_WEBGL && !UNITY_EDITOR
		OvercookedPort_SyncFS();
#endif
	}

	// Rich-text tags that UI.Text strips; any other "<...>" is drawn as text.
	private static readonly string[] s_richTags = { "b", "/b", "i", "/i", "color", "/color", "size", "/size", "material", "/material" };

	// First vertex of the quad drawn for text[charIndex] in a UI.Text mesh.
	// Unity 5.5 made one quad per character, so the game used charIndex * 4.
	// Since Unity 2019.1, spaces and rich-text tags get no quad (a <quad> tag
	// still gets one), so the index must count only the characters that are
	// drawn before it.
	//
	// Counting characters still missed in some lines (build13: "Use your
	// <quad> button!" in the 1-1 tutorial hid the "b" and left the quad
	// showing the whole font atlas). A <quad> is the only quad whose UVs span
	// the whole texture (0..1 both ways), so find it in the generated vertices
	// first and fall back to counting.
	public static int QuadVertexIndex(Text text, int charIndex)
	{
		int byUv = QuadVertexIndexFromUVs(text, charIndex);
		return byUv >= 0 ? byUv : QuadVertexIndexByCounting(text, charIndex);
	}

	private static int QuadVertexIndexFromUVs(Text text, int charIndex)
	{
		string s = text.text;
		int wanted = 0;
		for (int i = s.IndexOf("<quad", System.StringComparison.Ordinal); i >= 0 && i < charIndex; i = s.IndexOf("<quad", i + 1, System.StringComparison.Ordinal))
		{
			wanted++;
		}
		System.Collections.Generic.IList<UIVertex> verts = text.cachedTextGenerator.verts;
		int seen = 0;
		for (int v = 0; v + 3 < verts.Count; v += 4)
		{
			float minX = 1f, minY = 1f, maxX = 0f, maxY = 0f;
			for (int j = 0; j < 4; j++)
			{
				Vector2 uv = verts[v + j].uv0;
				minX = Mathf.Min(minX, uv.x);
				minY = Mathf.Min(minY, uv.y);
				maxX = Mathf.Max(maxX, uv.x);
				maxY = Mathf.Max(maxY, uv.y);
			}
			if (minX < 0.001f && minY < 0.001f && maxX > 0.999f && maxY > 0.999f)
			{
				if (seen == wanted)
				{
					return v;
				}
				seen++;
			}
		}
		return -1;
	}

	private static int QuadVertexIndexByCounting(Text text, int charIndex)
	{
		string s = text.text;
		int quads = 0;
		for (int i = 0; i < charIndex && i < s.Length; i++)
		{
			char c = s[i];
			if (c == '<' && text.supportRichText)
			{
				int close = s.IndexOf('>', i);
				if (close > i && close < charIndex)
				{
					string tag = s.Substring(i + 1, close - i - 1);
					if (tag.StartsWith("quad"))
					{
						quads++;
						i = close;
						continue;
					}
					if (IsRichTag(tag))
					{
						i = close;
						continue;
					}
				}
			}
			if (!char.IsWhiteSpace(c))
			{
				quads++;
			}
		}
		return quads * 4;
	}

	private static bool IsRichTag(string tag)
	{
		foreach (string name in s_richTags)
		{
			if (tag == name || tag.StartsWith(name + "="))
			{
				return true;
			}
		}
		return false;
	}
}
