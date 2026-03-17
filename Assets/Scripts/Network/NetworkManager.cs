using Fusion;
using GhostHunt.Core;
using GhostHunt.Maze;
using UnityEngine;

namespace GhostHunt.Network
{
    /// <summary>
    /// Top-level network orchestrator. Bootstraps all network singletons,
    /// wires cross-component communication, manages the full lifecycle
    /// from room creation through gameplay to round end.
    ///
    /// Uses IPlayerStateAccess to interact with PlayerController without
    /// a direct assembly reference (avoids Network→Player circular dep).
    /// </summary>
    public class NetworkManager : MonoBehaviour
    {
        [Header("Network Prefabs")]
        [SerializeField] private NetworkPrefabRef _gameManagerPrefab;
        [SerializeField] private NetworkPrefabRef _lobbyManagerPrefab;
        [SerializeField] private NetworkPrefabRef _eventResolverPrefab;

        [Header("Maze Rendering")]
        [SerializeField] private MazeRenderer _mazeRenderer;

        public static NetworkManager Instance { get; private set; }

        public RoomManager Room { get; private set; }
        public GameManager Game { get; private set; }
        public LobbyManager Lobby { get; private set; }
        public GameEventResolver Events { get; private set; }
        public NetworkRunner Runner { get; private set; }

        private MazeGenerator _currentMaze;
        private int[,] _currentGrid;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            Room = GetComponent<RoomManager>();
            if (Room == null) Room = gameObject.AddComponent<RoomManager>();
        }

        private void OnEnable()
        {
            Room.OnRoomCreated += OnRoomReady;
            Room.OnRoomJoined += OnRoomReady;
            MazeManager.OnCollectiblePickedUp += HandleCollectiblePickup;
            MazeManager.OnPowerPelletPickedUp += HandlePowerPelletPickup;
            MazeManager.OnPortalEntered += HandlePortalEnter;
            MazeManager.OnCatchAttempt += HandleCatchAttempt;
        }

        private void OnDisable()
        {
            Room.OnRoomCreated -= OnRoomReady;
            Room.OnRoomJoined -= OnRoomReady;
            MazeManager.OnCollectiblePickedUp -= HandleCollectiblePickup;
            MazeManager.OnPowerPelletPickedUp -= HandlePowerPelletPickup;
            MazeManager.OnPortalEntered -= HandlePortalEnter;
            MazeManager.OnCatchAttempt -= HandleCatchAttempt;
        }

        private void OnRoomReady(string code) => OnRoomReady();
        private void OnRoomReady()
        {
            Runner = FindFirstObjectByType<NetworkRunner>();
            if (Runner == null) return;

            if (Runner.IsServer)
                SpawnNetworkSingletons();

            Invoke(nameof(FindSingletons), 0.5f);
        }

        private void SpawnNetworkSingletons()
        {
            Runner.Spawn(_gameManagerPrefab, Vector3.zero, Quaternion.identity);
            Runner.Spawn(_lobbyManagerPrefab, Vector3.zero, Quaternion.identity);
            Runner.Spawn(_eventResolverPrefab, Vector3.zero, Quaternion.identity);
            Debug.Log("[NetworkManager] Spawned network singletons");
        }

        private void FindSingletons()
        {
            Game = FindFirstObjectByType<GameManager>();
            Lobby = FindFirstObjectByType<LobbyManager>();
            Events = FindFirstObjectByType<GameEventResolver>();
        }

        public void StartGame()
        {
            if (Game == null) return;
            Game.StartRound();
            BuildMaze(Game.State.MazeSeed);
        }

        public void BuildMaze(int seed)
        {
            _currentMaze = new MazeGenerator(29, 31, seed);
            _currentGrid = _currentMaze.Generate();

            if (_mazeRenderer != null)
                _mazeRenderer.BuildMaze(_currentGrid, _currentMaze.Width, _currentMaze.Height);

            // Initialize runtime gameplay (triggers, colliders, AOI)
            var mazeManager = _mazeRenderer != null
                ? _mazeRenderer.GetComponent<MazeManager>()
                : null;
            if (mazeManager == null)
                mazeManager = FindFirstObjectByType<MazeManager>();
            mazeManager?.InitializeRuntime(_currentMaze, _currentGrid);

            Debug.Log($"[NetworkManager] Maze built — seed {seed}");
        }

        // --- Spawn positions ---

        public Vector3 GetGhostSpawnPosition(int ghostIndex)
        {
            if (_currentMaze == null) return Vector3.zero;
            int cx = _currentMaze.Width / 2;
            int cy = _currentMaze.Height / 2;
            int offsetX = (ghostIndex % 2 == 0) ? ghostIndex / 2 : -(ghostIndex / 2 + 1);
            return _currentMaze.GridToWorld(cx + offsetX, cy);
        }

        public Vector3 GetTargetSpawnPosition()
        {
            if (_currentMaze == null) return Vector3.zero;
            return _currentMaze.GridToWorld(_currentMaze.Width / 2, _currentMaze.Height - 2);
        }

