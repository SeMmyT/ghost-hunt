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
            switch (evt.Type)
            {
                case GameEvent.Teleport:
                    Debug.Log($"[EventResolver] Teleport by {evt.Source}");
                    // TODO: Move player to portal exit, update position authority
                    break;

                case GameEvent.Collectible:
                    Debug.Log($"[EventResolver] Collectible {evt.TargetEntityId} picked up by {evt.Source}");
                    // TODO: Remove collectible, update score, check power pellet
                    break;

                case GameEvent.Catch:
                    Debug.Log($"[EventResolver] Catch! {evt.Source} caught target");
                    // TODO: End round or respawn target, award points
                    break;

                case GameEvent.RoleSwap:
                    Debug.Log($"[EventResolver] Role swap triggered by {evt.Source}");
                    // TODO: Enter/exit frightened mode
                    break;
            }
        }
    }
}
