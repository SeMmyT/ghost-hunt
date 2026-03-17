using Fusion;
using GhostHunt.Core;
using GhostHunt.Network;
using UnityEngine;

namespace GhostHunt.Gameplay
{
    /// <summary>
    /// The 7 gameplay quirks that make Ghost Hunt fun on any screen.
    /// Each mechanic is designed to produce clip-worthy "information collapse" moments.
    /// None require VR. All create stories players retell.
    /// </summary>

    // ═══════════════════════════════════════════════════════════════
    // 1. WALL PHASE — ghosts walk through walls briefly
    //    From: Among Us (vents visible but exclusive)
    //    The clip: target sees the shimmer, freezes, then RUNS
    // ═══════════════════════════════════════════════════════════════
    public class WallPhase : NetworkBehaviour
    {
        [Networked] private TickTimer PhaseCooldown { get; set; }
        [Networked] private TickTimer PhaseTimer { get; set; }
        [Networked] private NetworkBool IsPhasing { get; set; }

        private const float PhaseDuration = 3f;
        private const float Cooldown = 15f;
        private const float ShimmerWarning = 0.5f; // Target sees shimmer before ghost emerges

        /// <summary>
        /// Ghost activates wall phase. All players hear the audio cue.
        /// Only the target sees WHICH wall shimmered (0.5s before ghost emerges).
        /// Ghost is semi-transparent and can pass through one wall segment.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_Phase()
        {
            if (!PhaseCooldown.ExpiredOrNotRunning(Runner)) return;

            IsPhasing = true;
            PhaseTimer = TickTimer.CreateFromSeconds(Runner, PhaseDuration);
            PhaseCooldown = TickTimer.CreateFromSeconds(Runner, Cooldown);

            Debug.Log("[WallPhase] Ghost phasing through wall!");
            // Host broadcasts: audio cue to ALL players
            // Host sends: shimmer visual to TARGET only (with 0.5s lead time)
        }

