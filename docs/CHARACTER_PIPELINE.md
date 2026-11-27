# Character pipeline

The survivor, the colonists and the infected are **skinned human models made from MakeHuman's CC0 assets**, animated by the game's procedural rig. The sculpted procedural bodies remain as a fallback when the models are missing.

## Why MakeHuman
The repository is public on GitHub under the MIT license, so every committed asset must allow redistribution. That rules out the usual sources of realistic people:

| Source | Can it be committed here? |
|---|---|
| MakeHuman system assets (base mesh, proxies, skins, eyes, brows, lashes, hair, clothes, rigs) through the MPFB Blender extension | **Yes.** MPFB's LICENSE.md puts all of its data and assets (base mesh, proxies, targets, rigs and weights, textures, clothes) under CC0 1.0, and the MakeHuman team claims nothing over exported models. MPFB's code is GPL-3.0, but it is only a tool here: none of it ships. |
| CMU motion capture | Yes, with credit (not used yet; see below) |
| Mixamo, Unity Asset Store, Fab, CGTrader | No: their licenses forbid redistributing the files, and a public repository is redistribution |
| Bandai Namco motion dataset | No: non-commercial, no derivatives |
| Poly Haven, Kenney, Quaternius | Poly Haven has no people; the others are stylised low-poly |

## The models
| Model | Used for | Body | Outfit | Triangles |
|---|---|---|---|---|
| `survivor_m` | The survivor (and male survivors whose variant picks it) | male_generic proxy, middle-aged | Work overalls over a T-shirt | 31 k |
| `survivor_f` | Female survivors | female_generic proxy | Vest, striped shirt and jeans | 31 k |
| `colonist_m` | Survivors you can recruit | male_generic proxy, older | Striped shirt and jeans | 26 k |
| `colonist_f` | Survivors you can recruit | female_generic proxy | Striped dress | 29 k |
| `infected_m1`, `infected_m2`, `infected_f1` | The infected | low-poly proxies (male1591, female1605) | A suit; jeans and a shirt; a vest and jeans | 16–26 k |

Each has the 53-bone MPFB **game-engine** rig (pelvis, spine_01–03, neck, head, clavicles, arms, hands with fingers, thighs, calves, feet, toes), eyes, eyebrows, eyelashes, hair and shoes. Skin under the clothes is deleted. Textures are shared between models and capped at 1024 px (52 MB in all, in Git LFS).

**The infected** use the same pipeline: older skins tinted grey-green and clammy, grimy clothes, and the glowing eyes the procedural infected had.

## Building them (tools/characters)
`characters.json` lists each character: MakeHuman phenotype (gender, age, muscle, weight, proportions, height, ethnic mix), body proxy, skin, hair, brows, lashes and clothes. `build_characters.py` runs in Blender 4.5 with the MPFB extension (installed from extensions.blender.org, with the MakeHuman system asset pack):

```bash
/Applications/Blender.app/Contents/MacOS/Blender -b --python tools/characters/build_characters.py -- tools/characters/characters.json Assets/_Afterfall/Resources/Characters
```

For each character it:
- refuses any asset that is not in MPFB's CC0 system asset index, and the six outfits that print the MakeHuman logo (male_casualsuit02/04/06, female_casualsuit01/02, female_sportsuit01);
- builds the human with MPFB (game-engine rig, no subdivision), deletes the hidden skin and removes the base mesh (the proxy is the body);
- writes `<name>.fbx` (armature and meshes, no animation) and `<name>.json`: each mesh part with its kind (skin, cloth, hair, lashes, eyes), albedo and normal map, and triangle count;
- writes each texture once to `Textures/`, downscaled with macOS's `sips`.

Unity imports them with `Editor/CharacterImport.cs`: generic rig, no animation, no materials, normal maps flagged, 1024 px maximum.

## In the game
- `CharacterModels` picks a model for a palette (the survivor's look, a colonist's or an infected's variant), loads its manifest and builds shared materials from three URP Lit material assets in `Resources/Materials`: `AfterfallCharacter` (opaque), `AfterfallCharacterNormal` (with a normal map) and `AfterfallCharacterCutout` (alpha-cut, double-sided, for hair and lashes). Shipping these as assets keeps their shader variants in builds.
- `HumanoidRig` builds its procedural joints as before, but invisible, and `SkinnedBody` binds the model to them at rest:
  - it turns the model upright and towards +Z, scales it so its legs are as long as the procedural legs (so the procedural foot IK still plants its feet), and sets it on the ground;
  - arm and leg bones are re-aimed along the procedural limbs (the model stands in an A-pose);
  - each frame, every bone takes its driver joint's change of rotation since rest: spine_02 halfway between pelvis and chest, the neck halfway between chest and head, hands with the forearms, the pelvis following the procedural pelvis.
- So **every existing animation works unchanged**: walking, running, crouching, jumping, the infected's shamble and claw attack, tool swings, flinches, the death fall and sitting in a vehicle.
- Held tools hang from a grip in the model's right palm. The survivor's backpack and satchel (sculpted, as before) hang on the model's chest.
- `CharacterModels.Enabled = false` (or missing assets) falls back to the procedural bodies everywhere.

Checked in the model gallery (`tools/gallery.sh characters`): the line-up, every character front and back, walking, action poses (death, crouch, jump, claw, tool swing) and held items.

## Not done yet
- **Motion capture.** Animation is still procedural. CMU motion capture would need its own download approval, retargeting to the game-engine rig (it maps to a Unity Humanoid avatar) and an Animator; the procedural rig would remain for vehicles and deaths.
- **Faces**: no expressions or eye blinks (MPFB has expression targets).
- **More variety**: a few more bodies and outfits per role; per-character colour variation of clothes; torn clothing and wounds for the infected.
- **LODs**: one level today; the infected could use the proxy741 body far away.
