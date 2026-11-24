# NUCLEUS VR

A Unity 6 multiplayer VR horror project for Meta Quest 2, 3, and 3S. The current build starts in the Nucleus Research Facility calibration bay. It exercises hand-driven movement, climbing, physical equipment, and direct multiplayer pose replication.

## Open the project

1. Install Unity **6000.0.72f1** in Unity Hub. Add **Android Build Support**, **Android SDK & NDK Tools**, and **OpenJDK** for Quest builds.
2. Open the repository root as a Unity project and let Package Manager resolve the pinned dependencies.
3. Open `Assets/NucleusVR/Scenes/Boot/Boot.unity` and enter Play Mode. The scene is included in the project's build scene list.
4. For Quest, run **Nucleus VR > Configure Project**, switch the Build Profile to Android, then build and install the APK. Setup enables the OpenXR loader, Meta Quest support, both Quest Touch controller profiles, and single-pass instanced rendering. The build checks the required setup.

### Windows Editor startup

Run `Open-NucleusVR.cmd` from the extracted project. It checks the installed Unity Editor's reference files before opening the project. Run `Open-NucleusVR.cmd --check` to check without launching Unity. For an Editor installed outside the default Hub folder, pass its full `Unity.exe` path.

If Unity reports a missing `unity-4.8-api\Facades` folder, reinstall **6000.0.72f1** through **Unity Hub > Installs** or install a fresh copy in a separate folder. Run `Open-NucleusVR.cmd --install` to open the official download page. Unity checks both reference profiles at startup, including when the project's Editor compatibility level is set to .NET Standard. [Unity's startup reference check](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.0.72f1/Editor/Mono/Scripting/ScriptCompilation/LoadingAssemblyDefinition.cs#L89-L99)

### Calibration bay

- **Quest** — Push against the floor or a wall with your tracked hands. Crouch to reach the floor. Use the right controller's stick to turn and squeeze grip near an equipment case to pick it up.
- **Desktop** — Select **Enter calibration**. Move the mouse to look, or hold the left or right mouse button and drag that hand. Scroll while dragging to extend its reach. Use **Q / E** to turn, **Ctrl** to crouch, **R** to reset, and **F1** or **Esc** to reopen the controls.
- Touch **START**, then reach the three checkpoints in order. The active pad is amber and completed pads turn green. The start console also has reset and turning controls.
- Try the **GRIP** and **SLIP** benches near the start. Press a hand into each plate and drag it sideways to compare the grip. New contacts with solid surfaces play a spatial tap.

### Direct multiplayer

Choose **Host** in the desktop menu or at the in-world terminal. Join with the host's IPv4 address over UDP **7777**. In VR, touch a terminal key with your hand and squeeze grip. Build both endpoints from the same project revision. For two processes on one computer, join `127.0.0.1` from a second player instance. For a headset and a computer, use the host's LAN address.

Direct sessions replicate head, hands, grip, and avatar expressions for up to 10 players. Equipment, checkpoints, and run timing are local to each player. The two clients must be able to reach each other directly.

## Tests

Run **Window > General > Test Runner** for Edit Mode and Play Mode. The suites cover calibration progression, avatar geometry and posing, locomotion, boot wiring, pose acceptance, and a Unity Transport loopback session.

Run the static asset check from the repository root with Python 3.9 or newer.

```sh
python3 Tools/verify_project.py
```

See [NUCLEUS_PROGRESS.md](NUCLEUS_PROGRESS.md) for validation results, remaining work, and the Quest test sequence.
