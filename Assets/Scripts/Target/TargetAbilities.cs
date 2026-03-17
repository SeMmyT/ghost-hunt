using Fusion;
using GhostHunt.Core;
using GhostHunt.Network;
using GhostHunt.Player;
using UnityEngine;

namespace GhostHunt.Target
{
    /// <summary>
    /// Active abilities for the target role. Makes the target feel powerful, not passive.
    /// Works on any platform (VR, PC, mobile, console).
    /// </summary>
    public class TargetAbilities : NetworkBehaviour
    {
        [Networked] private TickTimer SpeedBoostTimer { get; set; }
        [Networked] private TickTimer DecoyCooldown { get; set; }
        [Networked] private TickTimer TunnelCooldown { get; set; }
        [Networked] private int DecoysRemaining { get; set; }

        [SerializeField] private float _speedBoostDuration = 3f;
        [SerializeField] private float _speedBoostMultiplier = 1.6f;
        [SerializeField] private float _decoyCooldownTime = 8f;
        [SerializeField] private int _maxDecoys = 3;
        [SerializeField] private NetworkPrefabRef _decoyPrefab;

        private PlayerController _player;

        public override void Spawned()
        {
            _player = GetComponent<PlayerController>();
            if (Runner.IsServer)
            {
                DecoysRemaining = _maxDecoys;
            }
        }

        /// <summary>
        /// Activate speed boost. Short burst of extra speed.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_UseSpeedBoost()
        {
            if (!SpeedBoostTimer.ExpiredOrNotRunning(Runner)) return;

            SpeedBoostTimer = TickTimer.CreateFromSeconds(Runner, _speedBoostDuration);

            var state = _player.State;
            state.MoveSpeed = GameConstants.TargetBaseSpeed * _speedBoostMultiplier;
            _player.State = state;

            Debug.Log("[Target] Speed boost activated!");
        }

        /// <summary>
        /// Drop a decoy that appears as the target on ghost radar.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_DropDecoy()
        {
            if (DecoysRemaining <= 0) return;
            if (!DecoyCooldown.ExpiredOrNotRunning(Runner)) return;

            DecoysRemaining--;
            DecoyCooldown = TickTimer.CreateFromSeconds(Runner, _decoyCooldownTime);

            // Spawn decoy at current position
            var pos = new Vector3(_player.State.Position.X, _player.State.Position.Y, _player.State.Position.Z);
            Runner.Spawn(_decoyPrefab, pos, Quaternion.identity);

            Debug.Log($"[Target] Decoy dropped! {DecoysRemaining} remaining.");
        }

        public override void FixedUpdateNetwork()
        {
            if (!Runner.IsServer) return;

            // Reset speed when boost expires
            if (SpeedBoostTimer.Expired(Runner))
            {
                var state = _player.State;
                state.MoveSpeed = GameConstants.TargetBaseSpeed;
                _player.State = state;
                SpeedBoostTimer = default;
            }
        }
    }

    /// <summary>
    /// Decoy entity. Appears as target on ghost radar for a duration, then fades.
    /// </summary>
    public class Decoy : NetworkBehaviour
    {
        [Networked] private TickTimer LifeTimer { get; set; }
        [SerializeField] private float _lifetime = 5f;

        public override void Spawned()
        {
            if (Runner.IsServer)
            {
                LifeTimer = TickTimer.CreateFromSeconds(Runner, _lifetime);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!Runner.IsServer) return;

            if (LifeTimer.Expired(Runner))
            {
                Runner.Despawn(Object);
            }
        }
    }
}
