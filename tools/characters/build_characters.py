# Builds AFTERFALL's characters from MakeHuman CC0 system assets with the MPFB Blender extension.
#
#   /Applications/Blender.app/Contents/MacOS/Blender -b --python tools/characters/build_characters.py -- \
#       tools/characters/characters.json Assets/_Afterfall/Resources/Characters [name ...]
#
# For each character in the spec: an MPFB human with the game-engine rig, a proxy body, skin, eyes,
# brows, lashes, hair and clothes; hidden skin under clothes deleted; the base mesh removed; then an
# FBX (armature and meshes, no animation) and <name>.json describing each mesh part's material
# (albedo, normal). Textures are shared between characters, downscaled to 1024 px with macOS's
# sips and written once to Textures/. Only makehuman_system assets (CC0) are accepted.

import json
import os
import subprocess
import sys

import bpy
from bl_ext.blender_org.mpfb.services.humanservice import HumanService
from bl_ext.blender_org.mpfb.services.assetservice import AssetService

MAX_TEXTURE = 1024
# These CC0 outfits print the MakeHuman logo on the chest; characters must not wear a brand.
LOGO_CLOTHES = {"male_casualsuit02", "male_casualsuit04", "male_casualsuit06", "female_casualsuit01", "female_casualsuit02", "female_sportsuit01"}
PACK = os.path.join(bpy.utils.extension_path_user("bl_ext.blender_org.mpfb", path="data", create=False), "packs", "makehuman_system_assets.json")


def args():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if len(argv) < 2:
        raise SystemExit("usage: -- <spec.json> <outdir> [name ...]")
    return argv[0], argv[1], set(argv[2:])


def cc0_assets():
    with open(PACK) as f:
        pack = json.load(f)
    return {k for k, v in pack.items() if v.get("license") == "CC0" and v.get("author") == "makehuman_system"}


def require_cc0(allowed, name):
    if name not in allowed:
        raise SystemExit(f"asset '{name}' is not a CC0 makehuman_system asset")


def clear_scene():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials, bpy.data.images):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def apply_modifier(obj, name):
    with bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj]):
        bpy.ops.object.modifier_apply(modifier=name)


def texture_of(material, want_normal):
    """The image feeding the material's normal map (want_normal) or its colour."""
    if material is None or not material.use_nodes:
        return None
    best = None
    for node in material.node_tree.nodes:
        if node.type != 'TEX_IMAGE' or node.image is None:
            continue
        n = node.image.name.lower()
        is_normal = "normal" in n or "nmap" in n or any(link.to_node.type == 'NORMAL_MAP' for link in node.outputs[0].links)
        if "_ao" in n or "spec" in n or "bump" in n or "rough" in n:
            continue
        if is_normal == want_normal:
            best = node.image
            if not want_normal and "diffuse" in n:
                return best
    return best


def save_texture(image, outdir, written, is_normal):
    if image is None:
        return ""
    base = os.path.splitext(os.path.basename(image.filepath or image.name))[0]
    rel = f"Textures/{base}"
    if rel in written:
        return rel
    src = bpy.path.abspath(image.filepath)
    path = os.path.join(outdir, rel + ".png")
    os.makedirs(os.path.dirname(path), exist_ok=True)
    # Downscale with macOS's sips (Blender will not load the 4096² maps in background mode).
    subprocess.run(["sips", "-Z", str(MAX_TEXTURE), "-s", "format", "png", src, "--out", path], check=True, capture_output=True)
    written.add(rel)
    return rel


def part_kind(obj_name, spec):
    n = obj_name.lower()
    if spec["proxy"].lower() in n:
        return "skin"
    if "eyebrow" in n or "eyelash" in n:
        return "lashes"
    if spec["hair"].lower() in n:
        return "hair"
    if "low-poly" in n or "high-poly" in n:
        return "eyes"
    return "cloth"


