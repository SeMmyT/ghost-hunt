#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Fusion;
using GhostHunt.Core;
using GhostHunt.Maze;
using GhostHunt.Network;
using GhostHunt.Player;
using UnityEngine;

namespace GhostHunt.Debug
{
    /// <summary>
    /// TCP debug server for CLI-to-game communication.
    /// Accepts newline-delimited JSON commands on port 9999.
    /// Compiled out of release builds.
    ///
    /// Usage:
    ///   echo '{"type":"spawn_ghost","x":10,"z":5}' | nc localhost 9999
    ///   echo '{"type":"get_state"}' | nc localhost 9999
    ///   echo '{"type":"set_phase","phase":"Frightened"}' | nc localhost 9999
    /// </summary>
    public class DebugServer : MonoBehaviour
    {
        [SerializeField] private int _port = 9999;

        private TcpListener _listener;
        private Thread _listenThread;
        private readonly ConcurrentQueue<PendingCommand> _commandQueue = new();
        private volatile bool _running;

        private GameManager _gameManager;
        private MazeGenerator _lastMaze;

        private struct PendingCommand
        {
            public string Json;
            public StreamWriter Writer;
        }

        private void Start()
        {
            _running = true;
            _listenThread = new Thread(ListenLoop) { IsBackground = true, Name = "DebugServer" };
            _listenThread.Start();
            UnityEngine.Debug.Log($"[DebugServer] Listening on tcp://localhost:{_port}");
        }

        private void ListenLoop()
        {
            _listener = new TcpListener(IPAddress.Loopback, _port);
            _listener.Start();

            while (_running)
            {
                if (!_listener.Pending())
                {
                    Thread.Sleep(10);
                    continue;
                }

                var client = _listener.AcceptTcpClient();
                client.NoDelay = true; // Disable Nagle — we want instant response
                var thread = new Thread(() => HandleClient(client))
                {
                    IsBackground = true,
                    Name = "DebugClient"
                };
                thread.Start();
            }
        }

        private void HandleClient(TcpClient client)
        {
            try
            {
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                writer.WriteLine("{\"status\":\"connected\",\"game\":\"ghost-hunt\",\"port\":" + _port + "}");

                string line;
                while (_running && (line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    // Queue for main thread execution (Unity API is not thread-safe)
                    var done = new ManualResetEventSlim(false);
                    string result = null;

                    _commandQueue.Enqueue(new PendingCommand { Json = line, Writer = writer });
                }
            }
            catch (IOException) { /* client disconnected */ }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[DebugServer] Client error: {e.Message}");
            }
            finally
            {
                client?.Close();
            }
        }

        private void Update()
        {
            // Cache references lazily
            if (_gameManager == null)
                _gameManager = FindFirstObjectByType<GameManager>();

            // Process queued commands on main thread
            while (_commandQueue.TryDequeue(out var cmd))
            {
                string response = Dispatch(cmd.Json);
                try { cmd.Writer.WriteLine(response); }
                catch { /* client gone */ }
            }
        }

        // --- Command dispatch ---

        private string Dispatch(string json)
        {
            try
            {
                var cmd = JsonUtility.FromJson<CommandEnvelope>(json);
                if (string.IsNullOrEmpty(cmd.type))
                    return Error("missing 'type' field");

                return cmd.type switch
                {
                    "spawn_ghost" => CmdSpawnGhost(json),
                    "get_state" => CmdGetState(),
                    "set_phase" => CmdSetPhase(json),
                    "set_maze_params" => CmdSetMazeParams(json),
                    "trigger_event" => CmdTriggerEvent(json),
                    "generate_maze" => CmdGenerateMaze(json),
                    "ping" => Ok("pong"),
                    "help" => CmdHelp(),
                    _ => Error($"unknown command: {cmd.type}")
                };
            }
            catch (Exception e)
            {
                return Error(e.Message);
            }
        }

        // --- Commands ---

