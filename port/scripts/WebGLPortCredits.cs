// "Ported By" credit at the top of the credits (not in Steam):
// Deriv3d and Claude, right after the main "Credits" title.
//
// Credits.unity: Scroller (animated AnchoredPosition.y 0 -> 8211.768 by clip
// CreditsScroller01) holds CreditsPanel, a VerticalLayoutGroup of sections
// (Title + Names + LayoutElement) and 100-high "Space" spacers. CreditsPanel
// grows by the added height, and the scroll end is pushed down by that height
// (as a share of the scroll so far), so the last section still stops where it
// did.
//
// The section is built from new objects with plain Text that copy
// DevelopersSection's look (font, size, colour, alignment, rect, layout,
// outline/shadow). Builds 21-22 cloned the whole DevelopersSection instead,
// and a second "Credits / Ported By" showed up mid-roll over the Team17 list;
// nothing from the game's own components is copied any more. The diagnostic
// below logs every credits Text that mentions "Ported" or "Credits" during
// the roll, so any remaining duplicate can be traced to its object.

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class WebGLPortCredits
{
	private const string SectionTitle = "Ported By";
	private const string SectionNames = "Deriv3d\nClaude";
	private const float ScrollEnd = 8211.768f;             // CreditsScroller01's last AnchoredPosition.y

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Register()
	{
		SceneManager.sceneLoaded += OnSceneLoaded;
	}

	private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
	{
		if (scene.name != "Credits")
		{
			return;
		}
		RectTransform panel = null;
		int panels = 0;
		foreach (GameObject root in scene.GetRootGameObjects())
		{
			foreach (VerticalLayoutGroup group in root.GetComponentsInChildren<VerticalLayoutGroup>(true))
			{
				if (group.name == "CreditsPanel")
				{
					panels++;
					if (panel == null)
					{
						panel = group.transform as RectTransform;
					}
				}
			}
		}
		Transform template = panel != null ? panel.Find("DevelopersSection") : null;
		Transform spacer = panel != null ? panel.Find("Space") : null;
		if (template == null || spacer == null)
		{
			Debug.LogWarning("[webgl-port] credits: CreditsPanel/DevelopersSection/Space not found; port credit not added");
			return;
		}

		RectTransform section = NewRect("WebGLPortSection", panel, template as RectTransform);
		CopyLayout(template.gameObject, section.gameObject);
		NewText("Title", section, template.Find("Title"), SectionTitle);
		NewText("Names", section, template.Find("Names"), SectionNames);
		RectTransform gap = NewRect("WebGLPortSpace", panel, spacer as RectTransform);
		CopyLayout(spacer.gameObject, gap.gameObject);
		// Right after the main "Credits" title (child 0).
		section.SetSiblingIndex(1);
		gap.SetSiblingIndex(2);

		float added = Height(section.gameObject) + Height(gap.gameObject);
		panel.sizeDelta += new Vector2(0f, added);
		RectTransform scroller = panel.parent as RectTransform;
		if (scroller != null)
		{
			scroller.gameObject.AddComponent<ScrollEndOffset>().Added = added;
		}
		panel.gameObject.AddComponent<CreditsDiag>();
		Debug.Log("[webgl-port] credits: port credit added, panel +" + added + " (CreditsPanels in scene: " + panels + ")");
	}

	private static RectTransform NewRect(string _name, Transform _parent, RectTransform _like)
	{
		GameObject go = new GameObject(_name, typeof(RectTransform));
		go.layer = _like.gameObject.layer;
		RectTransform rect = go.GetComponent<RectTransform>();
		rect.SetParent(_parent, false);
		rect.anchorMin = _like.anchorMin;
		rect.anchorMax = _like.anchorMax;
		rect.pivot = _like.pivot;
		rect.anchoredPosition = _like.anchoredPosition;
		rect.sizeDelta = _like.sizeDelta;
		rect.localScale = _like.localScale;
		return rect;
	}

	private static void CopyLayout(GameObject _from, GameObject _to)
	{
		LayoutElement from = _from.GetComponent<LayoutElement>();
		if (from == null)
		{
			return;
		}
		LayoutElement to = _to.AddComponent<LayoutElement>();
		to.ignoreLayout = from.ignoreLayout;
		to.minWidth = from.minWidth;
		to.minHeight = from.minHeight;
		to.preferredWidth = from.preferredWidth;
		to.preferredHeight = from.preferredHeight;
		to.flexibleWidth = from.flexibleWidth;
		to.flexibleHeight = from.flexibleHeight;
	}

	private static void NewText(string _name, Transform _parent, Transform _like, string _text)
	{
		Text like = _like != null ? _like.GetComponent<Text>() : null;
		if (like == null)
		{
			Debug.LogWarning("[webgl-port] credits: template " + _name + " not found");
			return;
		}
		RectTransform rect = NewRect(_name, _parent, _like as RectTransform);
		Text text = rect.gameObject.AddComponent<Text>();
		text.font = like.font;
		text.fontSize = like.fontSize;
		text.fontStyle = like.fontStyle;
		text.color = like.color;
		text.alignment = like.alignment;
		text.lineSpacing = like.lineSpacing;
		text.supportRichText = like.supportRichText;
		text.resizeTextForBestFit = like.resizeTextForBestFit;
		text.resizeTextMinSize = like.resizeTextMinSize;
		text.resizeTextMaxSize = like.resizeTextMaxSize;
		text.horizontalOverflow = like.horizontalOverflow;
		text.verticalOverflow = like.verticalOverflow;
		text.alignByGeometry = like.alignByGeometry;
		text.raycastTarget = false;
		if (like.material != like.defaultMaterial)
		{
			text.material = like.material;
		}
		text.text = _text;
		foreach (Shadow effect in _like.GetComponents<Shadow>())
		{
			Shadow copy = effect is Outline ? rect.gameObject.AddComponent<Outline>() : rect.gameObject.AddComponent<Shadow>();
			copy.effectColor = effect.effectColor;
			copy.effectDistance = effect.effectDistance;
			copy.useGraphicAlpha = effect.useGraphicAlpha;
		}
	}

	private static float Height(GameObject _go)
	{
		LayoutElement element = _go.GetComponent<LayoutElement>();
		if (element != null)
		{
			return Mathf.Max(element.minHeight, element.preferredHeight);
		}
		return (_go.transform as RectTransform).rect.height;
	}

	// Runs after the Animator has written the scroll position each frame.
	private class ScrollEndOffset : MonoBehaviour
	{
		public float Added;
		private RectTransform m_rect;

		private void Awake()
		{
			m_rect = transform as RectTransform;
		}

		private void LateUpdate()
		{
			Vector2 pos = m_rect.anchoredPosition;
			pos.y += Added * Mathf.Clamp01(pos.y / ScrollEnd);
			m_rect.anchoredPosition = pos;
		}
	}

	// Logs where every "Ported"/"Credits" text is during the roll (every 5 s
	// from 0 to 70 s), to trace the mid-roll duplicate seen in builds 21-22.
	private class CreditsDiag : MonoBehaviour
	{
		private IEnumerator Start()
		{
			for (int t = 0; t <= 70; t += 5)
			{
				Log(t);
				yield return new WaitForSeconds(5f);
			}
		}

		private static void Log(int _t)
		{
			foreach (Text text in Object.FindObjectsOfType<Text>())
			{
				string value = text.text ?? "";
				if (value.Contains("Ported") || value.Trim() == "Credits" || value.Contains("Deriv3d"))
				{
					Canvas canvas = text.canvas;
					Debug.Log("[webgl-port] diag credits t=" + _t + " path=" + PathOf(text.transform) + " text=\"" + value.Replace("\n", "|") + "\" world=" + text.transform.position + " active=" + text.isActiveAndEnabled + " canvas=" + (canvas != null ? canvas.name : "none"));
				}
			}
		}

		private static string PathOf(Transform _t)
		{
			string path = _t.name;
			for (Transform p = _t.parent; p != null; p = p.parent)
			{
				path = p.name + "/" + path;
			}
			return path + "#" + _t.GetSiblingIndex();
		}
	}
}