        public override void FixedUpdateNetwork()
        {
            if (!Runner.IsServer) return;

            if (IsPhasing && PhaseTimer.Expired(Runner))
            {
                IsPhasing = false;
                // Ghost becomes solid again — if inside a wall, push to nearest corridor
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 2. VOICE PROXIMITY — target's voice is audible to nearby ghosts
    //    From: Phasmophobia (voice triggers ghost)
    //    The clip: "I think I'm safe—" [ghost appears behind them]
    // ═══════════════════════════════════════════════════════════════
    public class VoiceProximity : NetworkBehaviour
    {
        [Networked] public float VoiceRadius { get; set; }

        private const float WhisperRadius = 6f;   // Quiet voice
        private const float NormalRadius = 12f;    // Normal voice
        private const float ShoutRadius = 20f;     // Loud voice / scream

        /// <summary>
        /// Called by Photon Voice integration.
        /// Measures target's mic amplitude and sets detection radius.
        /// Ghosts within radius see a pulsing indicator on HUD pointing toward voice.
        /// Target has NO indication they're being heard.
        /// </summary>
        public void UpdateVoiceLevel(float amplitude)
        {
            if (amplitude < 0.1f)
                VoiceRadius = 0; // Silent — invisible to ghosts
            else if (amplitude < 0.4f)
                VoiceRadius = WhisperRadius;
            else if (amplitude < 0.7f)
                VoiceRadius = NormalRadius;
            else
                VoiceRadius = ShoutRadius;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 3. LAST WORDS — 2-second voice window after being caught
    //    From: Lethal Company (voice cuts off mid-sentence)
    //    The clip: "GUYS IT'S IN THE— [silence]"
    // ═══════════════════════════════════════════════════════════════
    public class LastWords : NetworkBehaviour
    {
        [Networked] private TickTimer LastWordsTimer { get; set; }
        [Networked] private NetworkBool IsInLastWords { get; set; }

        private const float LastWordsDuration = 2f;

        /// <summary>
        /// Called by GameEventResolver when target is caught.
        /// Target gets 2 seconds of voice before elimination.
        /// No UI notification to any other player — they hear silence and must infer.
        /// Ghost team hears a triumphant audio sting.
        /// </summary>
        public void TriggerLastWords()
        {
            if (!Runner.IsServer) return;

            IsInLastWords = true;
            LastWordsTimer = TickTimer.CreateFromSeconds(Runner, LastWordsDuration);

            Debug.Log("[LastWords] Target has 2 seconds...");
        }

        public override void FixedUpdateNetwork()
        {
            if (!Runner.IsServer) return;

            if (IsInLastWords && LastWordsTimer.Expired(Runner))
            {
                IsInLastWords = false;
                Debug.Log("[LastWords] Voice cut. Target eliminated.");
                // Mute target, transition to spectator
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 4. HAUNTING SURGE — temporary power inversion
    //    From: Midnight Ghost Hunt (midnight flip) + Pac-Man (power pellet)
    //    The clip: ghosts scatter screaming as empowered target charges
    // ═══════════════════════════════════════════════════════════════
    public class HauntingSurge : NetworkBehaviour
    {
        [Networked] private TickTimer SurgeTimer { get; set; }
        [Networked] private TickTimer WarningTimer { get; set; }
        [Networked] public NetworkBool IsSurgeActive { get; set; }
        [Networked] public NetworkBool IsWarningActive { get; set; }

        private const float SurgeDuration = 30f;
        private const float WarningLeadTime = 10f;
        private const float SurgeInterval = 180f; // Every 3 minutes
        private const float TargetSpeedMultiplier = 1.8f;
        private const float GhostStunDuration = 3f;

        /// <summary>
        /// Trigger the surge. 10-second warning, then 30 seconds of inverted power.
        /// Target gains: speed boost + collision stun on ghosts + immunity to catch.
        /// Ghosts must: scatter, hide, or risk being stunned and revealed.
        /// </summary>
        public void TriggerSurge()
        {
            if (!Runner.IsServer) return;

            IsWarningActive = true;
            WarningTimer = TickTimer.CreateFromSeconds(Runner, WarningLeadTime);

            Debug.Log("[Surge] WARNING — haunting surge in 10 seconds!");
            // Broadcast: alarm sound to all players
            // Ghost HUD: "SURGE INCOMING — SCATTER OR HIDE"
            // Target HUD: "POWER RISING..."
        }

        public override void FixedUpdateNetwork()
        {
            if (!Runner.IsServer) return;

            // Warning → Active
            if (IsWarningActive && WarningTimer.Expired(Runner))
            {
                IsWarningActive = false;
                IsSurgeActive = true;
                SurgeTimer = TickTimer.CreateFromSeconds(Runner, SurgeDuration);

                Debug.Log("[Surge] ACTIVE — target is hunting ghosts!");
                // Target speed *= TargetSpeedMultiplier
                // Target collision with ghost = ghost stunned for GhostStunDuration
                // Stunned ghost is REVEALED (glowing) to all players
            }

            // Active → End
            if (IsSurgeActive && SurgeTimer.Expired(Runner))
            {
                IsSurgeActive = false;
                Debug.Log("[Surge] Ended — back to the hunt.");
                // Reset target speed
                // Unstun any remaining stunned ghosts
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 5. MANIFESTATION ENERGY — failure becomes a resource
    //    From: Crawl (wrath accumulation)
    //    The clip: ghost deletes a wall, target runs into a dead end
    // ═══════════════════════════════════════════════════════════════
    public class ManifestationEnergy : NetworkBehaviour
    {
        [Networked] public float Energy { get; set; }

        private const float EnergyPerSecondChasing = 1f;   // Builds while pursuing
        private const float EnergyPerDodge = 15f;           // Bonus when target escapes
        private const float WallDeleteCost = 50f;
        private const float WallDeleteDuration = 10f;

        public override void FixedUpdateNetwork()
        {
            if (!Runner.IsServer) return;

            // Energy builds passively while ghost is near target but can't catch
            // Faster accumulation when target successfully dodges
            Energy = Mathf.Min(Energy + EnergyPerSecondChasing * Runner.DeltaTime, 100f);
        }

        /// <summary>
        /// Spend energy to delete a wall segment for 10 seconds.
        /// Creates a shortcut — opens a new path through the maze.
        /// All players see the wall dissolve with a dithered fade animation.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_DeleteWall(int wallX, int wallY)
        {
            if (Energy < WallDeleteCost) return;

            Energy -= WallDeleteCost;
            Debug.Log($"[Manifestation] Wall ({wallX},{wallY}) deleted for {WallDeleteDuration}s!");
            // Host: modify MazeState, mark wall as temporarily removed
            // Broadcast: wall dissolve animation to all clients
            // After WallDeleteDuration: wall reforms with a rumble animation
        }

        /// <summary>
        /// Called by host when target successfully escapes this ghost.
        /// </summary>
        public void OnTargetDodged()
        {
            Energy = Mathf.Min(Energy + EnergyPerDodge, 100f);
            Debug.Log($"[Manifestation] Dodged! Energy: {Energy:F0}/100");
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 6. QUADRANT LOCKDOWN — coordinated team play reward
    //    From: Pac-Man 99 (train → attack)
    //    The clip: all exits seal, target is trapped, ghosts close in
    // ═══════════════════════════════════════════════════════════════
    public class QuadrantLockdown : NetworkBehaviour
    {
        [Networked] public int PressurePoints { get; set; }
        [Networked] private TickTimer LockdownTimer { get; set; }
        [Networked] public NetworkBool IsLockdownActive { get; set; }

        private const int PointsPerCorner = 1;       // Target forced to change direction
        private const int PointsForLockdown = 3;     // Need 3 corners to earn lockdown
        private const float LockdownDuration = 5f;

        /// <summary>
        /// Called when this ghost forces the target to reverse direction.
        /// "Cornering" = target was moving toward ghost and turned away.
        /// </summary>
        public void OnTargetCornered()
        {
            if (!Runner.IsServer) return;

            PressurePoints += PointsPerCorner;
            Debug.Log($"[Lockdown] Cornered! Points: {PressurePoints}/{PointsForLockdown}");
        }

        /// <summary>
        /// Spend pressure points to seal a maze quadrant for 5 seconds.
        /// All exits from the quadrant close. Target is trapped inside.
        /// Other ghosts see: HUD flash "LOCKDOWN — QUADRANT [NW/NE/SW/SE]"
        /// Target sees: walls slam shut with a dithered crash animation.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_Lockdown(int quadrant)
        {
            if (PressurePoints < PointsForLockdown) return;
            if (IsLockdownActive) return;

            PressurePoints -= PointsForLockdown;
            IsLockdownActive = true;
            LockdownTimer = TickTimer.CreateFromSeconds(Runner, LockdownDuration);

            Debug.Log($"[Lockdown] QUADRANT {quadrant} SEALED for {LockdownDuration}s!");
            // Host: seal all exit corridors from the specified quadrant
            // Broadcast: wall-slam animation + alarm sound
            // All ghosts: "LOCKDOWN ACTIVE — CLOSE IN"
        }

        public override void FixedUpdateNetwork()
        {
            if (!Runner.IsServer) return;

            if (IsLockdownActive && LockdownTimer.Expired(Runner))
            {
                IsLockdownActive = false;
                Debug.Log("[Lockdown] Expired — exits reopened.");
                // Unseal quadrant exits
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // 7. DIRECTOR ROLE — the camera player who films the action
    //    From: Content Warning (SpookTube scoring)
    //    The clip: literally the in-game recording played back
    // ═══════════════════════════════════════════════════════════════
    public class DirectorRole : NetworkBehaviour
    {
        [Networked] public int ViewScore { get; set; }
        [Networked] public NetworkBool IsRecording { get; set; }
        [Networked] private TickTimer BlindedTimer { get; set; }

        private const float CloseEncounterRadius = 5f;
        private const int CloseEncounterBonus = 100;
        private const int CatchFilmedBonus = 500;
        private const int NearMissBonus = 200;
        private const float BlindedDuration = 3f;

        /// <summary>
        /// Director is a non-corporeal 6th player with a floating camera.
        /// Cannot be caught. Cannot directly help either side.
        /// Earns score for filming dramatic moments:
        /// - Close encounters (ghost within 5m of target while camera rolling)
        /// - Catches (filming the exact moment of elimination)
        /// - Near misses (target escapes within 1m of ghost)
        /// Ghosts can BLIND the camera by phasing through it (3s static).
        /// </summary>
        public void OnCloseEncounter()
        {
            if (!IsRecording) return;
            ViewScore += CloseEncounterBonus;
            Debug.Log($"[Director] Close encounter filmed! Score: {ViewScore}");
        }

        public void OnCatchFilmed()
        {
            if (!IsRecording) return;
            ViewScore += CatchFilmedBonus;
            Debug.Log($"[Director] CATCH ON CAMERA! Score: {ViewScore}");
        }

        public void OnNearMiss()
        {
            if (!IsRecording) return;
            ViewScore += NearMissBonus;
            Debug.Log($"[Director] Near miss filmed! Score: {ViewScore}");
        }

        /// <summary>
        /// Ghost passes through the Director's camera — 3s of static.
        /// Tactical: blinds the Director during a critical moment.
        /// Comedy: the Director screams "MY FOOTAGE!"
        /// </summary>
        public void OnGhostBlind()
        {
            BlindedTimer = TickTimer.CreateFromSeconds(Runner, BlindedDuration);
            IsRecording = false;
            Debug.Log("[Director] Camera BLINDED by ghost!");
        }

        public override void FixedUpdateNetwork()
        {
            if (!Runner.IsServer) return;

            if (!IsRecording && BlindedTimer.ExpiredOrNotRunning(Runner))
            {
                IsRecording = true; // Auto-resume recording after blind
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // BONUS: SESSION REPLAY — the Content Warning "upload" moment
    //    Shows all perspectives after the round ends
    //    The clip: watching yourself from the ghost's perspective
    // ═══════════════════════════════════════════════════════════════
    public class SessionReplay : NetworkBehaviour
    {
        /// <summary>
        /// At round end, play back key moments from multiple camera angles.
        /// Highlight reel auto-generated from scored events:
        /// - Every catch attempt (hit or miss)
        /// - Every wall phase
        /// - Every surge collision
        /// - Every lockdown
        /// - The final elimination (all perspectives)
        ///
        /// Players watch together and react. This IS the Content Warning
        /// "upload" screen — the guaranteed clip moment every session.
        ///
        /// Implementation: record transform + state snapshots during gameplay,
        /// replay them with free camera in the replay scene.
        /// </summary>
        public struct ReplayMoment
        {
            public float Timestamp;
            public int EventType; // Matches GameEvent enum
            public Vector3 CameraPosition;
            public Quaternion CameraRotation;
            public int[] InvolvedPlayers;
        }

        // TODO: Accumulate ReplayMoments during gameplay
        // TODO: Sort by score (catch > near miss > lockdown > phase)
        // TODO: Play top 5 moments at round end with cinematic camera
        // TODO: "Share" button exports a 15-second clip
    }
}
