# Ghost Hunt

Cross-platform co-op game. Ghosts hunt a target through a dithered 1-bit maze world.
Any device: VR, PC, Switch, mobile, Xbox, PS, browser. 1-8 players.

## Tech Stack
- Unity 6 + URP (mandatory — HDRP can't target Quest/Switch/mobile)
- Photon Fusion 2 (Host Mode, NOT pure relay)
- Native OpenXR (NOT Meta OVR SDK — it blocks non-Meta headsets)
- Surface-Stable Fractal Dithering (alpha clip, not alpha blend)
- Unity Input System (new) with per-platform Control Schemes

## Architecture Rules
- Host is authoritative for ALL game state (catches, collectibles, role swaps)
- Event priority: teleport > collectible > catch > role swap (deterministic)
- Client-authoritative for own position only (local extrapolation)
- Maze walls = AOI boundaries for interest management
- Quest 1 (Snapdragon 835, 72Hz) is the hardware floor
- Must hold locked 72fps on Quest 1 with all systems enabled

## Project Structure
```
Assets/Scripts/
  Core/       — Constants, enums, shared types
  Network/    — Photon Fusion 2, GameState, RoomManager, EventResolver
  Player/     — PlayerController (cross-platform)
  Input/      — PlatformInputProvider (VR/PC/Mobile/Console/Browser)
  Roles/      — Ghost roles (Chaser, Ambusher, Flanker, Wildcard)
  Target/     — Target player + bot AI
  Maze/       — MazeGenerator, maze rendering
  Comfort/    — VR comfort (vignette, snap turn, floating locomotion)
  Shaders/    — Dither post-process (URP Full Screen Pass)
  UI/         — Lobby, HUD, room code entry
```

## Key Research
Full rozvidka synthesis: `~/codeprojects/ghost-hunt-research.md`
GPT architect critique: `~/codeprojects/ghost-hunt-critique.md`

## Platform Gotchas
- Unity 6 defaults to Android API 33; Quest runs API 32 — override to 29
- Xbox requires Unity Pro ($1,800/yr)
- WebGL Photon clients are relay-only (~120ms RTT)
- Console builds need self-hosted CI runners (GameCI doesn't support them)
- Dithered alpha clip solves Quest Adreno transparency bug
