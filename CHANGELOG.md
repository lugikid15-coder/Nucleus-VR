# NUCLEUS VR changelog

## v0.1.3

### Added

- **Normal Mode** opens six connected facility sectors with climbable routes, five original monsters, blackouts, containment alerts, and eight secrets. Players share puzzle progress, and earned discoveries are saved on each device.
- **Be the Monster** adds Backrooms, Factory, and Mines, with map voting, rotating monster selection, timed rounds, and survivor objectives. The monster can strike, grab with both hands, and throw. Solo practice lets players learn the routes.
- **Physics Sandbox** adds spawnable specimens, grabbable practice props, gravity and idle-motion controls, and a reset pad. The anatomy lab and Sandbox offer four gore settings, from Off to High.
- **Online Multiplayer** adds public Quick Join, invite-only rooms, and six-character room codes. Unity Lobby and Relay require a configured Unity Cloud project.

### Changed

- Updated the development project and current Quest build to Unity 6000.6.10f1.

### Fixed

- Fixed the ambiguous `SessionState` reference that could stop Unity from compiling the multiplayer Play Mode tests.
- Players joining an active room receive its mode, objectives, and round state. Anatomy specimens request an up-to-date snapshot when they appear.
- Mode changes and respawns clear held objects and stale hand poses. A survivor grab ends if the monster loses tracking or the round ends.

### Removed

- Removed manual IPv4 entry from the player menus. Public matchmaking and room codes are available in the new online menu. Direct transport remains available for development and tests.

## v0.1.2

### Added

- Added a Windows launcher for opening NUCLEUS VR in Unity 6000.0.72f1. It supports custom Editor paths and provides `--check` and `--install` options.

### Fixed

- Added an early check for missing Editor reference assemblies, including the `unity-4.8-api\Facades` folder. The launcher identifies an incomplete Unity installation before opening the project.

### Removed

- None recorded.

## v0.1.1

### Added

- Added Grip and Slip benches to practice moving on surfaces with different traction.
- Added spatial tap sounds for hand contact and configurable surface slip.

### Fixed

- Fully slippery walls stop the hand from penetrating the surface and release the player's weight. Moving a hand past the edge releases the old contact point.
- Updated the Unity layer settings format and wrote empty slots explicitly to address the `TagManager.asset` import error.

### Removed

- Replaced the third finger on each gorilla hand with an opposable thumb. Each hand has two animated fingers and one thumb.

## v0.1.0

### Added

- Introduced the Calibration Bay with a timed checkpoint course, reset controls, and grabbable equipment cases.
- Added Quest hand-driven movement, wall climbing, crouching, and snap or smooth turning, plus desktop mouse controls.
- Added the original tracked gorilla avatar with animated hands and jaw, and direct IPv4 multiplayer with head and hand synchronization for up to 10 players.

### Fixed

- Initial version.

### Removed

- None recorded.
