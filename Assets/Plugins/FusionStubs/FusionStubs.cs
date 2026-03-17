// Fusion 2 compilation stubs — DELETE THIS FILE when importing the real Photon Fusion 2 SDK.
// These provide just enough API surface for the project to compile without Fusion installed.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Fusion
{
    // --- Core Types ---

    public interface INetworkStruct { }

    public struct PlayerRef
    {
        public int RawEncoded;
        public override string ToString() => $"Player:{RawEncoded}";
        public static bool operator ==(PlayerRef a, PlayerRef b) => a.RawEncoded == b.RawEncoded;
        public static bool operator !=(PlayerRef a, PlayerRef b) => a.RawEncoded != b.RawEncoded;
        public override bool Equals(object obj) => obj is PlayerRef p && p.RawEncoded == RawEncoded;
        public override int GetHashCode() => RawEncoded;
    }

    public struct NetworkBool
    {
        private int _value;
        public static implicit operator bool(NetworkBool b) => b._value != 0;
        public static implicit operator NetworkBool(bool b) => new NetworkBool { _value = b ? 1 : 0 };
        public override string ToString() => ((bool)this).ToString();
    }

    public struct NetworkString<T> where T : struct
    {
        private string _value;
        public static implicit operator NetworkString<T>(string s) => new NetworkString<T> { _value = s };
        public override string ToString() => _value ?? "";
    }

    public struct _16 { }
    public struct _32 { }
    public struct _64 { }

    public struct TickTimer
    {
        private float _endTime;
        private bool _isRunning;

        public static TickTimer CreateFromSeconds(NetworkRunner runner, float seconds)
        {
            return new TickTimer { _endTime = Time.time + seconds, _isRunning = true };
        }

        public bool Expired(NetworkRunner runner) => _isRunning && Time.time >= _endTime;
        public bool ExpiredOrNotRunning(NetworkRunner runner) => !_isRunning || Time.time >= _endTime;

        public float? RemainingTime(NetworkRunner runner)
        {
            if (!_isRunning) return null;
            float remaining = _endTime - Time.time;
            return remaining > 0 ? remaining : 0;
        }
    }

    public struct NetworkPrefabRef { }

    public struct NetworkInput { }

    public enum GameMode { Host, Client, Server, Shared, AutoHostOrClient }

    public enum ShutdownReason { Ok, Error, GameClosed, GameNotFound, MaxCcuReached, CustomAuthenticationFailed }

    public struct StartGameArgs
    {
        public GameMode GameMode;
        public string SessionName;
        public int PlayerCount;
        public MonoBehaviour SceneManager;
    }

    public struct StartGameResult
    {
        public bool Ok;
        public ShutdownReason ShutdownReason;
    }

    // --- NetworkRunner ---

    public class NetworkRunner : MonoBehaviour
    {
        public bool IsServer { get; set; }
        public float DeltaTime => Time.fixedDeltaTime;
        public int Tick { get; set; }
        public bool ProvideInput { get; set; }

        public Task<StartGameResult> StartGame(StartGameArgs args)
        {
            IsServer = args.GameMode == GameMode.Host || args.GameMode == GameMode.Server;
            return Task.FromResult(new StartGameResult { Ok = true });
        }

        public NetworkObject Spawn(NetworkPrefabRef prefab, Vector3 position, Quaternion rotation, PlayerRef? inputAuthority = null)
        {
            var go = new GameObject("NetworkObject");
            go.transform.position = position;
            go.transform.rotation = rotation;
            return go.AddComponent<NetworkObject>();
        }

        public void Despawn(NetworkObject obj)
        {
            if (obj != null) Destroy(obj.gameObject);
        }

        public T GetSingleton<T>() where T : NetworkBehaviour
        {
            return FindFirstObjectByType<T>();
        }
    }

    // --- NetworkObject ---

    public class NetworkObject : MonoBehaviour
    {
        public PlayerRef InputAuthority { get; set; }
    }

    // --- NetworkSceneManagerDefault ---

    public class NetworkSceneManagerDefault : MonoBehaviour { }

    // --- NetworkBehaviour ---

    public abstract class NetworkBehaviour : MonoBehaviour
    {
        public NetworkRunner Runner => FindFirstObjectByType<NetworkRunner>();
        public NetworkObject Object => GetComponent<NetworkObject>();
        public bool HasInputAuthority => true;
        public bool HasStateAuthority => true;

        public virtual void Spawned() { }
        public virtual void FixedUpdateNetwork() { }
    }

    // --- NetworkLinkedList ---

    public struct NetworkLinkedList<T> : IEnumerable<T> where T : struct
    {
        private List<T> _list;

        private List<T> List => _list ??= new List<T>();

        public int Count => List.Count;

        public T this[int index]
        {
            get => List[index];
            set => List[index] = value;
        }

        public void Add(T item) => List.Add(item);

        public void Set(int index, T value) => List[index] = value;

        public IEnumerator<T> GetEnumerator() => List.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // --- Attributes ---

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class NetworkedAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class RpcAttribute : Attribute
    {
        public RpcAttribute(RpcSources sources, RpcTargets targets) { }
    }

    public enum RpcSources { InputAuthority, StateAuthority, All }
    public enum RpcTargets { InputAuthority, StateAuthority, All }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class CapacityAttribute : Attribute
    {
        public CapacityAttribute(int capacity) { }
    }

    // --- AOT ---

    public static class AOT
    {
        [AttributeUsage(AttributeTargets.Method)]
        public class MonoPInvokeCallbackAttribute : Attribute
        {
            public MonoPInvokeCallbackAttribute(Type type) { }
        }
    }
}

namespace Fusion.Sockets
{
    public interface INetworkRunnerCallbacks
    {
        void OnPlayerJoined(Fusion.NetworkRunner runner, Fusion.PlayerRef player);
        void OnPlayerLeft(Fusion.NetworkRunner runner, Fusion.PlayerRef player);
        void OnShutdown(Fusion.NetworkRunner runner, Fusion.ShutdownReason shutdownReason);
        void OnInput(Fusion.NetworkRunner runner, Fusion.NetworkInput input);
        void OnInputMissing(Fusion.NetworkRunner runner, Fusion.PlayerRef player, Fusion.NetworkInput input);
        void OnConnectedToServer(Fusion.NetworkRunner runner);
        void OnDisconnectedFromServer(Fusion.NetworkRunner runner, NetDisconnectReason reason);
        void OnConnectRequest(Fusion.NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token);
        void OnConnectFailed(Fusion.NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason);
        void OnUserSimulationMessage(Fusion.NetworkRunner runner, SimulationMessagePtr message);
        void OnCustomAuthenticationResponse(Fusion.NetworkRunner runner, Dictionary<string, object> data);
        void OnHostMigration(Fusion.NetworkRunner runner, HostMigrationToken hostMigrationToken);
        void OnSceneLoadDone(Fusion.NetworkRunner runner);
        void OnSceneLoadStart(Fusion.NetworkRunner runner);
        void OnObjectExitAOI(Fusion.NetworkRunner runner, Fusion.NetworkObject obj, Fusion.PlayerRef player);
        void OnObjectEnterAOI(Fusion.NetworkRunner runner, Fusion.NetworkObject obj, Fusion.PlayerRef player);
        void OnReliableDataReceived(Fusion.NetworkRunner runner, Fusion.PlayerRef player, ReliableKey key, ArraySegment<byte> data);
        void OnReliableDataProgress(Fusion.NetworkRunner runner, Fusion.PlayerRef player, ReliableKey key, float progress);
        void OnSessionListUpdated(Fusion.NetworkRunner runner, List<SessionInfo> sessionList);
    }

    public enum NetDisconnectReason { Requested, Timeout, Error }
    public struct NetAddress { }
    public enum NetConnectFailedReason { Timeout, Refused, ServerFull }
    public struct SimulationMessagePtr { }
    public class HostMigrationToken { }
    public class SessionInfo { public string Name; }
    public struct ReliableKey { }

    public static class NetworkRunnerCallbackArgs
    {
        public class ConnectRequest
        {
            public void Accept() { }
            public void Refuse() { }
        }
    }
}
