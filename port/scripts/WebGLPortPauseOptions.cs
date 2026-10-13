// OPTIONS in the in-level pause menu (not in Steam).
//
// The pause menu gets an OPTIONS entry before QUIT. It shows the same option
// pages the recipe book has (Graphics, Audio) on top of the paused level;
// backing out returns to the pause menu and the level is never unloaded.
//
// Layout: a fresh pause menu (the pause menu's own prefab) lists the
// categories, then the chosen category's options as "LABEL  VALUE" rows, so
// everything is drawn in the pause menu's own style. The book's OptionsGUI
// page was tried first (builds 20-21): it is laid out for the book's page
// render texture and drew oversized, smeared and with its columns collapsed
// on the pause canvas. Values change with left/right exactly as on the book's
// pages (same options, same steps, saved on leaving the page). The front end's
// options code reads its buttons through ScrollingListControlsHelper, which
// ignores Select while time is paused, so this reads the UI buttons directly,
// as PauseMenuManager does.

using System.Collections.Generic;
using UnityEngine;

public class WebGLPortPauseOptions : MonoBehaviour
{
	private struct Category
	{
		public string Label;
		public OptionsData.Categories Options;
	}

	// The book's "Options Selected" list minus the entries that only make sense
	// on the front end (pad split, safe area, clear save).
	private static readonly Category[] s_categories =
	{
		new Category { Label = "StartScreen.Options.Graphics", Options = OptionsData.Categories.Graphics },
		new Category { Label = "StartScreen.Options.Audio", Options = OptionsData.Categories.Audio },
	};

	private GameObject m_pauseGui;
	private GameObject m_listGui;
	private FrontendGUI m_list;
	private LocalisedText m_title;
	private IOption[] m_pageOptions;   // null while the category list is shown
	private OptionsManager m_optionsManager;

	private ILogicalButton m_up;
	private ILogicalButton m_down;
	private ILogicalButton m_left;
	private ILogicalButton m_right;
	private ILogicalButton m_select;
	private ILogicalButton m_cancel;

	public bool IsOpen { get; private set; }

	// The pause entries with `added` placed before `before` (or at the end).
	public static T[] WithOptions<T>(T[] entries, T before, T added)
	{
		List<T> list = new List<T>(entries);
		if (list.Contains(added))
		{
			return entries;
		}
		int at = list.IndexOf(before);
		list.Insert(at >= 0 ? at : list.Count, added);
		return list.ToArray();
	}

