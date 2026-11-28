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
#
# Faces: MakeHuman's expression-unit targets (CC0, shipped in MPFB's data) are mixed into a few
# expressions (blink, smile, pain, snarl, mouth_open) and carried from the base mesh onto the body,
# eyes, brows, lashes and clothes as shape keys (FBX blend shapes), before the hidden skin is cut away.
# Distance: every body, clothes, hair and eye part also gets a decimated copy, <part>_lod1, without
# shape keys (brows and lashes have none), for the LODGroup the game builds.
# Set AFTERFALL_FACE_PREVIEW=<dir> to render each expression of each character's face to PNG.

import json
import math
import os
import subprocess
import sys

import bmesh
import bpy
from mathutils import Vector
import bl_ext.blender_org.mpfb as mpfb_package
from bl_ext.blender_org.mpfb.entities.clothes.mhclo import Mhclo
from bl_ext.blender_org.mpfb.services.clothesservice import ClothesService
from bl_ext.blender_org.mpfb.services.humanservice import HumanService
from bl_ext.blender_org.mpfb.services.assetservice import AssetService
from bl_ext.blender_org.mpfb.services.targetservice import TargetService

MAX_TEXTURE = 1024
# These CC0 outfits print the MakeHuman logo on the chest; characters must not wear a brand.
LOGO_CLOTHES = {"male_casualsuit02", "male_casualsuit04", "male_casualsuit06", "female_casualsuit01", "female_casualsuit02", "female_sportsuit01"}
UNITS = os.path.join(os.path.dirname(mpfb_package.__file__), "data", "targets", "expression", "units")
# Expressions as weights of MakeHuman's expression units (targets/expression/units/<race>/<unit>.target.gz).
EXPRESSIONS = {
    "blink": {"eye-left-closure": 1.0, "eye-right-closure": 1.0},
    "smile": {"mouth-corner-puller": 0.8, "mouth-elevation": 0.15, "eye-left-slit": 0.25, "eye-right-slit": 0.25},
    "pain": {"eyebrows-left-down": 0.8, "eyebrows-right-down": 0.8, "eyebrows-left-inner-up": 0.8, "eyebrows-right-inner-up": 0.8,
             "eye-left-slit": 0.85, "eye-right-slit": 0.85, "mouth-retraction": 0.9, "mouth-depression": 0.4, "nose-compression": 0.5},
    "snarl": {"eyebrows-left-down": 0.9, "eyebrows-right-down": 0.9, "nose-left-elevation": 0.7, "nose-right-elevation": 0.7,
              "mouth-upward-retraction": 0.8, "mouth-open": 0.45, "eye-left-opened-up": 0.3, "eye-right-opened-up": 0.3},
    "mouth_open": {"mouth-open": 1.0},
}
SIGNIFICANT = 0.0001
LOD_RATIO = {"skin": 0.2, "cloth": 0.22, "hair": 0.35, "eyes": 0.5}
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


def race_folder(spec):
    race = spec["phenotype"].get("race", {})
    best = max(("caucasian", "african", "asian"), key=lambda r: race.get(r, 0.0))
    return os.path.join(UNITS, best)


def expression_deltas(basemesh, spec):
    """Per-vertex offsets of the base mesh for each expression (dict name -> list of Vectors)."""
    folder = race_folder(spec)
    units = sorted({u for e in EXPRESSIONS.values() for u in e})
    for u in units:
        path = os.path.join(folder, u + ".target.gz")
        if not os.path.exists(path):
            raise SystemExit(f"expression unit {u} missing at {path}")
        TargetService.load_target(basemesh, path, weight=0.0, name="unit_" + u)
    keys = basemesh.data.shape_keys.key_blocks
    basis = [v.co.copy() for v in basemesh.data.vertices]
    unit_delta = {}
    for u in units:
        kb = keys["unit_" + u]
        unit_delta[u] = [kb.data[i].co - basis[i] for i in range(len(basis))]
    out = {}
    for name, mix in EXPRESSIONS.items():
        d = [basis[0] * 0.0 for _ in basis]
        for u, w in mix.items():
            ud = unit_delta[u]
            for i in range(len(d)):
                if ud[i].length_squared > 0.0:
                    d[i] = d[i] + ud[i] * w
        out[name] = d
    for u in units:
        basemesh.shape_key_remove(keys["unit_" + u])
    return out


def add_expression_keys(child, deltas):
    """Carries the base mesh's expression offsets onto a fitted mesh through its MHCLO/proxy vertex matches."""
    path = ClothesService.find_clothes_absolute_path(child)
    if not path:
        return []
    mhclo = Mhclo()
    mhclo.load(path)
    made = []
    for name, d in deltas.items():
        changes = []
        for vi, mapping in mhclo.verts.items():
            if vi >= len(child.data.vertices):
                break
            (v0, v1, v2), (w0, w1, w2) = mapping["verts"], mapping["weights"]
            off = d[v0] * w0 + d[v1] * w1 + d[v2] * w2
            if off.length > SIGNIFICANT:
                changes.append((vi, off))
        if not changes:
            continue
        if not child.data.shape_keys:
            child.shape_key_add(name="Basis", from_mix=False)
        key = child.shape_key_add(name=name, from_mix=False)
        key.value = 0.0
        for vi, off in changes:
            key.data[vi].co = child.data.vertices[vi].co + off
        made.append(name)
    return made


