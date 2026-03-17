namespace GhostHunt.Core
{
    public static class GameConstants
    {
        // Player counts
        public const int MinPlayers = 1;
        public const int MaxPlayers = 8;
        public const int MaxGhosts = 4;
        public const int MaxTargets = 2;

        // Network
        public const int TickRate = 60;
        public const int MaxCCU = 20; // Photon free tier
        public const float HostMigrationTimeout = 5f;

        // Event priority (deterministic resolution — higher = resolves first)
        public const int PriorityTeleport = 40;
        public const int PriorityCollectible = 30;
        public const int PriorityCatch = 20;
        public const int PriorityRoleSwap = 10;

        // Gameplay
        public const float RoundDuration = 180f; // 3 min rounds
        public const float PowerPelletDuration = 8f;
        public const float GhostBurstCooldown = 12f;
        public const float GhostBurstSpeedMultiplier = 1.8f;
        public const float GhostBaseSpeed = 3.5f;
        public const float TargetBaseSpeed = 4.0f;
        public const float CatchRadius = 1.2f;

        // Comfort (VR)
        public const float VignetteIntensity = 0.4f;
        public const float SnapTurnDegrees = 45f;
        public const float MaxMoveSpeed = 4f; // Cap for sickness reduction

        // Dither
        public const float DitherResolution = 640f; // Obra Dinn reference
    }
}
