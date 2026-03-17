using System.Collections.Generic;
using System.Linq;
using Fusion;
using GhostHunt.Core;
using UnityEngine;

namespace GhostHunt.Network
{
    /// <summary>
    /// Manages lobby state: player list, role assignment, ready checks, countdown.
    /// Host-authoritative. Clients request roles, host approves/assigns.
    /// </summary>
    public class LobbyManager : NetworkBehaviour
    {
        [Networked, Capacity(8)] public NetworkLinkedList<LobbyPlayer> Players => default;
        [Networked] public NetworkBool AllReady { get; set; }
        [Networked] public TickTimer CountdownTimer { get; set; }
        [Networked] public int CountdownSeconds { get; set; }

        public const float CountdownDuration = 5f;

        public struct LobbyPlayer : INetworkStruct
        {
            public PlayerRef Ref;
            public PlayerRole RequestedRole;
            public PlayerRole AssignedRole;
            public PlatformType Platform;
            public NetworkBool IsReady;
            public NetworkString<_16> DisplayName;
        }

        public override void FixedUpdateNetwork()
        {
            if (!Runner.IsServer) return;

            CheckAllReady();
            UpdateCountdown();
        }

        /// <summary>
        /// Called when a new player joins. Host adds them to lobby list.
        /// </summary>
        public void AddPlayer(PlayerRef player, PlatformType platform, string name)
        {
            if (!Runner.IsServer) return;

            Players.Add(new LobbyPlayer
            {
                Ref = player,
                RequestedRole = PlayerRole.None,
                AssignedRole = PlayerRole.None,
                Platform = platform,
                IsReady = false,
                DisplayName = name
            });

            Debug.Log($"[Lobby] {name} joined on {platform}. {Players.Count}/{GameConstants.MaxPlayers}");
        }

        /// <summary>
        /// Client requests a role. Host validates and assigns.
        /// Any role from any device — asymmetry is information, not device lock.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_RequestRole(PlayerRef player, PlayerRole role)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players[i];
                if (p.Ref != player) continue;

                // Check if role is available
                if (role != PlayerRole.Target && IsRoleTaken(role))
                {
                    Debug.Log($"[Lobby] {p.DisplayName} requested {role} — already taken");
                    return;
                }

                // Target role: max 2 targets
                if (role == PlayerRole.Target && CountTargets() >= GameConstants.MaxTargets)
                {
                    Debug.Log($"[Lobby] {p.DisplayName} requested Target — max targets reached");
                    return;
                }

                p.RequestedRole = role;
                p.AssignedRole = role;
                Players.Set(i, p);
                Debug.Log($"[Lobby] {p.DisplayName} assigned {role}");
                return;
            }
        }

        /// <summary>
        /// Client toggles ready state.
        /// </summary>
        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        public void RPC_ToggleReady(PlayerRef player)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players[i];
                if (p.Ref != player) continue;

                p.IsReady = !p.IsReady;
                Players.Set(i, p);
                return;
            }
        }

        /// <summary>
        /// Auto-assign roles to players who haven't chosen.
        /// Called when countdown starts.
        /// </summary>
        private void AutoAssignRoles()
        {
            var availableGhostRoles = new Queue<PlayerRole>();
            availableGhostRoles.Enqueue(PlayerRole.Chaser);
            availableGhostRoles.Enqueue(PlayerRole.Ambusher);
            availableGhostRoles.Enqueue(PlayerRole.Flanker);
            availableGhostRoles.Enqueue(PlayerRole.Wildcard);

            // Remove already-taken ghost roles
            for (int i = 0; i < Players.Count; i++)
            {
                var role = Players[i].AssignedRole;
                if (role != PlayerRole.None && role != PlayerRole.Target)
                {
                    var temp = new Queue<PlayerRole>(availableGhostRoles.Where(r => r != role));
                    availableGhostRoles = temp;
                }
            }

            // Ensure at least one target
            bool hasTarget = false;
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].AssignedRole == PlayerRole.Target)
                    hasTarget = true;
            }

            for (int i = 0; i < Players.Count; i++)
            {
                var p = Players[i];
                if (p.AssignedRole != PlayerRole.None) continue;

                if (!hasTarget)
                {
                    // First unassigned player becomes target (or bot fills in)
                    // For now, assign as ghost — bot is default target
                    // p.AssignedRole = PlayerRole.Target;
                    // hasTarget = true;
                }

                if (availableGhostRoles.Count > 0)
                {
                    p.AssignedRole = availableGhostRoles.Dequeue();
                }
                else
                {
                    p.AssignedRole = PlayerRole.Chaser; // Fallback: duplicate Chaser
                }

                Players.Set(i, p);
            }
        }

        private void CheckAllReady()
        {
            if (Players.Count < GameConstants.MinPlayers)
            {
                AllReady = false;
                return;
            }

            bool allReady = true;
            for (int i = 0; i < Players.Count; i++)
            {
                if (!Players[i].IsReady)
                {
                    allReady = false;
                    break;
                }
            }

            if (allReady && !AllReady)
            {
                AllReady = true;
                AutoAssignRoles();
                CountdownTimer = TickTimer.CreateFromSeconds(Runner, CountdownDuration);
                CountdownSeconds = (int)CountdownDuration;
                Debug.Log("[Lobby] All ready! Starting countdown...");
            }
            else if (!allReady && AllReady)
            {
                AllReady = false;
                CountdownTimer = default;
                Debug.Log("[Lobby] Ready cancelled — not all players ready");
            }
        }

        private void UpdateCountdown()
        {
            if (!AllReady) return;

            float remaining = CountdownTimer.RemainingTime(Runner) ?? 0;
            CountdownSeconds = Mathf.CeilToInt(remaining);

            if (CountdownTimer.Expired(Runner))
            {
                AllReady = false;
                CountdownTimer = default;
                Debug.Log("[Lobby] Countdown complete — starting game!");

                var nm = NetworkManager.Instance;
                if (nm != null)
                    nm.StartGame();
            }
        }

        private bool IsRoleTaken(PlayerRole role)
        {
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].AssignedRole == role)
                    return true;
            }
            return false;
        }

        private int CountTargets()
        {
            int count = 0;
            for (int i = 0; i < Players.Count; i++)
            {
                if (Players[i].AssignedRole == PlayerRole.Target)
                    count++;
            }
            return count;
        }
    }
}