        private string CmdSpawnGhost(string json)
        {
            var args = JsonUtility.FromJson<SpawnGhostArgs>(json);

            var position = new Vector3(args.x, args.y, args.z);
            var role = ParseRole(args.role);

            // Create a ghost GameObject at the specified position
            var ghost = new GameObject($"DebugGhost_{role}");
            ghost.transform.position = position;

            // Add PlayerController if available, otherwise just mark it
            var controller = ghost.AddComponent<PlayerController>();

            // Layer for identification (layer 8 = Ghost, set up by ProjectSetup)
            int ghostLayer = LayerMask.NameToLayer("Ghost");
            if (ghostLayer >= 0) ghost.layer = ghostLayer;

            UnityEngine.Debug.Log($"[DebugServer] Spawned {role} ghost at ({args.x}, {args.y}, {args.z})");

            return Ok($"spawned {role} at ({args.x:F1}, {args.y:F1}, {args.z:F1})",
                "id", ghost.GetInstanceID().ToString());
        }

        private string CmdGetState()
        {
            if (_gameManager == null)
                return Error("GameManager not found");

            var state = _gameManager.State;
            float? remaining = state.RoundTimer.RemainingTime(_gameManager.Runner);

            return JsonUtility.ToJson(new StateResponse
            {
                ok = true,
                phase = state.Phase.ToString(),
                ghostScore = state.GhostScore,
                targetScore = state.TargetScore,
                collectiblesRemaining = state.CollectiblesRemaining,
                totalCollectibles = state.TotalCollectibles,
                mazeSeed = state.MazeSeed,
                roundTimeRemaining = remaining ?? 0f,
                isPowerPelletActive = state.IsPowerPelletActive
            });
        }

        private string CmdSetPhase(string json)
        {
            if (_gameManager == null)
                return Error("GameManager not found");

            var args = JsonUtility.FromJson<SetPhaseArgs>(json);
            if (!Enum.TryParse<GamePhase>(args.phase, true, out var phase))
                return Error($"invalid phase: {args.phase}. Valid: {string.Join(", ", Enum.GetNames(typeof(GamePhase)))}");

            var state = _gameManager.State;
            state.Phase = phase;
            _gameManager.State = state;

            UnityEngine.Debug.Log($"[DebugServer] Phase → {phase}");
            return Ok($"phase set to {phase}");
        }

        private string CmdSetMazeParams(string json)
        {
            var args = JsonUtility.FromJson<MazeParamsArgs>(json);
            int width = args.width > 0 ? args.width : 29;
            int height = args.height > 0 ? args.height : 31;
            int seed = args.seed;

            _lastMaze = new MazeGenerator(width, height, seed);
            var grid = _lastMaze.Generate();

            int corridors = 0;
            for (int x = 0; x < _lastMaze.Width; x++)
                for (int y = 0; y < _lastMaze.Height; y++)
                    if (grid[x, y] != MazeGenerator.Wall)
                        corridors++;

            UnityEngine.Debug.Log($"[DebugServer] Generated maze {_lastMaze.Width}x{_lastMaze.Height} seed={seed} corridors={corridors}");
            return Ok($"maze generated {_lastMaze.Width}x{_lastMaze.Height}",
                "corridors", corridors.ToString());
        }

        private string CmdTriggerEvent(string json)
        {
            var args = JsonUtility.FromJson<TriggerEventArgs>(json);
            if (!Enum.TryParse<GameEvent>(args.eventName, true, out var gameEvent))
                return Error($"invalid event: {args.eventName}. Valid: {string.Join(", ", Enum.GetNames(typeof(GameEvent)))}");

            // Find the event resolver and queue the event
            var resolver = FindFirstObjectByType<GameEventResolver>();
            if (resolver == null)
                return Error("GameEventResolver not found");

            resolver.QueueEvent(gameEvent, default, args.targetId);
            UnityEngine.Debug.Log($"[DebugServer] Queued event: {gameEvent}");
            return Ok($"event {gameEvent} queued");
        }

