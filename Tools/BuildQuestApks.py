#!/usr/bin/env python3
"""Build and verify private Quest APKs for the current and historical NUCLEUS VR versions."""

import argparse
import hashlib
from io import BytesIO
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
from zipfile import BadZipFile, ZipFile

ROOT = Path(__file__).resolve().parents[1]
PROJECT_DIRS = ["Assets", "Packages", "ProjectSettings"]
BUNDLE = "com.nucleusvr.nucleus"
VERSIONS = {"v0.1.0": 1, "v0.1.1": 2, "v0.1.2": 3, "current": 4}
CURRENT_VERSION = "v0.1.3"


def stage(label, destination):
    if label == "current":
        paths = subprocess.check_output(
            ["git", "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", *PROJECT_DIRS],
            cwd=ROOT,
        )
        for entry in dict.fromkeys(os.fsdecode(paths).split("\0")):
            source = ROOT / entry
            if not entry or not source.is_file():
                continue
            if not source.resolve().is_relative_to(ROOT):
                raise ValueError(f"Project input leaves the checkout: {entry}")
            target = destination / entry
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
    else:
        source_zip = subprocess.check_output(
            ["git", "archive", "--format=zip", label, "--", *PROJECT_DIRS], cwd=ROOT
        )
        with ZipFile(BytesIO(source_zip)) as archive:
            for item in archive.infolist():
                if not (destination / item.filename).resolve().is_relative_to(destination):
                    raise ValueError(f"Invalid project archive path: {item.filename}")
            archive.extractall(destination)

    # Preserve each tag's game code. Carry forward only the known import corrections.
    tags = destination / "ProjectSettings/TagManager.asset"
    if label == "v0.1.0":
        content = tags.read_text(encoding="utf-8")
        content = content.replace("  serializedVersion: 2", "  serializedVersion: 3", 1)
        content = re.sub(r'^  -$', '  - ""', content, flags=re.MULTILINE)
        tags.write_text(content, encoding="utf-8")

    settings = destination / "ProjectSettings/ProjectSettings.asset"
    content = settings.read_text(encoding="utf-8")
    if "  editorAssembliesCompatibilityLevel:" not in content:
        content = content.replace("  apiCompatibilityLevel:", "  editorAssembliesCompatibilityLevel: 3\n  apiCompatibilityLevel:", 1)
    version = CURRENT_VERSION if label == "current" else label
    content = re.sub(r"^  bundleVersion: .+$", f"  bundleVersion: {version[1:]}", content, flags=re.MULTILINE)
    content = re.sub(r"^  AndroidBundleVersionCode: .+$", f"  AndroidBundleVersionCode: {VERSIONS[label]}", content, flags=re.MULTILINE)
    settings.write_text(content, encoding="utf-8")

    boot_test = destination / "Assets/NucleusVR/Tests/PlayMode/CalibrationBootTests.cs"
    content = boot_test.read_text(encoding="utf-8")
    content = content.replace(
        "Assert.That(avatar.HasModel, Is.True);",
        "Assert.That(avatar.GetComponentsInChildren<SkinnedMeshRenderer>().Length, Is.EqualTo(2));",
    )
    boot_test.write_text(content, encoding="utf-8")

    loopback_test = destination / "Assets/NucleusVR/Tests/PlayMode/DirectSessionLoopbackTests.cs"
    content = loopback_test.read_text(encoding="utf-8")
    content = content.replace("using UnityEditor;\n", "using AssetDatabase = UnityEditor.AssetDatabase;\n", 1)
    loopback_test.write_text(content, encoding="utf-8")

    shutil.copy2(ROOT / "Tools/Quest/QuestApkBuild.cs", destination / "Assets/NucleusVR/Editor/QuestApkBuild.cs")
    return version


def read_editor_version(project):
    version_file = project / "ProjectSettings/ProjectVersion.txt"
    content = version_file.read_text(encoding="utf-8")
    match = re.search(r"^m_EditorVersion:[ \t]*([0-9]+\.[0-9]+\.[0-9]+[a-z][0-9]+)[ \t]*$", content, re.MULTILINE)
    if match is None:
        raise ValueError(f"Unity Editor version is missing or invalid in {version_file}.")
    return match[1]


def hub_unity_path(editor_version):
    program_files = Path(os.environ.get("ProgramFiles", r"C:\Program Files"))
    return program_files / "Unity" / "Hub" / "Editor" / editor_version / "Editor" / "Unity.exe"


def find_tools(unity, build_tools):
    framework = unity.parent / "Data/UnityReferenceAssemblies/unity-4.8-api"
    if not any(path.is_file() for path in (framework / "Facades").glob("*.dll")):
        raise ValueError(f"Unity Editor references are missing in {framework / 'Facades'}. Reinstall Unity through Hub.")
    android = unity.parent / "Data/PlaybackEngines/AndroidPlayer"
    if not android.is_dir():
        raise ValueError(f"Android Build Support is missing from {unity.parent}.")
    sdk = android / "SDK/build-tools"
    suffix = ".exe" if os.name == "nt" else ""
    if build_tools is None:
        candidates = [
            path for path in sdk.glob("*")
            if re.fullmatch(r"[0-9]+(?:\.[0-9]+){2}", path.name)
            and (path / f"aapt2{suffix}").is_file() and (path / "lib/apksigner.jar").is_file()
        ]
        if not candidates:
            raise ValueError("Unity's Android SDK build tools are missing. Install Android SDK & NDK Tools in Unity Hub.")
        build_tools = max(candidates, key=lambda path: tuple(int(part) for part in path.name.split(".")))
    apksigner = build_tools / "lib/apksigner.jar"
    aapt = build_tools / f"aapt2{suffix}"
    java = android / f"OpenJDK/bin/java{suffix}"
    for path in (apksigner, aapt, java):
        if not path.is_file():
            raise ValueError(f"Android verification tool missing: {path}")
    return java, apksigner, aapt


