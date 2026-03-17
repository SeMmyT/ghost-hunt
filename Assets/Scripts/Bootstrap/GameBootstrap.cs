using Fusion;
using GhostHunt.Comfort;
using GhostHunt.Core;
using GhostHunt.Maze;
using GhostHunt.Network;
using GhostHunt.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunt.Bootstrap
{
    /// <summary>
    /// Auto-bootstraps the entire game from any scene. Creates all required
    /// GameObjects, wires systems, generates a maze, and spawns a local player.
    ///
    /// Runs via [RuntimeInitializeOnLoadMethod] — no manual scene setup required.
    /// Open any scene, press Play, and the game is running.
    ///
    /// For multiplayer: disable auto-bootstrap and use the lobby flow instead.
    /// For testing: just press Play — maze generates, player spawns, debug server starts.
    /// </summary>
    public static class GameBootstrap
    {
        private static bool _bootstrapped;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_bootstrapped) return;
            _bootstrapped = true;

            Debug.Log("[Bootstrap] Starting Ghost Hunt...");

            CreateLighting();
            var systems = CreateGameSystems();
            var maze = CreateMaze(systems);
            var player = CreateLocalPlayer(maze);
            CreateCamera(player);
            CreateDebugServer(systems);

            // Auto-start a round for immediate playtesting
            StartTestRound(systems, maze);

            Debug.Log("[Bootstrap] Ghost Hunt ready. Debug server on tcp://localhost:9999");
        }

        // --- Lighting ---

        private static void CreateLighting()
        {
            // Skip if scene already has a light
            if (Object.FindFirstObjectByType<Light>() != null) return;

            var lightGO = new GameObject("DirectionalLight");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(0.9f, 0.9f, 1f);
            light.intensity = 1f;
            light.shadows = LightShadows.None; // No shadows in dithered world
            lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);
        }

        // --- Game Systems ---

        private static GameObject CreateGameSystems()
        {
            var systems = new GameObject("GameSystems");
            Object.DontDestroyOnLoad(systems);

            // NetworkManager creates RoomManager internally
            systems.AddComponent<NetworkManager>();

            // Add a NetworkRunner for the stub (single-player test mode)
            var runner = systems.AddComponent<NetworkRunner>();
            runner.IsServer = true;

            // Add network singletons directly (skip Fusion spawning in test mode)
            systems.AddComponent<GameManager>();
            systems.AddComponent<LobbyManager>();
            systems.AddComponent<GameEventResolver>();
            systems.AddComponent<PortalSystem>();

            Debug.Log("[Bootstrap] Game systems created");
            return systems;
        }

        // --- Maze ---

        private static MazeData CreateMaze(GameObject systems)
        {
            var mazeRoot = new GameObject("Maze");

            var renderer = mazeRoot.AddComponent<MazeRenderer>();
            var manager = mazeRoot.AddComponent<MazeManager>();

            // Generate maze
            int seed = Random.Range(1, 999999);
            var generator = new MazeGenerator(29, 31, seed);
            var grid = generator.Generate();

            // Render it
            renderer.BuildMaze(grid, generator.Width, generator.Height);

            // Initialize runtime (triggers, colliders, AOI)
            manager.InitializeRuntime(generator, grid);

            Debug.Log($"[Bootstrap] Maze generated — seed {seed}, {generator.Width}x{generator.Height}");

            return new MazeData
            {
                Root = mazeRoot,
                Generator = generator,
                Grid = grid
            };
        }

        // --- Local Player ---

        private static GameObject CreateLocalPlayer(MazeData maze)
        {
            // Spawn at target spawn position (bottom center)
            int cx = maze.Generator.Width / 2;
            var spawnPos = maze.Generator.GridToWorld(cx, maze.Generator.Height - 2);
            spawnPos.y = 0.5f; // Slightly above floor

            var player = new GameObject("LocalPlayer");
            player.transform.position = spawnPos;
            player.tag = "Player";

            // CharacterController for physics-based movement
            var cc = player.AddComponent<CharacterController>();
            cc.radius = 0.4f;
            cc.height = 1.8f;
            cc.center = new Vector3(0, 0.9f, 0);
            cc.slopeLimit = 0;
            cc.stepOffset = 0;

            // NetworkObject stub (required by PlayerController)
            player.AddComponent<NetworkObject>();

            // Player controller (handles input + movement)
            var controller = player.AddComponent<PlayerController>();

            // Visible body (capsule)
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.SetParent(player.transform);
            body.transform.localPosition = new Vector3(0, 1f, 0);
            body.transform.localScale = new Vector3(0.6f, 0.5f, 0.6f);
            Object.Destroy(body.GetComponent<Collider>()); // CC handles collision

            // Assign target role for testing (can chase collectibles)
            // Controller.Spawned() will initialize state, but we call it manually
            // since we're not going through Fusion's spawn flow
            controller.Spawned();

            // Set role to Target so we can collect things
            var state = controller.State;
            state.Role = PlayerRole.Target;
            state.MoveSpeed = GameConstants.TargetBaseSpeed;
            state.Platform = PlatformType.PC;
            state.IsAlive = true;
            controller.State = state;

            // VR comfort (auto-disables on non-VR)
            player.AddComponent<VRComfortController>();

            Debug.Log($"[Bootstrap] Player spawned at {spawnPos}");
            return player;
        }

        // --- Camera ---

        private static void CreateCamera(GameObject player)
        {
            // Remove any existing cameras
            foreach (var existingCam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
                Object.Destroy(existingCam.gameObject);

            // Third-person camera
            var camGO = new GameObject("MainCamera");
            camGO.tag = "MainCamera";
            var cam = camGO.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 100f;

            // Audio listener
            camGO.AddComponent<AudioListener>();

            // Position behind and above player
            var cameraController = camGO.AddComponent<CameraController>();
            cameraController.Initialize(player.transform, PlatformType.PC);

            Debug.Log("[Bootstrap] Camera created");
        }

        // --- Debug Server ---

        private static void CreateDebugServer(GameObject systems)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // DebugServer is in GhostHunt.Debug assembly — add via reflection
            // to avoid Bootstrap needing a GhostHunt.Debug assembly reference
            var debugType = System.Type.GetType("GhostHunt.Debug.DebugServer, GhostHunt.Debug");
            if (debugType != null)
            {
                systems.AddComponent(debugType);
                Debug.Log("[Bootstrap] Debug server attached");
            }
            else
            {
                Debug.LogWarning("[Bootstrap] DebugServer type not found — debug server skipped");
            }
#endif
        }

        // --- Test Round ---

        private static void StartTestRound(GameObject systems, MazeData maze)
        {
            var gameManager = systems.GetComponent<GameManager>();
            if (gameManager == null) return;

            // Set up initial game state for testing
            var state = gameManager.State;
            state.Phase = GamePhase.Hunt;
            state.MazeSeed = 0; // Already generated
            state.RoundTimer = TickTimer.CreateFromSeconds(
                systems.GetComponent<NetworkRunner>(),
                GameConstants.RoundDuration);
            state.GhostScore = 0;
            state.TargetScore = 0;

            // Count collectibles in the maze
            int collectibles = 0;
            for (int x = 0; x < maze.Generator.Width; x++)
                for (int y = 0; y < maze.Generator.Height; y++)
                    if (maze.Grid[x, y] == MazeGenerator.Collectible ||
                        maze.Grid[x, y] == MazeGenerator.PowerPellet)
                        collectibles++;

            state.TotalCollectibles = collectibles;
            state.CollectiblesRemaining = collectibles;
            gameManager.State = state;

            Debug.Log($"[Bootstrap] Test round started — {collectibles} collectibles, {GameConstants.RoundDuration}s timer");
        }

        private struct MazeData
        {
            public GameObject Root;
            public MazeGenerator Generator;
            public int[,] Grid;
        }
    }
}
