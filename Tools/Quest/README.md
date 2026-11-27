# Private Quest builds

Install and activate Unity 6000.6.10f1 for current builds and Unity 6000.0.72f1 for v0.1.0–v0.1.2. Add Android Build Support, Android SDK & NDK Tools, and OpenJDK to each Editor you use. From the development checkout, use Python 3.9+ and Git.

```powershell
py -3 Tools/BuildQuestApks.py --output Build/Quest
```

The script reads the Editor version from each staged project and looks for its Windows Hub installation. For a custom installation, pass its full executable path with `--unity` and select the versions using that Editor. The script accepts `v0.1.0`, `v0.1.1`, `v0.1.2`, and `current`. The working copy stages as `v0.1.3`. Each build uses a private temporary project copy. The script keeps failed copies and logs for diagnosis. Historical builds retain the tagged game code with the known Unity import and test compilation fixes.

Unity's local development key supports sideloading. For recurring distribution, keep one private Android keystore across builds. Set the signing values in the licensed build machine or CI secret store using `NUCLEUS_ANDROID_KEYSTORE`, `NUCLEUS_ANDROID_KEYSTORE_PASSWORD`, `NUCLEUS_ANDROID_KEY_ALIAS`, and `NUCLEUS_ANDROID_KEY_PASSWORD`. A keystore path must point to a file on the machine that runs Unity.

The script verifies the archive, package name, version, ARM64 and OpenXR libraries, Android VR feature, and signature before copying any APK to the output folder. Install and launch each candidate on a Quest before making it a player download. Keep the APKs and their original signing key to support future updates.
