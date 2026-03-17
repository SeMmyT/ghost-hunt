namespace GhostHunt.Core
{
    public enum PlayerRole
    {
        None = 0,
        Chaser = 1,    // Blinky archetype — pure speed, always sees target on radar
        Ambusher = 2,  // Pinky archetype — sees predictive trail, stake-out points
        Flanker = 3,   // Inky archetype — sees intercept from Chaser + target
        Wildcard = 4,  // Clyde archetype — area denial, forced retreat
        Target = 10    // The hunted — any device, god-view awareness
    }

    public enum PlatformType
    {
        VR = 0,        // Quest, SteamVR, PSVR2 — first-person immersive
        PC = 1,        // Desktop — third-person/isometric
        Mobile = 2,    // iOS/Android — top-down touch
        Console = 3,   // Switch, Xbox, PS — third-person gamepad
        Browser = 4    // WebGL — top-down, zero install
    }

    public enum GamePhase
    {
        Lobby = 0,
        Countdown = 1,
        Hunt = 2,          // Normal phase — ghosts hunt target
        Frightened = 3,    // Power pellet active — target hunts ghosts
        RoundEnd = 4,
        ScoreScreen = 5
    }

    public enum GameEvent
    {
        Teleport = 40,     // Matches priority constants
        Collectible = 30,
        Catch = 20,
        RoleSwap = 10
    }
}
