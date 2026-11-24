#!/usr/bin/env python3
"""Check Unity metadata and serialized references with the Python standard library."""

from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / "Assets"
SETTINGS = ROOT / "ProjectSettings"
BOOT = ASSETS / "NucleusVR/Scenes/Boot/Boot.unity"
GUID = r"[0-9a-f]{32}"
META_GUID = re.compile(rf"^guid: ({GUID})\s*$", re.MULTILINE)
OBJECT = re.compile(r"^--- !u!\d+ &(-?\d+)(?: stripped)?$", re.MULTILINE)
REFERENCE = re.compile(rf"\{{fileID: (-?\d+)(?:, guid: ({GUID}), type: \d+)?\}}")
SCENE_ENTRY = re.compile(
    rf"^  - enabled: ([01])\n    path: ([^\n]+)\n    guid: ({GUID})$",
    re.MULTILINE,
)
SERIALIZED_EXTENSIONS = {".unity", ".prefab", ".asset", ".mat"}

# Source metadata for URP 17.0.4 and NGO 2.4.4, available before Unity resolves packages.
PACKAGE_GUIDS = {
    "650dd9526735d5b46b79224bc6e94025",  # URP Unlit.shader
    "933532a4fcc9baf4fa0491de14d08ed7",  # URP Lit.shader
    "bf2edee5c58d82540a51f03df9d42094",  # UniversalRenderPipelineAsset.cs
    "de640fe3d0db1804a85f9fc8f5cadab6",  # UniversalRendererData.cs
    "d5a57f767e5e46a458fc5d3c628d0cbb",  # NetworkObject.cs
}


def index_metadata(asset_paths, errors):
    guid_to_path = {}
    for path in asset_paths:
        if path.suffix == ".meta":
            if not path.with_suffix("").exists():
                errors.append(f"Orphaned metadata: {path.relative_to(ROOT)}")
            continue

        meta_path = path.with_name(path.name + ".meta")
        match = META_GUID.search(meta_path.read_text(encoding="utf-8")) if meta_path.is_file() else None
        if not match:
            errors.append(f"Missing or invalid metadata: {path.relative_to(ROOT)}")
            continue
        previous = guid_to_path.setdefault(match[1], path)
        if previous != path:
            errors.append(f"Duplicate GUID: {path.relative_to(ROOT)} and {previous.relative_to(ROOT)}")
    return guid_to_path


def load_serialized(paths, errors):
    documents = {}
    object_ids = {}
    for path in paths:
        source = path.read_text(encoding="utf-8")
        ids = OBJECT.findall(source)
        if not ids or len(ids) != len(set(ids)):
            errors.append(f"Missing or duplicate serialized object IDs: {path.relative_to(ROOT)}")
        documents[path] = source
        object_ids[path] = set(ids)
    return documents, object_ids


def check_references(documents, object_ids, guid_to_path, errors):
    unresolved = {}
    count = 0
    for path, source in documents.items():
        for file_id, guid in REFERENCE.findall(source):
            if file_id == "0":
                continue
            count += 1
            if guid:
                target = guid_to_path.get(guid)
                if target is None:
                    if guid not in PACKAGE_GUIDS and not guid.startswith("0000000000000000"):
                        unresolved.setdefault(guid, set()).add(path)
                    continue
                # Unity reserves 100100000 for a source prefab's imported asset.
                if target.suffix == ".prefab" and file_id == "100100000":
                    continue
            else:
                target = path

            if target in object_ids and file_id not in object_ids[target]:
                errors.append(f"{path.relative_to(ROOT)} references missing object {file_id} in {target.relative_to(ROOT)}")
            if target.suffix == ".cs" and file_id != "11500000":
                errors.append(f"{path.relative_to(ROOT)} uses invalid MonoScript object ID {file_id}")

    if unresolved:
        cache = ROOT / "Library/PackageCache"
        for meta_path in cache.rglob("*.meta"):
            match = META_GUID.search(meta_path.read_text(encoding="utf-8", errors="replace"))
            if match:
                unresolved.pop(match[1], None)
            if not unresolved:
                break
    for guid, paths in unresolved.items():
        for path in sorted(paths):
            errors.append(f"{path.relative_to(ROOT)} references unknown GUID {guid}")
    return count


def check_build_settings(documents, guid_to_path, errors):
    build_source = documents.get(SETTINGS / "EditorBuildSettings.asset", "")
    boot_enabled = False
    for enabled, path_string, guid in SCENE_ENTRY.findall(build_source):
        scene_path = ROOT / path_string.strip('"')
        if guid_to_path.get(guid) != scene_path or scene_path not in documents:
            errors.append(f"Build scene GUID does not match {path_string}")
        if enabled == "1" and scene_path == BOOT:
            boot_enabled = True
    if not boot_enabled:
        errors.append("Boot scene is missing from the enabled build scene list.")

    graphics = documents.get(SETTINGS / "GraphicsSettings.asset", "")
    pipeline = re.search(rf"^  m_CustomRenderPipeline: \{{fileID: 11400000, guid: ({GUID}), type: 2\}}$", graphics, re.MULTILINE)
    if not pipeline or guid_to_path.get(pipeline[1]) not in documents:
        errors.append("GraphicsSettings does not reference a serialized render-pipeline asset.")

    tags = documents.get(SETTINGS / "TagManager.asset", "")
    layer_list = tags.partition("  layers:\n")[2].partition("  m_SortingLayers:")[0]
    layers = [line.strip().removeprefix("-").strip() for line in layer_list.splitlines()]
    if len(layers) != 32 or layers[8:12] != ["NucleusCourse", "NucleusPlayer", "NucleusHands", "NucleusProps"]:
        errors.append("TagManager layers 8–11 do not match CalibrationLayers.")


def main():
    errors = []
    asset_paths = sorted(
        path for path in ASSETS.rglob("*")
        if all(not segment.startswith(".") for segment in path.relative_to(ASSETS).parts)
    )
    guid_to_path = index_metadata(asset_paths, errors)
    serialized = [
        path for path in asset_paths if path.is_file() and path.suffix in SERIALIZED_EXTENSIONS
    ] + sorted(SETTINGS.glob("*.asset"))
    documents, object_ids = load_serialized(serialized, errors)
    references = check_references(documents, object_ids, guid_to_path, errors)
    check_build_settings(documents, guid_to_path, errors)

    for error in errors:
        print(error, file=sys.stderr)
    print(f"Checked {len(guid_to_path)} asset GUIDs, {len(documents)} serialized files and {references} references.")
    return 1 if errors else 0


if __name__ == "__main__":
    sys.exit(main())
