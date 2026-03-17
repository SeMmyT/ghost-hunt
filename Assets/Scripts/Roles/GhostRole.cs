using Fusion;
using GhostHunt.Core;
using GhostHunt.Network;
using UnityEngine;

namespace GhostHunt.Roles
{
    /// <summary>
    /// Base ghost behavior. Subclassed by each ghost archetype.
    /// Handles catch detection, burst ability, wail state.
    /// </summary>
    public abstract class GhostRole : NetworkBehaviour
    {
        [Networked] private TickTimer BurstTimer { get; set; }
        [Networked] private NetworkBool IsBursting { get; set; }

        protected PlayerController Player;
        protected GameEventResolver EventResolver;

        public abstract PlayerRole RoleType { get; }

        public override void Spawned()
        {
            Player = GetComponent<PlayerController>();
            EventResolver = Runner.GetSingleton<GameEventResolver>();
        }

        public override void FixedUpdateNetwork()
        {
            if (!Runner.IsServer) return;

            // Check catch range against all targets
            CheckCatchRange();

            // Update role-specific behavior
            UpdateRole();
        }

        protected abstract void UpdateRole();

        private void CheckCatchRange()
        {
            if (Player.State.IsInWailState) return; // Can't catch while frightened

            // TODO: Iterate target players, check distance
            // If within CatchRadius, queue catch event
            // EventResolver.QueueEvent(GameEvent.Catch, Object.InputAuthority);
        }

        /// <summary>
        /// Burst ability — short speed boost on cooldown.
        /// Prevents the "helplessly watching target escape" feeling.
        /// </summary>
        protected void TryBurst()
        {
            if (BurstTimer.ExpiredOrNotRunning(Runner))
            {
                IsBursting = true;
                BurstTimer = TickTimer.CreateFromSeconds(Runner, GameConstants.GhostBurstCooldown);

                var state = Player.State;
                state.MoveSpeed = GameConstants.GhostBaseSpeed * GameConstants.GhostBurstSpeedMultiplier;
                Player.State = state;

                // Reset speed after burst duration (0.5s)
                // TODO: Use TickTimer for burst duration end
            }
        }
    }

    /// <summary>
    /// Chaser (Blinky archetype): Pure speed. Always sees target on radar.
    /// The pressure role — simple but effective.
    /// </summary>
    public class ChaserRole : GhostRole
    {
        public override PlayerRole RoleType => PlayerRole.Chaser;

        protected override void UpdateRole()
        {
            // Chaser always gets target position on radar (host computes)
            // RoleData.RadarPingPosition is updated by host each tick
        }
    }

    /// <summary>
    /// Ambusher (Pinky archetype): Sees predictive trail. Can set stake-out points.
    /// The strategic role — cut off escape routes.
    /// </summary>
    public class AmbusherRole : GhostRole
    {
        public override PlayerRole RoleType => PlayerRole.Ambusher;

        protected override void UpdateRole()
        {
            // Host computes: target position + target velocity * lookahead time
            // Result placed in RoleData.PredictiveTrailTarget
        }
    }

    /// <summary>
    /// Flanker (Inky archetype): Sees intercept point from Chaser + target.
    /// The team-aware role — rewards coordination.
    /// </summary>
    public class FlankerRole : GhostRole
    {
        public override PlayerRole RoleType => PlayerRole.Flanker;

        protected override void UpdateRole()
        {
            // Host computes: vector from Chaser through target, doubled
            // Original Inky AI: target = 2 * (pacman_pos + 2*pacman_dir) - blinky_pos
            // Result placed in RoleData.InterceptPoint
        }
    }

    /// <summary>
    /// Wildcard (Clyde archetype): Area denial + forced retreat.
    /// The chaos role — controls space, periodically vulnerable.
    /// </summary>
    public class WildcardRole : GhostRole
    {
        public override PlayerRole RoleType => PlayerRole.Wildcard;

        protected override void UpdateRole()
        {
            var role = Player.Role;

            // Forced retreat when too close to target (like Clyde's 8-tile scatter)
            // TODO: Check distance to target, if < threshold, enter ForceRetreat

            if (role.IsForceRetreating)
            {
                // Move away from target
                // Retreat timer counts down
            }
        }
    }
}