def apply_mask_keeping_keys(obj, mod):
    """A MASK modifier applied by deleting the masked vertices with bmesh, which keeps shape keys."""
    vg = obj.vertex_groups.get(mod.vertex_group)
    if vg is None:
        obj.modifiers.remove(mod)
        return
    keep = set()
    for v in obj.data.vertices:
        inside = any(g.group == vg.index and g.weight > mod.threshold for g in v.groups)
        if inside != mod.invert_vertex_group:
            keep.add(v.index)
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bm.verts.ensure_lookup_table()
    bmesh.ops.delete(bm, geom=[v for v in bm.verts if v.index not in keep], context='VERTS')
    bm.to_mesh(obj.data)
    bm.free()
    obj.data.update()
    obj.modifiers.remove(mod)


def make_lod(obj, kind):
    """A decimated copy of a part without shape keys, skinned to the same armature."""
    lod = obj.copy()
    lod.data = obj.data.copy()
    for c in obj.users_collection:
        c.objects.link(lod)
    if lod.data.shape_keys:
        lod.shape_key_clear()
    mod = lod.modifiers.new("Decimate", 'DECIMATE')
    mod.ratio = LOD_RATIO[kind]
    mod.use_collapse_triangulate = True
    with bpy.context.temp_override(object=lod, active_object=lod, selected_objects=[lod]):
        bpy.ops.object.modifier_move_to_index(modifier="Decimate", index=0)
        bpy.ops.object.modifier_apply(modifier="Decimate")
    lod.name = obj.name + "_lod1"
    lod.data.name = lod.name
    return lod


def preview_faces(spec, armature, meshes, folder):
    """Renders the face (Workbench) neutral and with each expression, for checking the shape keys."""
    os.makedirs(folder, exist_ok=True)
    head = armature.matrix_world @ armature.data.bones["head"].head_local
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.light = 'STUDIO'
    scene.render.resolution_x = scene.render.resolution_y = 384
    cam_data = bpy.data.cameras.new("face_cam")
    cam_data.lens = 85
    cam = bpy.data.objects.new("face_cam", cam_data)
    scene.collection.objects.link(cam)
    target = head + Vector((0.0, 0.0, 0.08))
    cam.location = target + Vector((0.0, -0.75, 0.02))
    cam.rotation_euler = (math.radians(88.5), 0.0, 0.0)
    scene.camera = cam
    for name in ["neutral"] + list(EXPRESSIONS.keys()):
        for o in meshes:
            if o.data.shape_keys:
                for kb in o.data.shape_keys.key_blocks:
                    if kb.name != "Basis":
                        kb.value = 1.0 if kb.name == name else 0.0
        scene.render.filepath = os.path.join(folder, f"{spec['name']}_{name}.png")
        bpy.ops.render.render(write_still=True)
    for o in meshes:
        if o.data.shape_keys:
            for kb in o.data.shape_keys.key_blocks:
                kb.value = 0.0
    bpy.data.objects.remove(cam, do_unlink=True)


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

    # Expressions first, while every fitted mesh still has all its vertices (the matches index them).
    deltas = expression_deltas(basemesh, spec)
    faces = {}
    for o in meshes:
        faces[o.name] = add_expression_keys(o, deltas)

    parts = []
    lods = []
    for o in meshes:
        kind = part_kind(o.name, spec)
        expressions = faces.get(o.name, [])
        # Bake the masks (skin hidden under clothes is deleted); keep only the armature modifier.
        for m in list(o.modifiers):
            if m.type == 'MASK':
                if o.data.shape_keys:
                    apply_mask_keeping_keys(o, m)
                else:
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
        parts.append({"mesh": part_name, "kind": kind, "albedo": albedo, "normal": normal, "triangles": len(o.data.loop_triangles),
                      "lod": 0, "expressions": expressions})
        if kind in LOD_RATIO:
            lod = make_lod(o, kind)
            lod.data.calc_loop_triangles()
            lods.append(lod)
            parts.append({"mesh": lod.name, "kind": kind, "albedo": albedo, "normal": normal, "triangles": len(lod.data.loop_triangles),
                          "lod": 1, "expressions": []})
    bpy.data.objects.remove(basemesh, do_unlink=True)
    armature.name = spec["name"]
    if os.environ.get("AFTERFALL_FACE_PREVIEW"):
        for lod in lods:
            lod.hide_render = True
        preview_faces(spec, armature, meshes, os.environ["AFTERFALL_FACE_PREVIEW"])
        for lod in lods:
            lod.hide_render = False
    meshes = meshes + lods

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
        "triangles": sum(p["triangles"] for p in parts if p["lod"] == 0),
        "lod1_triangles": sum(p["triangles"] for p in parts if p["lod"] == 1),
        "expressions": list(EXPRESSIONS.keys()),
        "assets": {"proxy": spec["proxy"], "skin": spec["skin"], "hair": spec["hair"], "eyebrows": spec["eyebrows"], "eyelashes": spec["eyelashes"], "clothes": spec["clothes"]},
        "source": "MakeHuman system assets (CC0) via MPFB " + mpfb_version(),
    }
    with open(os.path.join(outdir, spec["name"] + ".json"), "w") as f:
        json.dump(manifest, f, indent=2)
    print(f"BUILT {spec['name']}: {manifest['triangles']} triangles ({manifest['lod1_triangles']} at LOD1), {len(parts)} parts, {manifest['bones']} bones, "
          f"expressions on {sum(1 for p in parts if p['expressions'])} parts")


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
