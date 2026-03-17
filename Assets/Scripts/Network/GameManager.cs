using Fusion;
using GhostHunt.Core;
using GhostHunt.Maze;
using UnityEngine;

namespace GhostHunt.Network
{
    /// <summary>
    /// Top-level game flow controller. Host-authoritative.
    /// Manages phase transitions, round lifecycle, scoring.
    /// </summary>
    public class GameManager : NetworkBehaviour
    {
        [Networked] public GameState State { get; set; }

        [SerializeField] private NetworkPrefabRef _targetBotPrefab;

        private MazeGenerator _mazeGenerator;
        private GameEventResolver _eventResolver;
        private LobbyManager _lobbyManager;
        private NetworkObject _targetBot;

        public override void Spawned()
        {
            _eventResolver = Runner.GetSingleton<GameEventResolver>();
            _lobbyManager = Runner.GetSingleton<LobbyManager>();

            if (Runner.IsServer)
            {
                var state = State;
                state.Phase = GamePhase.Lobby;
                State = state;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!Runner.IsServer) return;

            switch (State.Phase)
            {
                case GamePhase.Lobby:
                    // Managed by LobbyManager
                    break;
                case GamePhase.Countdown:
                    UpdateCountdown();
                    break;
                case GamePhase.Hunt:
                    UpdateHunt();
                    break;
                case GamePhase.Frightened:
                    UpdateFrightened();
                    break;
                case GamePhase.RoundEnd:
                    UpdateRoundEnd();
                    break;
            }
        }

        /// <summary>
        /// Called by LobbyManager when countdown completes.
        /// </summary>
        public void StartRound()
        {
            if (!Runner.IsServer) return;

            // Generate maze
            int seed = Random.Range(1, 999999);
            _mazeGenerator = new MazeGenerator(29, 31, seed);
            var grid = _mazeGenerator.Generate();

            // Count collectibles
            int collectibles = 0;
            for (int x = 0; x < _mazeGenerator.Width; x++)
                for (int y = 0; y < _mazeGenerator.Height; y++)
                    if (grid[x, y] == MazeGenerator.Collectible || grid[x, y] == MazeGenerator.PowerPellet)
                        collectibles++;

            // Set game state
            var state = State;
            state.Phase = GamePhase.Hunt;
            state.MazeSeed = seed;
            state.TotalCollectibles = collectibles;
            state.CollectiblesRemaining = collectibles;
            state.RoundTimer = TickTimer.CreateFromSeconds(Runner, GameConstants.RoundDuration);
            state.IsPowerPelletActive = false;
            state.GhostScore = 0;
            state.TargetScore = 0;
            State = state;

            // Spawn bot target if no human target player
            SpawnBotIfNeeded();

            Debug.Log($"[GameManager] Round started! Seed: {seed}, Collectibles: {collectibles}");
        }

        private void UpdateCountdown()
        {
            // Transition handled by LobbyManager → StartRound()
        }

        private void UpdateHunt()
        {
            var state = State;

            // Check round timer
            if (state.RoundTimer.Expired(Runner))
            {
                state.Phase = GamePhase.RoundEnd;
                State = state;
                Debug.Log("[GameManager] Time's up! Round over.");
                return;
            }

            // Check if all collectibles eaten
            if (state.CollectiblesRemaining <= 0)
            {
                state.TargetScore += 1000; // Bonus for clearing maze
                state.Phase = GamePhase.RoundEnd;
                State = state;
                Debug.Log("[GameManager] All collectibles eaten! Target wins.");
                return;
            }

            State = state;
        }

        private void UpdateFrightened()
        {
            var state = State;

            if (state.PowerPelletTimer.Expired(Runner))
            {
                state.Phase = GamePhase.Hunt;
                state.IsPowerPelletActive = false;
                State = state;
                Debug.Log("[GameManager] Power pellet expired. Back to hunt.");

                // TODO: Reset all ghost wail states
            }
        }

        private void UpdateRoundEnd()
        {
            // Show score screen for a few seconds, then return to lobby
            // TODO: TickTimer for score screen duration → back to lobby
        }

        /// <summary>
        /// Called by EventResolver when a power pellet is collected.
        /// </summary>
        public void ActivatePowerPellet()
        {
            if (!Runner.IsServer) return;

            var state = State;
            state.Phase = GamePhase.Frightened;
            state.IsPowerPelletActive = true;
            state.PowerPelletTimer = TickTimer.CreateFromSeconds(Runner, GameConstants.PowerPelletDuration);
            State = state;

            Debug.Log("[GameManager] POWER PELLET! Ghosts enter wail state!");
            // TODO: Set all ghost players to wail state
        }

        /// <summary>
        /// Called by EventResolver when a ghost catches the target.
        /// </summary>
        public void OnTargetCaught(PlayerRef catcher)
        {
            if (!Runner.IsServer) return;

            var state = State;
            state.GhostScore += 500;
            state.Phase = GamePhase.RoundEnd;
            State = state;

            Debug.Log($"[GameManager] Target caught by {catcher}! Ghost team wins!");
        }

        /// <summary>
        /// Called by EventResolver when target eats a ghost during frightened mode.
        /// </summary>
        public void OnGhostEaten(PlayerRef ghost)
        {
            if (!Runner.IsServer) return;

            var state = State;
            state.TargetScore += 200;
            State = state;

            Debug.Log($"[GameManager] Ghost {ghost} eaten! Target scores 200.");
            // TODO: Respawn ghost after delay
        }

        private void SpawnBotIfNeeded()
        {
            bool hasHumanTarget = false;
            for (int i = 0; i < _lobbyManager.Players.Count; i++)
            {
                if (_lobbyManager.Players[i].AssignedRole == PlayerRole.Target)
                {
                    hasHumanTarget = true;
                    break;
                }
            }

            if (!hasHumanTarget)
            {
                // Spawn bot at target spawn (bottom center of maze)
                int cx = _mazeGenerator.Width / 2;
                var spawnPos = _mazeGenerator.GridToWorld(cx, _mazeGenerator.Height - 2);
                _targetBot = Runner.Spawn(_targetBotPrefab, spawnPos, Quaternion.identity);

                // Scale difficulty based on ghost count — uses SendMessage to avoid
                // circular assembly dependency (Network cannot reference Target)
                int ghostCount = _lobbyManager.Players.Count;
                int difficulty = Mathf.Clamp(ghostCount + 1, 1, 8);
                _targetBot.SendMessage("SetDifficulty", difficulty, SendMessageOptions.DontRequireReceiver);

                Debug.Log($"[GameManager] Bot target spawned. Difficulty: {difficulty}");
            }
        }
    }
}