        // --- Player state operations (via IPlayerStateAccess) ---

        /// <summary>
        /// Find all player state accessors in the scene.
        /// Uses GetComponents on NetworkBehaviour objects to find IPlayerStateAccess.
        /// </summary>
        private IPlayerStateAccess[] FindAllPlayers()
        {
            var behaviours = FindObjectsByType<NetworkBehaviour>(FindObjectsSortMode.None);
            var result = new System.Collections.Generic.List<IPlayerStateAccess>();
            foreach (var nb in behaviours)
            {
                if (nb is IPlayerStateAccess psa)
                    result.Add(psa);
            }
            return result.ToArray();
        }

        /// <summary>
        /// Find player by PlayerRef. Returns null if not found.
        /// </summary>
        public IPlayerStateAccess FindPlayer(PlayerRef playerRef)
        {
            foreach (var nb in FindObjectsByType<NetworkBehaviour>(FindObjectsSortMode.None))
            {
                if (nb is IPlayerStateAccess psa && nb.Object != null &&
                    nb.Object.InputAuthority == playerRef)
                    return psa;
            }
            return null;
        }

        public void TeleportPlayer(PlayerRef player, Vector3 destination)
        {
            var psa = FindPlayer(player);
            if (psa == null) return;

            psa.SetPosition(destination.x, destination.y, destination.z);
            Debug.Log($"[NetworkManager] Teleported {player} to {destination}");
        }

        public void SetGhostWailStates()
        {
            foreach (var psa in FindAllPlayers())
            {
                if (psa.GetRole() != PlayerRole.Target && psa.GetRole() != PlayerRole.None)
                    psa.SetWailState(true, GameConstants.GhostBaseSpeed * 0.6f);
            }
        }

        public void ResetGhostWailStates()
        {
            foreach (var psa in FindAllPlayers())
            {
                if (psa.GetRole() != PlayerRole.Target && psa.GetIsInWailState())
                    psa.SetWailState(false, GameConstants.GhostBaseSpeed);
            }
        }

        public void RespawnGhost(PlayerRef ghost)
        {
            var psa = FindPlayer(ghost);
            if (psa == null) return;

            psa.SetAlive(false);
            psa.SetRespawnTimer(3f);
            Debug.Log($"[NetworkManager] Ghost {ghost} eaten — respawning in 3s");
        }

        private void Update()
        {
            if (Runner == null || !Runner.IsServer) return;

            // Check ghost respawn timers
            foreach (var psa in FindAllPlayers())
            {
                if (!psa.GetIsAlive() && psa.IsRespawnExpired())
                {
                    int ghostIndex = (int)psa.GetRole() - 1;
                    var spawnPos = GetGhostSpawnPosition(Mathf.Max(0, ghostIndex));
                    psa.SetAlive(true);
                    psa.SetWailState(false, GameConstants.GhostBaseSpeed);
                    psa.SetPosition(spawnPos.x, spawnPos.y, spawnPos.z);
                    Debug.Log($"[NetworkManager] Ghost {psa.GetRole()} respawned");
                }
            }
        }

        // --- Maze event handlers (bridge Maze → Network) ---

        private PlayerRef FindPlayerRef(GameObject playerGO)
        {
            if (playerGO == null) return default;
            var nb = playerGO.GetComponent<NetworkBehaviour>();
            if (nb?.Object != null)
                return nb.Object.InputAuthority;
            // Check parent (player GO might be a child collider)
            nb = playerGO.GetComponentInParent<NetworkBehaviour>();
            if (nb?.Object != null)
                return nb.Object.InputAuthority;
            return default;
        }

        private void HandleCollectiblePickup(int entityId, GameObject playerGO)
        {
            if (!Runner.IsServer) return;
            var playerRef = FindPlayerRef(playerGO);
            Events?.QueueEvent(GameEvent.Collectible, playerRef, entityId);

            // Remove the collectible visually
            MazeManager.Instance?.RemoveCollectible(entityId);
        }

        private void HandlePowerPelletPickup(int entityId, GameObject playerGO)
        {
            if (!Runner.IsServer) return;
            var playerRef = FindPlayerRef(playerGO);
            Events?.QueueEvent(GameEvent.RoleSwap, playerRef, entityId);

            MazeManager.Instance?.RemoveCollectible(entityId);
        }

        private void HandlePortalEnter(int portalId, GameObject playerGO)
        {
            if (!Runner.IsServer) return;
            var playerRef = FindPlayerRef(playerGO);
            Events?.QueueEvent(GameEvent.Teleport, playerRef, portalId);
        }

        private void HandleCatchAttempt(GameObject ghostGO, GameObject targetGO)
        {
            if (!Runner.IsServer) return;
            var ghostRef = FindPlayerRef(ghostGO);
            Events?.QueueEvent(GameEvent.Catch, ghostRef);
        }

        public MazeGenerator CurrentMaze => _currentMaze;
        public int[,] CurrentGrid => _currentGrid;
    }
}
