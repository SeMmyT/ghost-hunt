using Fusion;
using GhostHunt.Core;
using GhostHunt.Maze;
using UnityEngine;

namespace GhostHunt.Network
{
    /// <summary>
    /// Handles portal/tunnel teleportation.
    /// Portals link opposite edges of the maze (classic Pac-Man wrapping).
    /// VR shimmer effect pre-warms player before teleport to reduce disorientation.
    /// Host-authoritative: teleport events go through GameEventResolver.
    /// </summary>
    public class PortalSystem : NetworkBehaviour
    {
        [SerializeField] private float _portalCooldown = 2f;
        [SerializeField] private float _shimmerWarningDistance = 3f;

        [Networked, Capacity(4)] private NetworkLinkedList<PortalPair> Portals => default;

        public struct PortalPair : INetworkStruct
        {
            public int Id;
            public Vector3Net EntryA;
            public Vector3Net EntryB;
        }

        private GameEventResolver _eventResolver;

        public override void Spawned()
        {
            _eventResolver = Runner.GetSingleton<GameEventResolver>();
        }

        /// <summary>
        /// Register a portal pair. Called during maze generation.
        /// </summary>
        public void RegisterPortal(int id, Vector3 entryA, Vector3 entryB)
        {
            if (!Runner.IsServer) return;

            Portals.Add(new PortalPair
            {
                Id = id,
                EntryA = new Vector3Net(entryA.x, entryA.y, entryA.z),
                EntryB = new Vector3Net(entryB.x, entryB.y, entryB.z)
            });
        }

        /// <summary>
        /// Check if a player is near a portal and trigger teleport.
        /// Called by host each tick for all players.
        /// </summary>
        public void CheckPortalProximity(PlayerRef player, Vector3 playerPos)
        {
            if (!Runner.IsServer) return;

            for (int i = 0; i < Portals.Count; i++)
            {
                var portal = Portals[i];
                Vector3 a = new(portal.EntryA.X, portal.EntryA.Y, portal.EntryA.Z);
                Vector3 b = new(portal.EntryB.X, portal.EntryB.Y, portal.EntryB.Z);

                float distA = Vector3.Distance(playerPos, a);
                float distB = Vector3.Distance(playerPos, b);

                if (distA < 1f)
                {
                    _eventResolver.QueueEvent(GameEvent.Teleport, player, portal.Id);
                    return;
                }
                if (distB < 1f)
                {
                    _eventResolver.QueueEvent(GameEvent.Teleport, player, portal.Id);
                    return;
                }
            }
        }

        /// <summary>
        /// Execute teleport. Called by GameEventResolver after priority resolution.
        /// </summary>
        public Vector3 GetTeleportDestination(int portalId, Vector3 currentPos)
        {
            for (int i = 0; i < Portals.Count; i++)
            {
                var portal = Portals[i];
                if (portal.Id != portalId) continue;

                Vector3 a = new(portal.EntryA.X, portal.EntryA.Y, portal.EntryA.Z);
                Vector3 b = new(portal.EntryB.X, portal.EntryB.Y, portal.EntryB.Z);

                // Teleport to the opposite end
                float distA = Vector3.Distance(currentPos, a);
                return distA < Vector3.Distance(currentPos, b) ? b : a;
            }

            return currentPos; // Fallback: no teleport
        }

        /// <summary>
        /// Get shimmer intensity for VR comfort pre-warning.
        /// Returns 0-1 based on distance to nearest portal.
        /// </summary>
        public float GetShimmerIntensity(Vector3 playerPos)
        {
            float minDist = float.MaxValue;

            for (int i = 0; i < Portals.Count; i++)
            {
                var portal = Portals[i];
                Vector3 a = new(portal.EntryA.X, portal.EntryA.Y, portal.EntryA.Z);
                Vector3 b = new(portal.EntryB.X, portal.EntryB.Y, portal.EntryB.Z);

                minDist = Mathf.Min(minDist, Vector3.Distance(playerPos, a));
                minDist = Mathf.Min(minDist, Vector3.Distance(playerPos, b));
            }

            if (minDist > _shimmerWarningDistance) return 0f;
            return 1f - (minDist / _shimmerWarningDistance);
        }
    }
}
