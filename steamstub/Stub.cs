// Steam stub for the iOS port. No Steam, no network. Every call is a no-op or returns empty.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Steamworks
{
	public struct Friend
	{
		public SteamId Id;
		public bool IsFriend => false;
		public string Name => string.Empty;
		public Friend(SteamId id) { Id = id; }
	}

	public struct SteamId
	{
		public ulong Value;
		public SteamId(ulong v) { Value = v; }
		public static implicit operator ulong(SteamId s) { return s.Value; }
		public static implicit operator SteamId(ulong v) { return new SteamId(v); }
		public SteamId Id => this;
		public string Name => "Player";
		public override string ToString() { return Value.ToString(); }
		public override int GetHashCode() { return Value.GetHashCode(); }
	}

	public static class SteamClient
	{
		public static SteamId SteamId => default(SteamId);
		public static string Name => "Player";
		public static bool IsValid => false;
		public static bool IsLoggedOn => false;
		public static void Init(uint appId, bool asyncCallbacks = true) { }
		public static void Shutdown() { }
		public static void RunCallbacks() { }
	}

	public static class SteamNetworkingUtils
	{
		public static void InitRelayNetworkAccess() { }
	}

	// Networking entry points used by Facepunch Transport for NGO. Inert: nothing is ever created by a real socket.
	public static class SteamNetworkingSockets
	{
		public static T ConnectRelay<T>(SteamId target) where T : ConnectionManager, new() { return new T(); }
		public static T CreateRelaySocket<T>(int virtualPort = 0) where T : SocketManager, new() { return new T(); }
	}

	public static class SteamFriends
	{
		public static event Action<Steamworks.Data.Lobby, SteamId> OnGameLobbyJoinRequested;
		public static void SetRichPresence(string key, string value) { }
		public static void ClearRichPresence() { }
		public static void OpenUserOverlay(SteamId id, string dialog) { }
		public static void OpenGameInviteOverlay(SteamId lobby) { }
		public static IEnumerable<Friend> GetFriends() { return new List<Friend>(); }
		public static IEnumerable<Friend> GetBlocked() { return new List<Friend>(); }
		public static Task<Steamworks.Data.Image?> GetLargeAvatarAsync(SteamId id) { return Task.FromResult<Steamworks.Data.Image?>(null); }
		public static Task<Steamworks.Data.Image?> GetSmallAvatarAsync(SteamId id) { return Task.FromResult<Steamworks.Data.Image?>(null); }
	}

	public static class SteamMatchmaking
	{
		public static event Action<Steamworks.Result, Steamworks.Data.Lobby> OnLobbyCreated;
		public static event Action<Steamworks.Data.Lobby, Friend> OnLobbyMemberJoined;
		public static event Action<Steamworks.Data.Lobby, Friend> OnLobbyMemberLeave;
		public static event Action<Friend, Steamworks.Data.Lobby> OnLobbyInvite;
		public static event Action<Steamworks.Data.Lobby, uint, ushort, SteamId> OnLobbyGameCreated;
		public static event Action<Steamworks.Data.Lobby> OnLobbyDataChanged;
		public static Steamworks.Data.LobbyQuery LobbyList => new Steamworks.Data.LobbyQuery();
		public static Task<Steamworks.Data.Lobby?> CreateLobbyAsync(int maxMembers) { return Task.FromResult<Steamworks.Data.Lobby?>(null); }
	}

	public static class SteamUtils
	{
		public static event Action<bool> OnGamepadTextInputDismissed;
		public static void ShowGamepadTextInput(GamepadTextInputMode mode, GamepadTextInputLineMode line, string description, int maxChars) { }
		public static string GetEnteredGamepadText() { return string.Empty; }
	}

	public enum GamepadTextInputMode { Normal, Password }
	public enum GamepadTextInputLineMode { SingleLine, MultipleLines }

	public static class SteamUserStats
	{
		public static Task<Steamworks.Data.Leaderboard?> FindOrCreateLeaderboardAsync(string name, Steamworks.Data.LeaderboardSort sort, Steamworks.Data.LeaderboardDisplay display)
		{
			return Task.FromResult<Steamworks.Data.Leaderboard?>(null);
		}
	}
}

namespace Steamworks.ServerList
{
	public class Internet { }

}

namespace Steamworks.Data
{
	public class LobbyQuery
	{
		public LobbyQuery WithMaxResults(int n) { return this; }
		public LobbyQuery WithKeyValue(string key, string value) { return this; }
		public LobbyQuery WithSlotsAvailable(int n) { return this; }
		public LobbyQuery FilterDistanceClose() { return this; }
		public LobbyQuery FilterDistanceFar() { return this; }
		public LobbyQuery FilterDistanceWorldwide() { return this; }
		public Task<Steamworks.Data.Lobby[]> RequestAsync() { return Task.FromResult(new Steamworks.Data.Lobby[0]); }
	}

