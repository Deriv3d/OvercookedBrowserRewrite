// Dev switches in the page address, for testing. Inactive unless the address has the parameter.
//   ?unlockall=1  turns on the game's own debug flag m_unlockAll
//                 (GameDebugConfig): every level on the world map is open and
//                 can be played in any order. Progress saves as normal.
//   ?level=4-1   on the world map, opens the player-select screen for that
//                level as if its node had been chosen (the label as the map
//                shows it, or the scene-directory index). Once per page load,
//                and only for a level that is open (bought, or ?unlockall=1).
//                Lets headless runs reach any level without steering the van.
// Steam has the same flag but no way to turn it on, so nothing changes for
// normal play.

using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WebGLPortDevFlags
{
	private static bool s_unlockAll;
	private static string s_level;

	private class Runner : MonoBehaviour
	{
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
	private static void Init()
	{
		s_unlockAll = Query(Application.absoluteURL, "unlockall") == "1";
		if (s_unlockAll)
		{
			SceneManager.sceneLoaded += OnSceneLoaded;
			Debug.Log("[webgl-port] dev: ?unlockall=1, all levels open on the world map");
		}
		s_level = Query(Application.absoluteURL, "level");
		if (!string.IsNullOrEmpty(s_level))
		{
			s_level = WWWUnescape(s_level);
			SceneManager.sceneLoaded += OnSceneLoadedLevel;
			Debug.Log("[webgl-port] dev: ?level=" + s_level + ", opens on the world map");
		}
	}

	private static string WWWUnescape(string _value)
	{
		return System.Uri.UnescapeDataString(_value.Replace('+', ' ')).Trim();
	}

	private static void OnSceneLoadedLevel(Scene _scene, LoadSceneMode _mode)
	{
		if (s_level == null || Object.FindObjectOfType<WorldMapFlowController>() == null)
		{
			return;
		}
		GameObject host = new GameObject("WebGLPortDevLevel");
		host.AddComponent<Runner>().StartCoroutine(OpenLevel(host));
	}

	private static IEnumerator OpenLevel(GameObject _host)
	{
		string wanted = s_level.StartsWith("Level ", System.StringComparison.OrdinalIgnoreCase) ? s_level.Substring(6).Trim() : s_level;
		s_level = null; // once per page load, so returning to the map doesn't loop
		yield return new WaitForSecondsRealtime(3f);
		WorldMapFlowController flow = Object.FindObjectOfType<WorldMapFlowController>();
		MapAvatarControls avatar = Object.FindObjectOfType<MapAvatarControls>();
		if (flow == null || avatar == null)
		{
			Debug.Log("[webgl-port] dev: level " + wanted + " not opened, world map not ready");
			Object.Destroy(_host);
			yield break;
		}
		SceneDirectoryData directory = flow.GetSceneDirectory();
		int index;
		if (!int.TryParse(wanted, out index))
		{
			index = -1;
		}
		LevelPortalMapNode found = null;
		foreach (LevelPortalMapNode node in Object.FindObjectsOfType<LevelPortalMapNode>())
		{
			SceneDirectoryData.SceneDirectoryEntry entry = node.LevelIndex >= 0 && node.LevelIndex < directory.Scenes.Length ? directory.Scenes[node.LevelIndex] : null;
			// Labels are localisation keys (CampaignLevel.LevelNN); the map shows their text ("4-1").
			bool labelMatch = entry != null && entry.Label != null
				&& (string.Equals(entry.Label.Trim(), wanted, System.StringComparison.OrdinalIgnoreCase)
					|| string.Equals(Shown(entry.Label), wanted, System.StringComparison.OrdinalIgnoreCase));
			if (node.LevelIndex == index || labelMatch)
			{
				found = node;
				break;
			}
		}
		if (found == null)
		{
			Debug.Log("[webgl-port] dev: no world-map level matches " + wanted + "; levels: " + LevelList(directory));
		}
		else if (!GameUtils.GetGameSession().Progress.GetProgress(found.LevelIndex).Purchased && !GameUtils.GetDebugConfig().m_unlockAll && !found.ForceUnlocked)
		{
			Debug.Log("[webgl-port] dev: level " + wanted + " is locked on this save; add &unlockall=1");
		}
		else
		{
			Debug.Log("[webgl-port] dev: opening level " + wanted + " (index " + found.LevelIndex + ", node " + found.name + ", scene " + SceneOf(directory, found.LevelIndex) + ")");
			flow.OnSelectLevelPortal(avatar, found);
		}
		Object.Destroy(_host);
	}

	// The map's text for a level, without the "Level " in front ("Level 3-1" -> "3-1").
	private static string Shown(string _label)
	{
		string text = (Localization.Get(_label) ?? "").Trim();
		return text.StartsWith("Level ") ? text.Substring(6).Trim() : text;
	}

	private static string LevelList(SceneDirectoryData _directory)
	{
		System.Text.StringBuilder list = new System.Text.StringBuilder();
		foreach (LevelPortalMapNode node in Object.FindObjectsOfType<LevelPortalMapNode>())
		{
			SceneDirectoryData.SceneDirectoryEntry entry = node.LevelIndex >= 0 && node.LevelIndex < _directory.Scenes.Length ? _directory.Scenes[node.LevelIndex] : null;
			list.Append(node.LevelIndex).Append('=').Append(entry != null && entry.Label != null ? Localization.Get(entry.Label) : "?").Append(' ');
		}
		return list.ToString();
	}

	private static string SceneOf(SceneDirectoryData _directory, int _index)
	{
		SceneDirectoryData.SceneDirectoryEntry entry = _directory.Scenes[_index];
		return entry.SceneVarients.Length > 0 ? entry.SceneVarients[0].SceneName : "?";
	}

	private static void OnSceneLoaded(Scene _scene, LoadSceneMode _mode)
	{
		GameDebugManager manager = Object.FindObjectOfType<GameDebugManager>();
		GameDebugConfig config = manager != null ? manager.GetDebugConfig() : null;
		if (config != null && !config.m_unlockAll)
		{
			config.m_unlockAll = true;
			Debug.Log("[webgl-port] dev: m_unlockAll on (scene " + _scene.name + ")");
		}
	}

	public static string Query(string _url, string _key)
	{
		int start = string.IsNullOrEmpty(_url) ? -1 : _url.IndexOf('?');
		if (start < 0)
		{
			return null;
		}
		foreach (string pair in _url.Substring(start + 1).Split('&', '#'))
		{
			if (pair.StartsWith(_key + "="))
			{
				return pair.Substring(_key.Length + 1);
			}
		}
		return null;
	}
}