        private string CmdGenerateMaze(string json)
        {
            var args = JsonUtility.FromJson<MazeParamsArgs>(json);
            int width = args.width > 0 ? args.width : 29;
            int height = args.height > 0 ? args.height : 31;
            int seed = args.seed;

            var gen = new MazeGenerator(width, height, seed);
            var grid = gen.Generate();

            // Render as ASCII for terminal display
            var sb = new StringBuilder();
            for (int y = 0; y < gen.Height; y++)
            {
                for (int x = 0; x < gen.Width; x++)
                {
                    sb.Append(grid[x, y] switch
                    {
                        MazeGenerator.Wall => '#',
                        MazeGenerator.Collectible => '.',
                        MazeGenerator.PowerPellet => 'O',
                        MazeGenerator.GhostSpawn => 'G',
                        MazeGenerator.TargetSpawn => 'T',
                        MazeGenerator.Portal => 'P',
                        _ => ' '
                    });
                }
                sb.Append('\n');
            }

            return JsonUtility.ToJson(new MazeResponse
            {
                ok = true,
                width = gen.Width,
                height = gen.Height,
                seed = seed,
                ascii = sb.ToString()
            });
        }

        private string CmdHelp()
        {
            return JsonUtility.ToJson(new HelpResponse
            {
                ok = true,
                commands = "spawn_ghost, get_state, set_phase, set_maze_params, trigger_event, generate_maze, ping, help",
                examples = "spawn_ghost: {\"type\":\"spawn_ghost\",\"x\":10,\"z\":5,\"role\":\"Chaser\"}\n" +
                           "get_state: {\"type\":\"get_state\"}\n" +
                           "set_phase: {\"type\":\"set_phase\",\"phase\":\"Frightened\"}\n" +
                           "trigger_event: {\"type\":\"trigger_event\",\"eventName\":\"Catch\"}\n" +
                           "generate_maze: {\"type\":\"generate_maze\",\"seed\":42,\"width\":29,\"height\":31}"
            });
        }

        // --- Helpers ---

        private static PlayerRole ParseRole(string role)
        {
            if (string.IsNullOrEmpty(role)) return PlayerRole.Chaser;
            if (Enum.TryParse<PlayerRole>(role, true, out var parsed)) return parsed;
            return PlayerRole.Chaser;
        }

        private static string Ok(string message, string extraKey = null, string extraValue = null)
        {
            if (extraKey != null)
                return $"{{\"ok\":true,\"message\":\"{Escape(message)}\",\"{extraKey}\":\"{Escape(extraValue)}\"}}";
            return $"{{\"ok\":true,\"message\":\"{Escape(message)}\"}}";
        }

        private static string Error(string message)
        {
            return $"{{\"ok\":false,\"error\":\"{Escape(message)}\"}}";
        }

        private static string Escape(string s)
        {
            return s?.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") ?? "";
        }

        private void OnDestroy()
        {
            _running = false;
            _listener?.Stop();
            UnityEngine.Debug.Log("[DebugServer] Stopped");
        }

        // --- JSON DTOs (JsonUtility requires serializable classes) ---

        [Serializable] private class CommandEnvelope { public string type; }

        [Serializable] private class SpawnGhostArgs
        {
            public string type;
            public float x, y, z;
            public string role;
        }

        [Serializable] private class SetPhaseArgs
        {
            public string type;
            public string phase;
        }

        [Serializable] private class MazeParamsArgs
        {
            public string type;
            public int width, height, seed;
        }

        [Serializable] private class TriggerEventArgs
        {
            public string type;
            public string eventName;
            public int targetId;
        }

        [Serializable] private class StateResponse
        {
            public bool ok;
            public string phase;
            public int ghostScore, targetScore;
            public int collectiblesRemaining, totalCollectibles;
            public int mazeSeed;
            public float roundTimeRemaining;
            public bool isPowerPelletActive;
        }

        [Serializable] private class MazeResponse
        {
            public bool ok;
            public int width, height, seed;
            public string ascii;
        }

        [Serializable] private class HelpResponse
        {
            public bool ok;
            public string commands;
            public string examples;
        }
    }
}
#endif