def build(spec, outdir, allowed, written):
    for key in [spec["proxy"], spec["skin"], spec["hair"], spec["eyebrows"], spec["eyelashes"], "low-poly"] + spec["clothes"]:
        require_cc0(allowed, key)
    for key in spec["clothes"]:
        if key in LOGO_CLOTHES:
            raise SystemExit(f"'{key}' prints a logo; choose another outfit")
    clear_scene()
    p = dict(spec["phenotype"])
    p.setdefault("cupsize", 0.5)
    p.setdefault("firmness", 0.5)
    info = {
        "name": spec["name"],
        "phenotype": p,
        "rig": "game_engine",
        "eyes": "low-poly.mhclo",
        "eyebrows": spec["eyebrows"] + ".mhclo",
        "eyelashes": spec["eyelashes"] + ".mhclo",
        "hair": spec["hair"] + ".mhclo",
        "proxy": spec["proxy"] + ".proxy",
        "clothes": [c + ".mhclo" for c in spec["clothes"]],
        "skin_mhmat": spec["skin"] + ".mhmat",
        "skin_material_type": "GAMEENGINE",
        "eyes_material_type": "MAKESKIN",
        "clothes_material_type": "MAKESKIN",
    }
    settings = HumanService.get_default_deserialization_settings()
    settings["subdiv_levels"] = 0
    basemesh = HumanService.deserialize_from_dict(info, settings)
    armature = basemesh.parent
    meshes = [o for o in bpy.data.objects if o.type == 'MESH' and o != basemesh]

    parts = []
    for o in meshes:
        kind = part_kind(o.name, spec)
        # Bake the masks (skin hidden under clothes is deleted); keep only the armature modifier.
        for m in list(o.modifiers):
            if m.type == 'MASK':
                apply_modifier(o, m.name)
            elif m.type != 'ARMATURE':
                o.modifiers.remove(m)
        material = o.data.materials[0] if len(o.data.materials) > 0 else None
        part_name = f"{spec['name']}_{kind}" if kind in ("skin", "eyes", "hair") else o.name.replace(".", "_")
        o.name = part_name
        o.data.name = part_name
        albedo = save_texture(texture_of(material, False), outdir, written, False)
        normal = save_texture(texture_of(material, True), outdir, written, True) if kind in ("skin", "cloth") else ""
        o.data.calc_loop_triangles()
        parts.append({"mesh": part_name, "kind": kind, "albedo": albedo, "normal": normal, "triangles": len(o.data.loop_triangles)})
    bpy.data.objects.remove(basemesh, do_unlink=True)
    armature.name = spec["name"]

    bpy.ops.object.select_all(action='DESELECT')
    armature.select_set(True)
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = armature
    fbx = os.path.join(outdir, spec["name"] + ".fbx")
    bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False,
                             bake_anim=False, apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
                             bake_space_transform=False, mesh_smooth_type='FACE', use_armature_deform_only=True, path_mode='STRIP',
                             embed_textures=False, use_mesh_modifiers=False)
    manifest = {
        "name": spec["name"], "role": spec["role"], "female": spec["female"], "rig": "game_engine",
        "bones": len(armature.data.bones), "parts": parts,
        "triangles": sum(p["triangles"] for p in parts),
        "assets": {"proxy": spec["proxy"], "skin": spec["skin"], "hair": spec["hair"], "eyebrows": spec["eyebrows"], "eyelashes": spec["eyelashes"], "clothes": spec["clothes"]},
        "source": "MakeHuman system assets (CC0) via MPFB " + mpfb_version(),
    }
    with open(os.path.join(outdir, spec["name"] + ".json"), "w") as f:
        json.dump(manifest, f, indent=2)
    print(f"BUILT {spec['name']}: {manifest['triangles']} triangles, {len(parts)} parts, {manifest['bones']} bones")


def mpfb_version():
    import tomllib
    path = os.path.join(os.path.dirname(bpy.utils.extension_path_user("bl_ext.blender_org.mpfb", create=False)).replace("/.user", ""), "mpfb", "blender_manifest.toml")
    try:
        with open(path, "rb") as f:
            return tomllib.load(f).get("version", "?")
    except OSError:
        return "?"


def main():
    spec_path, outdir, only = args()
    with open(spec_path) as f:
        specs = json.load(f)["characters"]
    os.makedirs(outdir, exist_ok=True)
    allowed = cc0_assets()
    written = set()
    for spec in specs:
        if only and spec["name"] not in only:
            continue
        build(spec, outdir, allowed, written)


main()
