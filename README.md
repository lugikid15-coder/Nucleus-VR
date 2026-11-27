# NUCLEUS VR

A Unity 6 multiplayer VR horror game for Meta Quest 2, 3, and 3S. Start in the calibration bay to practice hand-driven movement and choose Normal Mode, Be the Monster, or Physics Sandbox.

## Open the project

1. Install Unity **6000.6.10f1** in Unity Hub. Add **Android Build Support**, **Android SDK & NDK Tools**, and **OpenJDK** for Quest builds.
2. Open the repository root as a Unity project and let Package Manager resolve the pinned dependencies.
3. Open `Assets/NucleusVR/Scenes/Boot/Boot.unity` and enter Play Mode. The scene is included in the project's build scene list.
4. For Quest, run **Nucleus VR > Configure Project**, switch the Build Profile to Android, then build and install the APK. Setup enables the OpenXR loader, Meta Quest support, both Quest Touch controller profiles, and single-pass instanced rendering. The build checks the required setup.

### Windows Editor startup

Run `Open-NucleusVR.cmd` from the development checkout. It checks the installed Unity Editor's reference files before opening the project. Run `Open-NucleusVR.cmd --check` to check without launching Unity. For an Editor installed outside the default Hub folder, pass its full `Unity.exe` path.

If Unity reports a missing `unity-4.8-api\Facades` folder, reinstall the project's pinned Editor version through **Unity Hub > Installs** or install a fresh copy in a separate folder. Run `Open-NucleusVR.cmd --install` to open the official download page. Unity checks both reference profiles at startup, including when the project's Editor compatibility level is set to .NET Standard. [Unity's startup reference check](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.6.10f1/Editor/Mono/Scripting/ScriptCompilation/LoadingAssemblyDefinition.cs#L100)

### Calibration bay

- **Quest** — Push against the floor or a wall with your tracked hands. Crouch to reach the floor. Use the right controller's stick to turn and squeeze grip near an equipment case to pick it up.
- **Desktop** — Select **Enter calibration**. Move the mouse to look, or hold the left or right mouse button and drag that hand. Scroll while dragging to extend its reach. Use **Q / E** to turn, **Ctrl** to crouch, **R** to reset, and **F1** or **Esc** to reopen the controls.
- Touch **START**, then reach the three checkpoints in order. The active pad is amber and completed pads turn green. The start console also has reset and turning controls.
- Try the **GRIP** and **SLIP** benches near the start. Press a hand into each plate and drag it sideways to compare the grip. New contacts with solid surfaces play a spatial tap.

### Anatomy lab

Find the **Anatomy lab** at the north end of the calibration bay. Strike the gorilla or containment mutant to injure one body region at a time. The readout shows the damaged region and its remaining function. Heavier strikes can fracture bones and break the joints that connect the head and arms.

- Squeeze either grip near the torso to make the specimen ragdoll and carry it. Hold with both hands for more control. Swing and release to throw. Squeeze near exposed organs to extract them, or near detached anatomy to pick it up.
- On desktop, start a mouse-button drag with the hand clear of the specimen to swing at it. Start the drag while the hand is close to grip it. Release the mouse button to let go.
- Squeeze grip on the **Gore** pad or press **G** on desktop to cycle **Off**, **Low**, **Medium**, and **High**. Off keeps the gameplay damage and hides internal anatomy and blood effects. The **Reset specimens** pad restores both test subjects offline or as the LAN host.

### Game modes

- **Normal Mode** opens six connected facility sectors for free exploration. Grip controls marked in teal. Search for the eight discoveries and investigate the optional Experiment encounter. The room shares puzzle progress and remembers the discoveries each player earns on that device.
- **Be the Monster** starts with map voting. Survivors activate three stations and escape. The selected monster moves faster, strikes with tracked hands, and can hold a nearby survivor with both grips. Swing as you release to throw. Late-joining, captured, and escaped players wait safely in the briefing room. Offline practice lets one player learn the routes.
- **Physics Sandbox** lets the host spawn two anatomy specimens and practice props, change gravity and specimen idle motion, and reset the workshop. Each player controls their own gore detail.

The room host chooses modes from the calibration bay. On desktop, offline shortcuts are **1** for Normal Mode, **2** for Be the Monster, **3** for Sandbox, and **4** to return. During offline map voting, **7** selects Backrooms, **8** selects Factory, and **9** selects Mines.

### Online multiplayer

Open **Online Multiplayer** in the desktop menu or at the terminal beside the starting area. On Quest, touch each terminal key and squeeze grip.

- **Quick Join** finds an available public room. If none is available, it opens one for other players to find.
- **Create Private** opens an invite-only room. Share the six-character room code with your friends.
- **Join by Code** connects to a room using its code. **Leave** exits the current room.

Rooms hold up to 10 players. Unity Lobby tracks membership, Unity Relay carries gameplay traffic over the internet, and the host validates interactions, rewards, combat, and round outcomes. Peers joining a room receive its current mode and state. Calibration equipment, checkpoints, and run timing are local to each player. When the host leaves, players can Quick Join or create another room.

For an online-enabled build, link the Editor project to your Unity Cloud project under **Edit > Project Settings > Services**. Enable Unity Authentication, Lobby, and Relay for that project, and build both players from the same project revision and service environment. The game signs players in anonymously on their first online action. Offline play is available from Boot while the services are unlinked. [Unity Gaming Services setup](https://docs.unity.com/ugs/en-us/manual/overview/manual/getting-started)

## Quest builds

Use the private build pipeline for the current build and all three published tags. Upload verified headset APKs as player downloads. [Build and signing instructions](Tools/Quest/README.md)

## Tests

Run **Window > General > Test Runner** for Edit Mode and Play Mode. The suites cover calibration, avatar posing, locomotion, boot and mode transitions, pose validation, anatomy, facility puzzles, hunt rounds, and Unity Transport loopback.

Run the static asset check from the repository root with Python 3.9 or newer.

```sh
python3 Tools/verify_project.py
```

See [NUCLEUS_PROGRESS.md](NUCLEUS_PROGRESS.md) for validation results, remaining work, and the Quest test sequence.