	public enum SendType { Unreliable = 0, NoNagle = 8, NoDelay = 4, Reliable = 8 | 1 }

	public struct NetIdentity
	{
		public SteamId SteamId => default(SteamId);
	}

	public struct ConnectionInfo
	{
		public NetIdentity Identity;
	}

	public struct Connection
	{
		public uint Id;
		public bool Accept() { return false; }
		public void Close() { }
		public void Flush() { }
		public Result SendMessage(byte[] data, int offset, int length, SendType sendType) { return Result.Fail; }
	}

	public struct LeaderboardEntry
	{
		public Friend User;
		public int GlobalRank;
		public int Score;
		public int[] Details;
		public string Name => string.Empty;
	}


	public enum LeaderboardSort { None, Ascending, Descending }
	public enum LeaderboardDisplay { None, Numeric, TimeSeconds, TimeMilliSeconds }

	public struct Color
	{
		public byte r, g, b, a;
	}

	public struct Image
	{
		public uint Width;
		public uint Height;
		public byte[] Data;
		public Color GetPixel(int x, int y) { return new Color(); }
	}

	public struct Lobby
	{
		public SteamId Id;
		public SteamId Owner => default(SteamId);
		public System.Collections.Generic.IEnumerable<Friend> Members => new System.Collections.Generic.List<Friend>();
		public Task<RoomEnter> Join() { return Task.FromResult(RoomEnter.Error); }
		public void Leave() { }
		public bool SetData(string key, string value) { return false; }
		public bool SetFriendsOnly() { return false; }
		public bool SetJoinable(bool b) { return false; }
		public bool SetPrivate() { return false; }
		public bool SetPublic() { return false; }
		public int MemberCount => 0;
		public bool GetGameServer(ref uint ip, ref ushort port, ref SteamId serverId) { return false; }
		public bool IsOwnedBy(SteamId id) { return false; }
		public string GetData(string key) { return null; }
		public bool Refresh() { return false; }
	}

	public struct LeaderboardUpdate
	{
		public int NewGlobalRank;
		public int Score;
	}

	public struct Leaderboard
	{
		public Task<LeaderboardEntry[]> GetScoresAsync(int count) { return Task.FromResult(new LeaderboardEntry[0]); }
		public Task<LeaderboardEntry[]> GetScoresAroundUserAsync(int count = 10) { return Task.FromResult(new LeaderboardEntry[0]); }
		public Task<LeaderboardEntry[]> GetScoresFromFriendsAsync() { return Task.FromResult(new LeaderboardEntry[0]); }
		public Task<LeaderboardUpdate?> ReplaceScore(int score) { return Task.FromResult<LeaderboardUpdate?>(null); }
		public Task<LeaderboardUpdate?> ReplaceScore(int score, int[] details) { return Task.FromResult<LeaderboardUpdate?>(null); }
	}
}


namespace Steamworks
{
	using Steamworks.Data;

	public interface IConnectionManager
	{
		void OnConnecting(ConnectionInfo info);
		void OnConnected(ConnectionInfo info);
		void OnDisconnected(ConnectionInfo info);
		void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel);
	}

	public interface ISocketManager
	{
		void OnConnecting(Connection connection, ConnectionInfo info);
		void OnConnected(Connection connection, ConnectionInfo info);
		void OnDisconnected(Connection connection, ConnectionInfo info);
		void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel);
	}

	public class ConnectionManager : IConnectionManager
	{
		public Connection Connection = new Connection();
		public IConnectionManager Interface;
		public void Close() { }
		public void Receive(int maxMessages = 32) { }
		public virtual void OnConnecting(ConnectionInfo info) { }
		public virtual void OnConnected(ConnectionInfo info) { }
		public virtual void OnDisconnected(ConnectionInfo info) { }
		public virtual void OnMessage(IntPtr data, int size, long messageNum, long recvTime, int channel) { }
	}

	public class SocketManager : ISocketManager
	{
		public ISocketManager Interface;
		public void Close() { }
		public void Receive(int maxMessages = 32) { }
		public virtual void OnConnecting(Connection connection, ConnectionInfo info) { }
		public virtual void OnConnected(Connection connection, ConnectionInfo info) { }
		public virtual void OnDisconnected(Connection connection, ConnectionInfo info) { }
		public virtual void OnMessage(Connection connection, NetIdentity identity, IntPtr data, int size, long messageNum, long recvTime, int channel) { }
	}
}




namespace Steamworks
{
}

namespace Steamworks
{
	public enum Result { OK = 1, Fail = 2 }
	public enum RoomEnter { Success, DoesntExist, NotAllowed, Full, Error, Banned, Limited, ClanDisabled, CommunityBan, MemberBlockedYou, YouBlockedMember, RatelimitExceeded }
}
