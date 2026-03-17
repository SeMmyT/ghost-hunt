using System.Collections.Generic;
using Fusion;
using GhostHunt.Core;
using UnityEngine;

namespace GhostHunt.Network
{
    /// <summary>
    /// Host-only deterministic event resolver.
    /// Processes all game events per tick in priority order.
    /// Prevents same-tick conflicts (catch + power pellet on same frame).
    /// </summary>
    public class GameEventResolver : NetworkBehaviour
    {
        private readonly List<PendingEvent> _pendingEvents = new();

        public struct PendingEvent
        {
            public GameEvent Type;
            public PlayerRef Source;
            public int TargetEntityId; // Collectible ID, portal ID, or player ref
            public int Tick;
        }

        /// <summary>
        /// Queue an event for resolution this tick. Host only.
        /// </summary>
        public void QueueEvent(GameEvent type, PlayerRef source, int targetId = -1)
        {
            if (!Runner.IsServer) return;

            _pendingEvents.Add(new PendingEvent
            {
                Type = type,
                Source = source,
                TargetEntityId = targetId,
                Tick = Runner.Tick
            });
        }

        public override void FixedUpdateNetwork()
        {
            if (!Runner.IsServer || _pendingEvents.Count == 0) return;

            // Sort by priority (highest first), then by tick (earliest first)
            _pendingEvents.Sort((a, b) =>
            {
                int priorityCompare = ((int)b.Type).CompareTo((int)a.Type);
                if (priorityCompare != 0) return priorityCompare;
                return a.Tick.CompareTo(b.Tick);
            });

            // Process in order — earlier events can invalidate later ones
            var consumed = new HashSet<int>(); // Track consumed entity IDs

            foreach (var evt in _pendingEvents)
            {
                if (evt.TargetEntityId >= 0 && consumed.Contains(evt.TargetEntityId))
                {
                    Debug.Log($"[EventResolver] Skipping {evt.Type} — entity {evt.TargetEntityId} already consumed this tick");
                    continue;
                }

                ProcessEvent(evt);

                if (evt.TargetEntityId >= 0)
                    consumed.Add(evt.TargetEntityId);
            }

            _pendingEvents.Clear();
        }

        private void ProcessEvent(PendingEvent evt)
        {
            var nm = NetworkManager.Instance;
            var game = nm?.Game;

            switch (evt.Type)
            {
                case GameEvent.Teleport:
                    ProcessTeleport(evt, nm);
                    break;

                case GameEvent.Collectible:
                    ProcessCollectible(evt, game);
                    break;

                case GameEvent.Catch:
                    ProcessCatch(evt, game, nm);
                    break;

                case GameEvent.RoleSwap:
                    ProcessRoleSwap(evt, game, nm);
                    break;
            }
        }

        private void ProcessTeleport(PendingEvent evt, NetworkManager nm)
        {
            var portal = FindFirstObjectByType<PortalSystem>();
            if (portal == null || nm == null) return;

            // Find player position via NetworkBehaviour scan
            Vector3 playerPos = Vector3.zero;
            foreach (var nb in FindObjectsByType<NetworkBehaviour>(FindObjectsSortMode.None))
            {
                if (nb.Object != null && nb.Object.InputAuthority == evt.Source)
                {
                    playerPos = nb.transform.position;
                    break;
                }
            }

            var dest = portal.GetTeleportDestination(evt.TargetEntityId, playerPos);
            nm.TeleportPlayer(evt.Source, dest);
            Debug.Log($"[EventResolver] Teleport: {evt.Source} → {dest}");
        }

        private void ProcessCollectible(PendingEvent evt, GameManager game)
        {
            if (game == null) return;

            var state = game.State;
            state.CollectiblesRemaining--;
            state.TargetScore += 10;
            game.State = state;

            // Check if this was a power pellet
            if (evt.TargetEntityId < 0)
            {
                // Regular collectible
                Debug.Log($"[EventResolver] Collectible picked up by {evt.Source}. {state.CollectiblesRemaining} remaining");
            }
        }

        private void ProcessCatch(PendingEvent evt, GameManager game, NetworkManager nm)
        {
            if (game == null) return;

            var state = game.State;
            if (state.IsPowerPelletActive)
            {
                // Frightened mode — target eats ghost
                game.OnGhostEaten(evt.Source);
                nm?.RespawnGhost(evt.Source);
                Debug.Log($"[EventResolver] Ghost {evt.Source} eaten during power pellet!");
            }
            else
            {
                // Normal mode — ghost catches target
                game.OnTargetCaught(evt.Source);
                Debug.Log($"[EventResolver] Target caught by {evt.Source}!");
            }
        }

        private void ProcessRoleSwap(PendingEvent evt, GameManager game, NetworkManager nm)
        {
            if (game == null) return;

            // Power pellet collected — activate frightened mode
            game.ActivatePowerPellet();
            nm?.SetGhostWailStates();
            Debug.Log($"[EventResolver] Power pellet! Ghosts enter wail state.");
        }
    }
}
