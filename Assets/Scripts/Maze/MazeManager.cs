using System;
using System.Collections.Generic;
using UnityEngine;
using GhostHunt.Core;

namespace GhostHunt.Maze
{
    /// <summary>
    /// Runtime maze controller. Manages the live state of the maze during gameplay:
    /// tracks collectibles, detects pickups via trigger colliders, fires events
    /// for the network layer to process.
    ///
    /// Lives in the Maze assembly (no Fusion dependency). Communicates with
    /// the network layer via static C# events — NetworkManager subscribes.
    /// </summary>
    public class MazeManager : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private float _cellSize = 2f;
        [SerializeField] private float _catchRadius = GameConstants.CatchRadius;

        // --- Events (subscribed by NetworkManager) ---

        /// <summary>Fired when a player enters a collectible trigger. Args: entityId, playerGO.</summary>
        public static event Action<int, GameObject> OnCollectiblePickedUp;

        /// <summary>Fired when a player enters a power pellet trigger. Args: entityId, playerGO.</summary>
        public static event Action<int, GameObject> OnPowerPelletPickedUp;

        /// <summary>Fired when a player enters a portal trigger. Args: portalId, playerGO.</summary>
        public static event Action<int, GameObject> OnPortalEntered;

        /// <summary>Fired when a ghost is within catch range of a target. Args: ghostGO, targetGO.</summary>
        public static event Action<GameObject, GameObject> OnCatchAttempt;

        // --- Runtime state ---

        public static MazeManager Instance { get; private set; }

        private readonly Dictionary<int, GameObject> _collectibles = new();
        private readonly Dictionary<int, GameObject> _powerPellets = new();
        private readonly List<PortalTrigger> _portals = new();
        private int _nextEntityId;

        private MazeGenerator _generator;
        private int[,] _grid;
        private MazeAOI _aoi;

        public MazeAOI AOI => _aoi;
        public MazeGenerator Generator => _generator;
        public float CellSize => _cellSize;

        private void Awake()
        {
            Instance = this;
        }

        /// <summary>
        /// Initialize the runtime maze from a generated grid.
        /// Attaches trigger colliders to all interactive elements.
        /// Called after MazeRenderer.BuildMaze() creates the visuals.
        /// </summary>
        public void InitializeRuntime(MazeGenerator generator, int[,] grid)
        {
            _generator = generator;
            _grid = grid;
            _nextEntityId = 0;
            _collectibles.Clear();
            _powerPellets.Clear();
            _portals.Clear();

            // Build AOI for network interest management
            _aoi = new MazeAOI(grid, generator.Width, generator.Height, _cellSize);

            // Attach trigger colliders to spawned objects
            AttachCollectibleTriggers();
            AttachPortalTriggers();
            AttachWallColliders();

            Debug.Log($"[MazeManager] Runtime initialized — {_collectibles.Count} collectibles, " +
                      $"{_powerPellets.Count} pellets, {_portals.Count} portals");
        }

        /// <summary>
        /// Remove a collectible by entity ID. Called after network confirms pickup.
        /// </summary>
        public void RemoveCollectible(int entityId)
        {
            if (_collectibles.TryGetValue(entityId, out var go))
            {
                _collectibles.Remove(entityId);
                Destroy(go);
            }
            else if (_powerPellets.TryGetValue(entityId, out var pellet))
            {
                _powerPellets.Remove(entityId);
                Destroy(pellet);
            }
        }

        /// <summary>
        /// Get world position from grid coordinates.
        /// </summary>
        public Vector3 GridToWorld(int x, int y)
        {
            if (_generator != null)
                return _generator.GridToWorld(x, y, _cellSize);
            return Vector3.zero;
        }

        /// <summary>
        /// Get grid coordinates from world position.
        /// </summary>
        public (int x, int y) WorldToGrid(Vector3 worldPos)
        {
            if (_generator == null) return (0, 0);
            int x = Mathf.RoundToInt(worldPos.x / _cellSize + _generator.Width / 2f);
            int y = Mathf.RoundToInt(worldPos.z / _cellSize + _generator.Height / 2f);
            x = Mathf.Clamp(x, 0, _generator.Width - 1);
            y = Mathf.Clamp(y, 0, _generator.Height - 1);
            return (x, y);
        }

        /// <summary>
        /// Check if a grid cell is walkable (not a wall).
        /// </summary>
        public bool IsWalkable(int x, int y)
        {
            if (_grid == null || x < 0 || x >= _generator.Width || y < 0 || y >= _generator.Height)
                return false;
            return _grid[x, y] != MazeGenerator.Wall;
        }

        // --- Trigger attachment ---

        private void AttachCollectibleTriggers()
        {
            // Find all objects tagged Collectible and PowerPellet by MazeRenderer
            foreach (var go in GameObject.FindGameObjectsWithTag("Collectible"))
            {
                int id = _nextEntityId++;
                _collectibles[id] = go;

                var trigger = go.AddComponent<CollectibleTrigger>();
                trigger.EntityId = id;
                trigger.IsPowerPellet = false;

                EnsureTriggerCollider(go, 0.4f);
            }

            foreach (var go in GameObject.FindGameObjectsWithTag("PowerPellet"))
            {
                int id = _nextEntityId++;
                _powerPellets[id] = go;

                var trigger = go.AddComponent<CollectibleTrigger>();
                trigger.EntityId = id;
                trigger.IsPowerPellet = true;

                EnsureTriggerCollider(go, 0.8f);
            }
        }

