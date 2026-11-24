# NUCLEUS VR development checkpoint

## Implemented

| Area | Files and behavior |
| --- | --- |
| Unity project | `Packages/manifest.json` and `ProjectSettings/` pin Unity 6000.0.72f1, URP, OpenXR, Input System, Netcode for GameObjects, and Unity Transport. `Assets/NucleusVR/Scenes/Boot/Boot.unity` references the palette and direct-player prefab. `Editor/NucleusProjectSetup.cs` configures Quest features and validates Android builds. |
| Core VR | `Scripts/VR/` samples floor-relative head and controller poses. `Scripts/Movement/` resolves tracked hands against surfaces, drives the player capsule at fixed physics ticks, handles tracking loss and room-scale head obstruction, and caps movement and release speeds. Surface settings provide configurable hand slip and spatial contact taps. Snap turning is the default and smooth turning can be enabled. |
| Presentation | `Scripts/Maps/` builds an original facility calibration course, with a timed checkpoint route, physical reset and turn controls, and equipment cases with bounded spring grabs. `Scripts/Player/` builds and poses an original skinned gorilla with tracked elbows and wrists, two animated fingers and an opposable thumb per hand, and a moving 3D jaw. `Scripts/UI/` contains desktop controls and the physical VR session terminal. |
| Multiplayer foundation | `Scripts/Multiplayer/` and `Prefabs/Players/Direct Player.prefab` integrate opted-in direct IPv4 host and join, connection limits, sender-associated pose submissions, server validation, remote interpolation, retained spawn snapshots, and disconnect handling. The jaw expression path is available for a future voice provider. |

The `Scripts/`, `Prefabs/`, and `Editor/` paths in the table are under `Assets/NucleusVR/`.

The uploaded [GorillaLoco+](https://github.com/developer9998/GorillaLocoPlus) Unity 2022 sample served as a feature reference for the adjacent grip and slip plates. NUCLEUS VR implements both behaviors in its own motor and generates the tap audio locally.

## Validation

- The offline Roslyn pass parses project C# under Unity Editor and Android symbol profiles. It checks language syntax and does not load Unity assemblies or run Unity's compiler pipeline.
- The static project verifier checks serialized references and object IDs, stable metadata, the enabled boot scene, the render pipeline link, and the collision layer assignments.
- A cloud-only offline .NET harness passed 12 checks. Six exercise production calibration progression and timing directly. Six exercise production pose validation using substitute Unity math and serializer declarations. Separate CPU checks covered mesh winding and elbow geometry.
- Managed harnesses with Unity test doubles checked tangential slide, retained push force, edge release, tap debouncing, and audio clip routing and cleanup. Unity physics and spatial playback still require Play Mode validation.
- Unity Edit Mode and Play Mode suites are present for calibration, avatar geometry and posing, hand locomotion, boot wiring, pose validation, and two-endpoint loopback. Unity execution remains unverified. The cloud instance had no Unity Editor or license, and requests to the official Editor download and package registry received proxy 403 responses.
- Android packaging, OpenXR startup, actual headset tracking, LAN interoperability on separate devices, comfort, and the 72 FPS Quest 2 target remain unverified. No Unity runtime bug has been confirmed in this cloud run.

## Resume from here

1. Open the project in Unity 6000.0.72f1 with Android Build Support. Run **Nucleus VR > Configure Project**, open the Boot scene, and run both Test Runner suites. Fix any Unity import, package, shader, or runtime failures, then rerun the affected tests.
2. Play through all four pads on desktop. Check that reaching below a low obstacle cannot send the camera through it, a fast hand sweep does not tunnel through walls, a released hand maintains bounded momentum, and a dropped case collides with the bay.
3. Install the same Android build on a Quest 2, 3, or 3S. Check floor-origin acquisition, left and right hand tracking, tracking loss and recentering, crouch fit, snap pivot, smooth turning, floor push, wall climbing, checkpoint reach, and equipment release. Repeat the loop on Quest 2 at 72 Hz and collect CPU and GPU frame timing, dropped frames, and memory with the platform profiler. Only enable higher refresh rates after profiling.
4. Host and join from separate devices. Check late join, room-full rejection, reconnect, pose jitter under packet loss, and peer removal. Verify the calibration bay remains playable during network failures.
5. Finish Phase 2 with configured Lobby and Relay services, public and private room flows, network object ownership, authoritative grabbing and gameplay state, cosmetics, persistence, and a selected proximity voice provider. Verify these with two peers before extending them to gameplay. Direct sessions currently have no account, service authentication, or room-code flow.
6. Implement the later phases in order. The connected Normal Mode facility, monsters, events, and eight secrets are pending. The anatomical damage system, Be the Monster maps and rounds, and the Sandbox are pending too. Use the verified locomotion rig and server authority boundary as shared foundations.