	// Shows all _count entries at once: the list's rect grows downwards by
	// whole lines of the prefab's line height and the line count follows, so
	// the rows keep the game's size and spacing (QUIT no longer needs a scroll).
	public static void FitRows(FrontendGUI _list, int _count)
	{
		System.Reflection.FieldInfo field = typeof(ScrollingListUIContainer).GetField("m_numLines", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
		RectTransform rect = _list != null ? _list.transform as RectTransform : null;
		if (field == null || rect == null)
		{
			Debug.LogWarning("[webgl-port] pause rows: list or m_numLines not found");
			return;
		}
		int lines = (int)field.GetValue(_list);
		if (lines <= 0 || _count <= lines)
		{
			return;
		}
		float line = rect.rect.height / lines;
		rect.offsetMin -= new Vector2(0f, line * (_count - lines));
		_list.SetNumberOfLines(_count);
		Debug.Log("[webgl-port] pause rows: " + lines + " -> " + _count + " lines, line height " + line);
	}

	// _pauseGuiPrefab and _canvas are the pause menu's own prefab and canvas,
	// so the list is a fresh pause menu (a copy of the live one kept its
	// generated rows and selection: build 18 showed MT[EXIT] and threw).
	public static WebGLPortPauseOptions Open(GameObject _pauseGui, GameObject _pauseGuiPrefab, string _canvas)
	{
		GameObject host = new GameObject("WebGLPortPauseOptions");
		WebGLPortPauseOptions options = host.AddComponent<WebGLPortPauseOptions>();
		options.Begin(_pauseGui, _pauseGuiPrefab, _canvas);
		return options;
	}

	private void Begin(GameObject _pauseGui, GameObject _pauseGuiPrefab, string _canvas)
	{
		m_pauseGui = _pauseGui;
		m_optionsManager = GameUtils.RequireManager<OptionsManager>();
		m_up = PlayerInputLookup.GetAnyButton(PlayerInputLookup.LogicalButtonID.UIUp);
		m_down = PlayerInputLookup.GetAnyButton(PlayerInputLookup.LogicalButtonID.UIDown);
		m_left = PlayerInputLookup.GetAnyButton(PlayerInputLookup.LogicalButtonID.UILeft);
		m_right = PlayerInputLookup.GetAnyButton(PlayerInputLookup.LogicalButtonID.UIRight);
		m_select = PlayerInputLookup.GetAnyButton(PlayerInputLookup.LogicalButtonID.UISelect);
		m_cancel = PlayerInputLookup.GetAnyButton(PlayerInputLookup.LogicalButtonID.UICancel);
		// The press that chose OPTIONS must not also choose GRAPHICS.
		m_select.ClaimPressEvent();

		m_listGui = GameUtils.InstantiateUIController(_pauseGuiPrefab, _canvas);
		m_listGui.name = _pauseGuiPrefab.name + " (options)";
		m_list = m_listGui.GetComponentInChildren<FrontendGUI>(true);
		m_title = FindTitle(m_listGui, m_list);
		_pauseGui.SetActive(false);
		ShowCategories(0);
		IsOpen = true;
		Debug.Log("[webgl-port] pause options opened");
	}

	// The pause menu's title is the LocalisedText that is not inside the list.
	private static LocalisedText FindTitle(GameObject _root, FrontendGUI _list)
	{
		foreach (LocalisedText text in _root.GetComponentsInChildren<LocalisedText>(true))
		{
			if (_list == null || !text.transform.IsChildOf(_list.transform))
			{
				return text;
			}
		}
		return null;
	}

	private void Update()
	{
		if (!IsOpen || TimeManager.IsPaused(TimeManager.PauseLayer.System))
		{
			return;
		}
		if (m_down.JustPressed())
		{
			m_list.MoveDown();
		}
		if (m_up.JustPressed())
		{
			m_list.MoveUp();
		}
		if (m_pageOptions == null)
		{
			if (m_cancel.JustPressed())
			{
				Dispose();
			}
			else if (m_select.JustPressed())
			{
				ShowPage(m_list.GetSelection());
			}
			return;
		}
		// As on the book's pages, Select does nothing and Cancel goes back.
		if (m_cancel.JustPressed())
		{
			m_optionsManager.Save();
			int category = System.Array.FindIndex(s_categories, c => c.Options == m_pageOptions[0].Category);
			ShowCategories(Mathf.Max(0, category));
			return;
		}
		int step = (m_right.JustPressed() ? 1 : 0) - (m_left.JustPressed() ? 1 : 0);
		if (step != 0)
		{
			int row = Mathf.Clamp(m_list.GetSelection(), 0, m_pageOptions.Length - 1);
			Step(m_pageOptions[row], step);
			ShowRows(row);
		}
	}

	private void ShowCategories(int _selection)
	{
		m_pageOptions = null;
		FrontendListEntry.NameData[] names = new FrontendListEntry.NameData[s_categories.Length];
		for (int i = 0; i < names.Length; i++)
		{
			names[i] = new FrontendListEntry.NameData(s_categories[i].Label, _enabled: true, _completed: false);
		}
		SetNames(names, _selection);
		SetTitle("StartScreen.Options");
	}

	private void ShowPage(int _category)
	{
		if (_category < 0 || _category >= s_categories.Length)
		{
			return;
		}
		List<IOption> options = new List<IOption>();
		foreach (IOption option in m_optionsManager.IterateOverCategory(s_categories[_category].Options))
		{
			OptionsData.IEnabled enabled = option as OptionsData.IEnabled;
			if ((enabled == null || enabled.IsEnabled()) && (option is INameListOption || option is IQuantizedOption))
			{
				options.Add(option);
			}
		}
		if (options.Count == 0)
		{
			return;
		}
		m_pageOptions = options.ToArray();
		ShowRows(0);
		SetTitle(s_categories[_category].Label);
		Debug.Log("[webgl-port] pause options page " + s_categories[_category].Label + " rows=" + m_pageOptions.Length);
	}

	// Same steps as the book: name lists wrap, sliders stop at 0 and Quanta.
	private static void Step(IOption _option, int _step)
	{
		INameListOption nameList = _option as INameListOption;
		IQuantizedOption quantized = _option as IQuantizedOption;
		int value = _option.GetOption() + _step;
		if (nameList != null)
		{
			int count = nameList.GetNames().Length;
			value = (value % count + count) % count;
		}
		else if (quantized != null)
		{
			value = Mathf.Clamp(value, 0, quantized.Quanta);
		}
		_option.SetOption(value);
	}

	private void ShowRows(int _selection)
	{
		FrontendListEntry.NameData[] rows = new FrontendListEntry.NameData[m_pageOptions.Length];
		for (int i = 0; i < rows.Length; i++)
		{
			// Quoted text is shown as is by Localization.Get.
			rows[i] = new FrontendListEntry.NameData("\"" + Local(m_pageOptions[i].Label) + "   " + ValueText(m_pageOptions[i]) + "\"", _enabled: true, _completed: false);
		}
		SetNames(rows, _selection);
	}

	// What the book shows as the value: the name for name lists, 0-100 for sliders.
	private static string ValueText(IOption _option)
	{
		INameListOption nameList = _option as INameListOption;
		if (nameList != null)
		{
			string[] names = nameList.GetNames();
			int index = _option.GetOption();
			return index >= 0 && index < names.Length ? Local(names[index]) : index.ToString();
		}
		IQuantizedOption quantized = (IQuantizedOption)_option;
		return Mathf.RoundToInt(100f * _option.GetOption() / Mathf.Max(1, quantized.Quanta)).ToString();
	}

	private static string Local(string _tagOrText)
	{
		string text = Localization.Get(_tagOrText);
		return text.StartsWith("MT[") ? _tagOrText : text;
	}

	private void SetNames(FrontendListEntry.NameData[] _names, int _selection)
	{
		m_list.SetNames(_names);
		for (int guard = 0; m_list.GetSelection() > _selection && guard < 64; guard++)
		{
			m_list.MoveUp();
		}
		for (int guard = 0; m_list.GetSelection() < _selection && guard < 64; guard++)
		{
			m_list.MoveDown();
		}
	}

	private void SetTitle(string _tag)
	{
		if (m_title != null)
		{
			m_title.text = _tag;
		}
	}

	// Back to the pause menu (or gone with it when the pause menu closes).
	public void Dispose()
	{
		if (!IsOpen)
		{
			return;
		}
		IsOpen = false;
		if (m_pageOptions != null)
		{
			m_optionsManager.Save();
		}
		if (m_listGui != null)
		{
			Destroy(m_listGui);
		}
		if (m_pauseGui != null)
		{
			m_pauseGui.SetActive(true);
		}
		Destroy(gameObject);
		Debug.Log("[webgl-port] pause options closed");
	}
}
