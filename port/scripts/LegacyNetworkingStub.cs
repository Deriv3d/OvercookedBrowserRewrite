// Stand-ins for Unity's legacy (RakNet) networking API, removed in Unity 2018.2.
//
// The game has an unused prototype online mode (NetworkManager,
// NetworkInputCoordinator) written against UnityEngine.Network, NetworkView,
// MasterServer and [RPC]. Those types still exist in 2022.3 but are marked
// obsolete-as-error, so the scripts don't compile. These classes live in the
// global namespace, which C# prefers over the `using UnityEngine;` imports in
// the game's (also global) scripts, so the game binds to them instead.
//
// Everything reports "not connected", so the game stays on its local path.

using UnityEngine;

public enum RPCMode
{
	Server = 0,
	Others = 1,
	OthersBuffered = 5,
	All = 2,
	AllBuffered = 6,
}

// Named RPC, not RPCAttribute, to match UnityEngine.RPC: a global RPCAttribute
// next to the imported UnityEngine.RPC makes [RPC] ambiguous (CS1614).
[System.AttributeUsage(System.AttributeTargets.Method)]
public sealed class RPC : System.Attribute
{
}

public struct NetworkPlayer : System.IEquatable<NetworkPlayer>
{
	private int m_index;

	public string guid => m_index.ToString();

	public bool Equals(NetworkPlayer other)
	{
		return m_index == other.m_index;
	}

	public override bool Equals(object obj)
	{
		return obj is NetworkPlayer other && Equals(other);
	}

	public override int GetHashCode()
	{
		return m_index;
	}

	public static bool operator ==(NetworkPlayer a, NetworkPlayer b)
	{
		return a.m_index == b.m_index;
	}

	public static bool operator !=(NetworkPlayer a, NetworkPlayer b)
	{
		return a.m_index != b.m_index;
	}
}

public sealed class HostData
{
	public string gameName = "";
}

public sealed class BitStream
{
}

public struct NetworkMessageInfo
{
}

public class NetworkView : MonoBehaviour
{
	public void RPC(string name, RPCMode mode, params object[] args)
	{
	}
}

public static class Network
{
	public static bool isClient => false;

	public static bool isServer => false;

	public static NetworkPlayer player => default(NetworkPlayer);

	public static NetworkPlayer[] connections => new NetworkPlayer[0];

	public static bool HavePublicAddress()
	{
		return false;
	}

	public static void InitializeServer(int connections, int listenPort, bool useNat)
	{
		Debug.Log("[webgl-port] Online play isn't available in the browser build");
	}

	public static void Connect(HostData hostData)
	{
	}
}

public static class MasterServer
{
	public static void RegisterHost(string gameTypeName, string gameName)
	{
	}

	public static void RequestHostList(string gameTypeName)
	{
	}

	public static HostData[] PollHostList()
	{
		return new HostData[0];
	}
}
