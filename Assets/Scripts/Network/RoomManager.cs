using System;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using GhostHunt.Core;

namespace GhostHunt.Network
{
    /// <summary>
    /// Manages Photon Fusion 2 room lifecycle.
    /// Room-code join: host creates room, others enter code.
    /// Host is authoritative for all game state.
    /// </summary>
    public class RoomManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        [SerializeField] private NetworkPrefabRef _playerPrefab;

        private NetworkRunner _runner;

        public event Action<string> OnRoomCreated;
        public event Action OnRoomJoined;
        public event Action<string> OnRoomError;

        public string RoomCode { get; private set; }
        public bool IsHost => _runner != null && _runner.IsServer;

        /// <summary>
        /// Create a room as host. Generates a 6-char room code.
        /// </summary>
        public async Task CreateRoom()
        {
            RoomCode = GenerateRoomCode();

            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;

            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Host,
                SessionName = RoomCode,
                PlayerCount = GameConstants.MaxPlayers,
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
            });

            if (result.Ok)
            {
                Debug.Log($"[GhostHunt] Room created: {RoomCode}");
                OnRoomCreated?.Invoke(RoomCode);
            }
            else
            {
                Debug.LogError($"[GhostHunt] Failed to create room: {result.ShutdownReason}");
                OnRoomError?.Invoke(result.ShutdownReason.ToString());
            }
        }

        /// <summary>
        /// Join an existing room by code. Connects as client.
        /// </summary>
        public async Task JoinRoom(string code)
        {
            RoomCode = code.ToUpperInvariant().Trim();

            _runner = gameObject.AddComponent<NetworkRunner>();
            _runner.ProvideInput = true;

            var result = await _runner.StartGame(new StartGameArgs
            {
                GameMode = GameMode.Client,
                SessionName = RoomCode,
                SceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>()
            });

            if (result.Ok)
            {
                Debug.Log($"[GhostHunt] Joined room: {RoomCode}");
                OnRoomJoined?.Invoke();
            }
            else
            {
                Debug.LogError($"[GhostHunt] Failed to join room: {result.ShutdownReason}");
                OnRoomError?.Invoke(result.ShutdownReason.ToString());
            }
        }

        private static string GenerateRoomCode()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // No I/O/0/1 (ambiguity)
            var code = new char[6];
            for (int i = 0; i < 6; i++)
                code[i] = chars[UnityEngine.Random.Range(0, chars.Length)];
            return new string(code);
        }

        // --- INetworkRunnerCallbacks ---

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (runner.IsServer)
            {
                // Host spawns player object for the joining client
                var obj = runner.Spawn(_playerPrefab, Vector3.zero, Quaternion.identity, player);
                Debug.Log($"[GhostHunt] Player {player} joined. Spawned network object.");
            }
        }

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            Debug.Log($"[GhostHunt] Player {player} left.");
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            Debug.Log($"[GhostHunt] Runner shutdown: {shutdownReason}");
        }

        // Required interface stubs
        public void OnInput(NetworkRunner runner, NetworkInput input) { }
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        public void OnSceneLoadDone(NetworkRunner runner) { }
        public void OnSceneLoadStart(NetworkRunner runner) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    }
}
