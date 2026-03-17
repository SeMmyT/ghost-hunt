using Fusion;
using GhostHunt.Core;
using GhostHunt.Network;
using UnityEngine;

namespace GhostHunt.Roles
{
    /// <summary>
    /// Commander — a 5th ghost role exclusive to flat-screen players (PC/mobile/console).
    /// Instead of chasing through the maze, the Commander sees a full tactical overhead
    /// map and directs the ghost team. Think "Dungeon Master" meets "Chris in the chopper."
    ///
    /// Why this role exists:
    /// - Makes non-VR gameplay distinctly valuable, not a downgrade
    /// - Creates a "coach" dynamic that drives voice chat engagement
    /// - Naturally fits top-down/isometric view that flat platforms already use
    /// - Solves the coordination problem: 4 first-person ghosts can't see each other
    ///
    /// Abilities:
    /// 1. PING — mark a location visible to all ghosts (shared radar highlight)
    /// 2. HAUNT — temporarily darken a corridor section for the target (fog of war)
    /// 3. SHIFT — rotate a maze section 90° (walls physically move, new paths open/close)
    /// 4. REVEAL — briefly show target's exact position to all ghosts (long cooldown)
    ///
    /// The Commander cannot catch the target directly. They win through the team.
    /// </summary>
    public class CommanderRole : NetworkBehaviour
    {
        [Networked] private TickTimer PingCooldown { get; set; }
        [Networked] private TickTimer HauntCooldown { get; set; }
        [Networked] private TickTimer ShiftCooldown { get; set; }
        [Networked] private TickTimer RevealCooldown { get; set; }
        [Networked] private Vector3Net PingPosition { get; set; }
        [Networked] private NetworkBool IsRevealing { get; set; }
        [Networked] private int HauntedCorridorId { get; set; }

        private const float PingCooldownTime = 3f;
        private const float HauntCooldownTime = 15f;
        private const float HauntDuration = 5f;
        private const float ShiftCooldownTime = 25f;
        private const float RevealCooldownTime = 45f;
        private const float RevealDuration = 3f;

        /// <summary>
        /// Mark a location on the shared radar. All ghosts see a pulsing indicator.
        /// Like pinging in League — fast, spammable (with short cooldown), essential.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_Ping(Vector3 worldPosition)
        {
            if (!PingCooldown.ExpiredOrNotRunning(Runner)) return;

            PingCooldown = TickTimer.CreateFromSeconds(Runner, PingCooldownTime);
            PingPosition = new Vector3Net(worldPosition.x, worldPosition.y, worldPosition.z);

            Debug.Log($"[Commander] PING at ({worldPosition.x:F1}, {worldPosition.z:F1})");
            // TODO: Broadcast ping visual to all ghost players' radars
        }

        /// <summary>
        /// Darken a corridor section — target loses visibility in that area.
        /// The target's screen goes darker in the haunted zone. Ghosts are unaffected.
        /// Creates ambush opportunities: Commander haunts a corridor, tells Ambusher to wait there.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_Haunt(int corridorId)
        {
            if (!HauntCooldown.ExpiredOrNotRunning(Runner)) return;

            HauntCooldown = TickTimer.CreateFromSeconds(Runner, HauntCooldownTime);
            HauntedCorridorId = corridorId;

            Debug.Log($"[Commander] HAUNT corridor {corridorId}");
            // TODO: Apply fog-of-war to target in this corridor for HauntDuration
        }

        /// <summary>
        /// Rotate a maze section 90°. Walls physically move. New paths open, old ones close.
        /// This is the power move — changes the entire flow of a chase.
        /// Only affects a small section (3x3 grid area). Cannot shift spawn areas or portals.
        /// Animation: walls grind and rotate over 1 second. Players in the zone are pushed out.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_ShiftMaze(int sectionX, int sectionY, bool clockwise)
        {
            if (!ShiftCooldown.ExpiredOrNotRunning(Runner)) return;

            ShiftCooldown = TickTimer.CreateFromSeconds(Runner, ShiftCooldownTime);

            Debug.Log($"[Commander] SHIFT section ({sectionX},{sectionY}) {(clockwise ? "CW" : "CCW")}");
            // TODO: Rotate 3x3 maze section, update MazeState, push players out of walls
            // This modifies the host-authoritative MazeState and replicates to all clients
        }

        /// <summary>
        /// Briefly reveal the target's exact position to all ghosts.
        /// Target sees a warning flash ("YOU'VE BEEN SPOTTED").
        /// Long cooldown — use it to break a stalemate or coordinate a final push.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_Reveal()
        {
            if (!RevealCooldown.ExpiredOrNotRunning(Runner)) return;

            RevealCooldown = TickTimer.CreateFromSeconds(Runner, RevealCooldownTime);
            IsRevealing = true;

            Debug.Log("[Commander] REVEAL — target position exposed!");
            // TODO: Send target position to all ghost radars for RevealDuration
            // TODO: Flash warning on target's screen
        }

        public override void FixedUpdateNetwork()
        {
            if (!Runner.IsServer) return;

            // End reveal after duration
            if (IsRevealing && RevealCooldown.RemainingTime(Runner) < RevealCooldownTime - RevealDuration)
            {
                IsRevealing = false;
            }
        }
    }
}
