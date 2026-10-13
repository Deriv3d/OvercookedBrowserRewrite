// Stand-in for Steamworks.NET in the browser build.
//
// The browser can't load steam_api.dll, so apply_patches.py deletes the exported
// Steamworks.NET sources from Assembly-CSharp-firstpass and drops this file in
// their place. It declares only the members the game calls (see
// notes/symbol-map.md, "Steam"), and every call behaves as if Steam is
// unavailable: Init() fails, so SteamPlayerManager stays uninitialised and the
// game skips achievements, language lookup and DLC checks on its own.

using System.Text;

namespace Steamworks
{
	public struct AppId_t
	{
		public uint m_AppId;

		public AppId_t(uint value)
		{
			m_AppId = value;
		}
	}

	public enum EResult
	{
		k_EResultOK = 1,
		k_EResultFail = 2,
	}

	public struct UserStatsReceived_t
	{
		public ulong m_nGameID;
		public EResult m_eResult;
	}

	public delegate void SteamAPIWarningMessageHook_t(int nSeverity, StringBuilder pchDebugText);

	public static class Packsize
	{
		public static bool Test()
		{
			return true;
		}
	}

	public static class DllCheck
	{
		public static bool Test()
		{
			return true;
		}
	}

	public static class SteamAPI
	{
		public static bool RestartAppIfNecessary(AppId_t unOwnAppID)
		{
			return false;
		}

		public static bool Init()
		{
			return false;
		}

		public static void Shutdown()
		{
		}

		public static void RunCallbacks()
		{
		}
	}

	public static class SteamClient
	{
		public static void SetWarningMessageHook(SteamAPIWarningMessageHook_t pFunction)
		{
		}
	}

	public static class SteamApps
	{
		public static bool BIsDlcInstalled(AppId_t appID)
		{
			return false;
		}

		public static string GetCurrentGameLanguage()
		{
			return "english";
		}
	}

	public static class SteamUserStats
	{
		public static bool RequestCurrentStats()
		{
			return false;
		}

		public static bool SetAchievement(string pchName)
		{
			return false;
		}

		public static bool StoreStats()
		{
			return false;
		}
	}

	// Callbacks never fire: nothing on the other end produces them.
	public sealed class Callback<T>
	{
		public delegate void DispatchDelegate(T param);

		private Callback(DispatchDelegate func)
		{
		}

		public static Callback<T> Create(DispatchDelegate func)
		{
			return new Callback<T>(func);
		}

		public void Unregister()
		{
		}

		public void Dispose()
		{
		}
	}
}