        private void AttachPortalTriggers()
        {
            int portalId = 0;
            var portalObjects = GameObject.FindGameObjectsWithTag("Portal");

            // Pair portals (left edge + right edge)
            for (int i = 0; i + 1 < portalObjects.Length; i += 2)
            {
                var a = portalObjects[i];
                var b = portalObjects[i + 1];

                var triggerA = a.AddComponent<PortalTrigger>();
                triggerA.PortalId = portalId;
                triggerA.Destination = b.transform.position;
                _portals.Add(triggerA);

                var triggerB = b.AddComponent<PortalTrigger>();
                triggerB.PortalId = portalId;
                triggerB.Destination = a.transform.position;
                _portals.Add(triggerB);

                EnsureTriggerCollider(a, _cellSize * 0.5f);
                EnsureTriggerCollider(b, _cellSize * 0.5f);

                portalId++;
            }
        }

        private void AttachWallColliders()
        {
            // MazeRenderer creates walls as cubes — ensure they have colliders
            // (CreatePrimitive adds a BoxCollider, but prefabs might not)
            var mazeParent = transform.Find("Maze");
            if (mazeParent == null) return;

            int wallLayer = LayerMask.NameToLayer("Wall");
            foreach (Transform child in mazeParent)
            {
                if (child.gameObject.layer == wallLayer)
                {
                    if (child.GetComponent<Collider>() == null)
                        child.gameObject.AddComponent<BoxCollider>();
                }
            }
        }

        private static void EnsureTriggerCollider(GameObject go, float radius)
        {
            var col = go.GetComponent<Collider>();
            if (col == null)
            {
                var sphere = go.AddComponent<SphereCollider>();
                sphere.radius = radius;
                sphere.isTrigger = true;
            }
            else
            {
                col.isTrigger = true;
            }

            // Ensure a Rigidbody exists for trigger events (kinematic, no gravity)
            if (go.GetComponent<Rigidbody>() == null)
            {
                var rb = go.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        // --- Static event dispatchers (called by trigger components) ---

        internal static void FireCollectiblePickup(int entityId, GameObject player)
            => OnCollectiblePickedUp?.Invoke(entityId, player);

        internal static void FirePowerPelletPickup(int entityId, GameObject player)
            => OnPowerPelletPickedUp?.Invoke(entityId, player);

        internal static void FirePortalEnter(int portalId, GameObject player)
            => OnPortalEntered?.Invoke(portalId, player);

        internal static void FireCatchAttempt(GameObject ghost, GameObject target)
            => OnCatchAttempt?.Invoke(ghost, target);

        private void OnDestroy()
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Trigger component on collectibles and power pellets.
    /// Fires events when a player enters the trigger zone.
    /// </summary>
    public class CollectibleTrigger : MonoBehaviour
    {
        public int EntityId;
        public bool IsPowerPellet;

        private bool _consumed;

        private void OnTriggerEnter(Collider other)
        {
            if (_consumed) return;

            // Only players can pick up collectibles (check for CharacterController or tag)
            if (other.GetComponent<CharacterController>() == null &&
                !other.CompareTag("Player"))
                return;

            _consumed = true;

            if (IsPowerPellet)
                MazeManager.FirePowerPelletPickup(EntityId, other.gameObject);
            else
                MazeManager.FireCollectiblePickup(EntityId, other.gameObject);
        }
    }

    /// <summary>
    /// Trigger component on portal entries.
    /// Fires teleport event when a player enters.
    /// </summary>
    public class PortalTrigger : MonoBehaviour
    {
        public int PortalId;
        public Vector3 Destination;

        private float _cooldownTimer;

        private void OnTriggerEnter(Collider other)
        {
            if (_cooldownTimer > 0) return;

            if (other.GetComponent<CharacterController>() == null &&
                !other.CompareTag("Player"))
                return;

            _cooldownTimer = 2f; // Prevent immediate re-trigger after teleport
            MazeManager.FirePortalEnter(PortalId, other.gameObject);
        }

        private void Update()
        {
            if (_cooldownTimer > 0)
                _cooldownTimer -= Time.deltaTime;
        }
    }

    /// <summary>
    /// Attached to ghost players. Checks distance to targets each frame
    /// and fires catch events when within range.
    /// </summary>
    public class CatchDetector : MonoBehaviour
    {
        [HideInInspector] public float CatchRadius = GameConstants.CatchRadius;
        [HideInInspector] public bool IsGhost = true;

        private float _checkInterval = 0.1f;
        private float _timer;

        private void Update()
        {
            if (!IsGhost) return;

            _timer -= Time.deltaTime;
            if (_timer > 0) return;
            _timer = _checkInterval;

            // Find nearby targets
            var targets = GameObject.FindGameObjectsWithTag("Player");
            foreach (var target in targets)
            {
                if (target == gameObject) continue;

                float dist = Vector3.Distance(transform.position, target.transform.position);
                if (dist <= CatchRadius)
                {
                    MazeManager.FireCatchAttempt(gameObject, target);
                    return; // One catch per check
                }
            }
        }
    }
}
