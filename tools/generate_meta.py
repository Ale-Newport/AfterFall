#!/usr/bin/env python3
"""Creates missing Unity .meta files under Assets/ with deterministic GUIDs.

Unity generates .meta files itself when it imports assets. This tool exists so the
repository can track stable GUIDs for assets authored outside the Editor (scripts,
assembly definitions, content JSON, UI style sheets). GUIDs are derived from the
asset path, so re-running the tool is idempotent and never changes existing metas.

Usage: python3 tools/generate_meta.py [--check]
  --check  only report missing metas (exit code 1 if any are missing)
"""
import hashlib
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "Assets")

FOLDER = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
SCRIPT = """fileFormatVersion: 2
guid: {guid}
MonoImporter:
  externalObjects: {{}}
  serializedVersion: 2
  defaultReferences: []
  executionOrder: 0
  icon: {{instanceID: 0}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
ASMDEF = """fileFormatVersion: 2
guid: {guid}
AssemblyDefinitionImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
TEXT = """fileFormatVersion: 2
guid: {guid}
TextScriptImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""
USS = """fileFormatVersion: 2
guid: {guid}
ScriptedImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 2
  userData: 
  assetBundleName: 
  assetBundleVariant: 
  script: {{fileID: 12385, guid: 0000000000000000e000000000000000, type: 0}}
  disableValidation: 0
"""
TSS = """fileFormatVersion: 2
guid: {guid}
ScriptedImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 2
  userData: 
  assetBundleName: 
  assetBundleVariant: 
  script: {{fileID: 12388, guid: 0000000000000000e000000000000000, type: 0}}
  disableValidation: 0
  unsupportedSelectorAction: 0
"""
DEFAULT = """fileFormatVersion: 2
guid: {guid}
DefaultImporter:
  externalObjects: {{}}
  userData: 
  assetBundleName: 
  assetBundleVariant: 
"""

TEMPLATES = {
    ".cs": SCRIPT,
    ".asmdef": ASMDEF,
    ".asmref": ASMDEF,
    ".json": TEXT,
    ".txt": TEXT,
    ".md": TEXT,
    ".csv": TEXT,
    ".uss": USS,
    ".tss": TSS,
}

# Asset types whose metas must come from the Unity importer (textures, models, scenes...).
UNITY_ONLY = {".png", ".jpg", ".tga", ".psd", ".fbx", ".obj", ".blend", ".unity", ".prefab", ".mat", ".asset", ".wav", ".ogg", ".mp3", ".ttf", ".otf"}


def guid_for(rel_path: str) -> str:
    return hashlib.md5(("afterfall/" + rel_path.replace(os.sep, "/")).encode("utf-8")).hexdigest()


def template_for(path: str):
    if os.path.isdir(path):
        return FOLDER
    ext = os.path.splitext(path)[1].lower()
    if ext in UNITY_ONLY:
        return None
    return TEMPLATES.get(ext, DEFAULT)


def main() -> int:
    check_only = "--check" in sys.argv
    missing = []
    for dirpath, dirnames, filenames in os.walk(ASSETS):
        dirnames[:] = [d for d in dirnames if not d.startswith(".") and not d.endswith("~")]
        entries = [os.path.join(dirpath, d) for d in dirnames] + [os.path.join(dirpath, f) for f in filenames]
        for path in entries:
            name = os.path.basename(path)
            if name.endswith(".meta") or name.startswith(".") or name == ".DS_Store":
                continue
            meta = path + ".meta"
            if os.path.exists(meta):
                continue
            tmpl = template_for(path)
            if tmpl is None:
                continue
            rel = os.path.relpath(path, ROOT)
            missing.append(rel)
            if not check_only:
                with open(meta, "w", newline="\n") as f:
                    f.write(tmpl.format(guid=guid_for(rel)))
    for m in missing:
        print(("missing: " if check_only else "created: ") + m + ".meta")
    if check_only and missing:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
