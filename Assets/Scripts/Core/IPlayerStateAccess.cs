namespace GhostHunt.Core
{
    /// <summary>
    /// Interface for accessing player state across assembly boundaries.
    /// Implemented by PlayerController (in GhostHunt.Player assembly),
    /// consumed by NetworkManager (in GhostHunt.Network assembly).
    /// Avoids circular assembly dependency: Network cannot ref Player.
    /// </summary>
    public interface IPlayerStateAccess
    {
        PlayerRole GetRole();
        bool GetIsAlive();
        bool GetIsInWailState();
        float GetMoveSpeed();
        void SetAlive(bool alive);
        void SetWailState(bool wailing, float speed);
        void SetRespawnTimer(float seconds);
        bool IsRespawnExpired();
        void SetPosition(float x, float y, float z);
    }
}