def verify_apk(artifact, version, code, tools):
    with ZipFile(artifact) as archive:
        files = set(archive.namelist())
        required = {
            "AndroidManifest.xml",
            "lib/arm64-v8a/libunity.so",
            "lib/arm64-v8a/libil2cpp.so",
            "lib/arm64-v8a/libUnityOpenXR.so",
            "lib/arm64-v8a/libopenxr_loader.so",
        }
        if not required.issubset(files) or archive.testzip() is not None:
            raise ValueError(f"APK is damaged or lacks the manifest, ARM64 Unity libraries, or OpenXR runtime: {artifact.name}")
        if any(path.startswith("lib/armeabi") or path.startswith("lib/x86") for path in files):
            raise ValueError(f"Unexpected native architecture: {artifact.name}")

    java, apksigner, aapt = tools
    signatures = subprocess.check_output(
        [str(java), "-jar", str(apksigner), "verify", "--min-sdk-version", "29", "--print-certs", str(artifact)],
        text=True,
    )
    signer = re.search(r"certificate SHA-256 digest: ([a-fA-F0-9]{64})", signatures)
    if signer is None:
        raise ValueError(f"No APK signing certificate was verified: {artifact.name}")
    manifest = subprocess.check_output([str(aapt), "dump", "badging", str(artifact)], text=True)
    expected = f"package: name='{BUNDLE}' versionCode='{code}' versionName='{version[1:]}'"
    if (
        expected not in manifest
        or "sdkVersion:'29'" not in manifest
        or "android.hardware.vr.headtracking" not in manifest
        or "application-debuggable" in manifest
    ):
        raise ValueError(f"APK has unexpected app identity, SDK, VR feature, or development flags: {artifact.name}")
    return signer[1].lower()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", type=Path, help="Override the Hub Editor path for the selected builds")
    parser.add_argument("--output", type=Path, required=True, help="Directory for verified APKs or private staging")
    parser.add_argument("--build-tools", type=Path, help="Override the bundled SDK build-tools directory")
    parser.add_argument("--prepare-only", action="store_true", help="Stage private build inputs; do not run Unity")
    parser.add_argument("versions", nargs="*", metavar="VERSION", help="v0.1.0, v0.1.1, v0.1.2, or current (default: all)")
    args = parser.parse_args()
    labels = list(dict.fromkeys(args.versions or VERSIONS))
    if any(label not in VERSIONS for label in labels):
        parser.error("versions must be v0.1.0, v0.1.1, v0.1.2, or current")

    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    staging = Path(tempfile.mkdtemp(prefix="quest-build-", dir=output if args.prepare_only else None))
    if args.prepare_only:
        for label in labels:
            project = staging / label
            version = stage(label, project)
            print(f"{version} staged privately for Unity {read_editor_version(project)} in {project}")
        return 0

    built = []
    failed = []
    try:
        for label in labels:
            project = staging / label
            try:
                version = stage(label, project)
                editor_version = read_editor_version(project)
                unity = (args.unity or hub_unity_path(editor_version)).resolve()
                if not unity.is_file():
                    raise ValueError(f"Unity {editor_version} not found at {unity}. Pass --unity with the activated Editor path.")
                tools = find_tools(unity, args.build_tools)
                candidate = staging / f"NucleusVR-{version}-Quest.apk"
                log = staging / f"Unity-{version}.log"
                env = os.environ.copy()
                env["NUCLEUS_QUEST_APK_PATH"] = str(candidate)
                print(f"Building {version} for Quest with Unity {editor_version}...", flush=True)
                subprocess.run(
                    [str(unity), "-batchmode", "-nographics", "-quit", "-projectPath", str(project),
                     "-buildTarget", "Android", "-executeMethod", "NucleusVR.Editor.QuestApkBuild.Run",
                     "-logFile", str(log)],
                    env=env,
                    check=True,
                )
                built.append((candidate, verify_apk(candidate, version, VERSIONS[label], tools)))
            except (BadZipFile, OSError, ValueError, subprocess.CalledProcessError) as error:
                failed.append(label)
                print(f"{label} failed: {error}", file=sys.stderr)
        if len({signer for _, signer in built}) > 1:
            raise ValueError("APK signing certificates differ between versions.")
        for candidate, _ in built:
            if (output / candidate.name).exists():
                raise ValueError(f"Output already exists: {output / candidate.name}")
        for candidate, _ in built:
            shutil.move(candidate, output / candidate.name)
            digest = hashlib.sha256()
            with (output / candidate.name).open("rb") as artifact:
                for block in iter(lambda: artifact.read(1024 * 1024), b""):
                    digest.update(block)
            print(f"Verified {candidate.name} · SHA-256 {digest.hexdigest()}")
    except Exception:
        print(f"Private build inputs and Unity logs kept at {staging}", file=sys.stderr)
        raise
    if failed:
        print(f"Private build inputs and Unity logs kept at {staging}", file=sys.stderr)
        return 1
    shutil.rmtree(staging)
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (BadZipFile, OSError, ValueError, subprocess.CalledProcessError) as error:
        print(error, file=sys.stderr)
        sys.exit(1)
