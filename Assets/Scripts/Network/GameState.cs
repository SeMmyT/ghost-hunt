using Fusion;
using GhostHunt.Core;

namespace GhostHunt.Network
{
    /// <summary>
    /// Host-authoritative game state. Replicated to all clients.
    /// Only the host modifies this — clients read only.
    /// </summary>
    public struct GameState : INetworkStruct
    {
        public GamePhase Phase;
        public TickTimer RoundTimer;
        public TickTimer PowerPelletTimer;
        public int CollectiblesRemaining;
        public int TotalCollectibles;
        public int GhostScore;
        public int TargetScore;
        public int MazeSeed; // For procedural generation — set at round start
        public NetworkBool IsPowerPelletActive;
    }

    /// <summary>
    /// Per-player replicated state. Host-authoritative for role data.
    /// Client-authoritative for position (local extrapolation).
    /// </summary>
    public struct PlayerState : INetworkStruct
    {
        public PlayerRole Role;
        public PlatformType Platform;
        public NetworkBool IsAlive;
        public NetworkBool IsInWailState; // Frightened mode ghost
        public Vector3Net Position;
        public QuaternionNet Rotation;
        public float MoveSpeed;
        public TickTimer BurstCooldown;
        public TickTimer RespawnTimer;
    }

    /// <summary>
    /// Role-specific data replicated per player. Host-computed.
    /// </summary>
    public struct RoleData : INetworkStruct
    {
        // Chaser
        public Vector3Net RadarPingPosition; // Target position for Chaser radar

        // Ambusher
        public Vector3Net PredictiveTrailTarget; // Where target is heading
        public Vector3Net StakeoutPosition;
        public NetworkBool HasActiveStakeout;

        // Flanker
        public Vector3Net InterceptPoint; // Computed from Chaser + target

        // Wildcard
        public Vector3Net EctoplasmZoneCenter;
        public float EctoplasmZoneRadius;
        public TickTimer RetreatTimer;
        public NetworkBool IsForceRetreating;

        // Target
        public TickTimer SpeedBoostTimer;
        public TickTimer DecoyTimer;
        public TickTimer TunnelCooldown;
        public int DecoysRemaining;
    }

    /// <summary>
    /// Network-safe Vector3. Photon Fusion requires INetworkStruct fields.
    /// </summary>
    public struct Vector3Net : INetworkStruct
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3Net(float x, float y, float z)
        {
            X = x; Y = y; Z = z;
        }
    }

    /// <summary>
    /// Network-safe Quaternion.
    /// </summary>
    public struct QuaternionNet : INetworkStruct
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public QuaternionNet(float x, float y, float z, float w)
        {
            X = x; Y = y; Z = z; W = w;
        }
    }
}
