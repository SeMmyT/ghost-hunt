using System.Collections.Generic;
using Fusion;
using GhostHunt.Core;
using GhostHunt.Network;
using UnityEngine;

namespace GhostHunt.Target
{
    /// <summary>
    /// Bot AI for the target role when no human player fills it.
    /// Difficulty scales via pathfinding lookahead depth, not just speed.
    /// Must be challenging enough that co-op vs bot is the primary mode.
    /// </summary>
    public class TargetBot : NetworkBehaviour
    {
        [SerializeField] private int _lookaheadDepth = 4; // Difficulty: 1 (easy) to 8 (hard)
        [SerializeField] private float _decisionInterval = 0.3f;

        [Networked] private Vector3Net CurrentTarget { get; set; }
        [Networked] private int CollectedCount { get; set; }

        private float _decisionTimer;
        private List<Vector3> _nearbyCollectibles = new();
        private List<Vector3> _ghostPositions = new();

        public override void FixedUpdateNetwork()
        {
            if (!Runner.IsServer) return;

            _decisionTimer -= Runner.DeltaTime;
            if (_decisionTimer <= 0)
            {
                _decisionTimer = _decisionInterval;
                MakeDecision();
            }

            MoveTowardTarget();
        }

        private void MakeDecision()
        {
            // Gather ghost positions from network state
            UpdateGhostPositions();
            UpdateNearbyCollectibles();

            if (_nearbyCollectibles.Count == 0) return;

            // Score each reachable collectible by:
            // 1. Distance to collectible (closer = better)
            // 2. Distance from ghosts to collectible (farther from ghosts = safer)
            // 3. Proximity to power pellet (prioritize when ghosts are clustering)

            Vector3 bestTarget = transform.position;
            float bestScore = float.MinValue;

            foreach (var collectible in _nearbyCollectibles)
            {
                float score = ScoreCollectible(collectible);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestTarget = collectible;
                }
            }

            CurrentTarget = new Vector3Net(bestTarget.x, bestTarget.y, bestTarget.z);
        }

        private float ScoreCollectible(Vector3 pos)
        {
            float distanceToCollectible = Vector3.Distance(transform.position, pos);
            float distancePenalty = -distanceToCollectible * 2f;

            // Safety score: how far are ghosts from this collectible?
            float safetyScore = 0f;
            foreach (var ghost in _ghostPositions)
            {
                float ghostDist = Vector3.Distance(ghost, pos);
                safetyScore += Mathf.Clamp(ghostDist, 0, 20f);
            }

            // Lookahead bonus: deeper lookahead considers more collectibles in path
            float lookaheadBonus = _lookaheadDepth * 0.5f;

            return distancePenalty + safetyScore + lookaheadBonus;
        }

        private void MoveTowardTarget()
        {
            Vector3 target = new Vector3(CurrentTarget.X, CurrentTarget.Y, CurrentTarget.Z);
            Vector3 direction = (target - transform.position).normalized;
            transform.position += direction * GameConstants.TargetBaseSpeed * Runner.DeltaTime;
        }

        private void UpdateGhostPositions()
        {
            _ghostPositions.Clear();
            // TODO: Query all PlayerController instances with ghost roles
            // Collect their State.Position into _ghostPositions
        }

        private void UpdateNearbyCollectibles()
        {
            _nearbyCollectibles.Clear();
            // TODO: Query collectible NetworkObjects within lookahead range
            // Range increases with _lookaheadDepth
        }

        /// <summary>
        /// Set difficulty level. Called by host when round starts.
        /// </summary>
        public void SetDifficulty(int level)
        {
            _lookaheadDepth = Mathf.Clamp(level, 1, 8);
            _decisionInterval = Mathf.Lerp(0.5f, 0.15f, (level - 1) / 7f);
        }
    }
}
