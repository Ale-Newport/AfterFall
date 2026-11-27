# Character pipeline

Status: **planned, not started in the game.** The survivor, colonists and infected are still the procedural characters described below. This document records the current system, the licensing research behind the replacement, the pipeline chosen, and what is waiting on a decision from the project owner.

## Today: procedural humanoids
`HumanoidRig` (Presentation/Player) and `MeshFactory.Characters` build every person in code: lofted, rigid low-poly body parts (one mesh per material per bone) on a joint hierarchy (pelvis, chest, head, upper arms, forearms, thighs, shins, feet), with hair styles, outfits, headwear and palettes for variety. Animation is procedural: two-bone IK plants the feet while walking, running and crouching; arm swing, breathing, attacks, flinches, the death fall and the seated pose (vehicles) are blended in code. Nothing is skinned, there is no Mecanim avatar, and the look is stylised, not photorealistic.

## Why the replacement is constrained
The repository is **public** on GitHub under the MIT license, and AFTERFALL's rules forbid committing assets whose license does not allow redistribution. That rules out the usual sources of realistic people:

| Source | What it offers | Can it be committed here? |
|---|---|---|
| MakeHuman / MPFB2 (Blender add-on) system assets | Parametric realistic humans: base mesh, skins, eyes, eyebrows, hair, clothes, proxies; game-engine rig | **Yes.** The MakeHuman system assets are CC0, and so are characters exported from them. MPFB2's code is GPL, but it is only a tool here: none of it ships. Community assets on the MakeHuman asset site carry their own licenses (some CC-BY, some not) and must be checked one by one. |
| CMU Graphics Lab Motion Capture Database | Thousands of motion-capture clips (walking, running, crouching, fighting, falling) | **Yes**, with acknowledgement: the data is free for any use, and the lab asks to be credited. Clips need retargeting (they come as BVH/ASF-AMC). |
| Mixamo (Adobe) | Rigged characters and animations | **No.** Free to use inside a game, but the raw character and animation files may not be redistributed; a public repository is redistribution. |
| Unity Asset Store, Fab (Epic), CGTrader, TurboSquid | Realistic characters, zombies and animation packs | **No** for the public repository (their licenses forbid redistributing source assets). Usable only in a private repository or kept out of Git, which this project does not do. |
| Bandai Namco Research motion dataset | High-quality stylised motion | **No.** CC BY-NC-ND: non-commercial and no derivatives. |
| Poly Haven, Kenney, Quaternius | CC0 models | Poly Haven has no people. Kenney and Quaternius characters are CC0 but low-poly and stylised: not the photorealistic look asked for. |

Generative or "AI-made" human models were not considered: their provenance cannot be recorded in ASSET_LICENSES.md with confidence.

## Chosen pipeline (MPFB2 → Blender → Unity Humanoid)
1. **Make characters in Blender with MPFB2** using only CC0 system assets: a set of base adults (varied age, build and skin tone), each with its own clothes, hair and eyes. Use the *game engine* rig, which has the bone layout Unity's Humanoid avatar expects (hips, spine, chest, neck, head, shoulders, arms, hands, legs, feet, toes).
2. **Reduce for real time**: the MPFB2 proxy meshes (a lower-polygon body), a target of about 15–25 k triangles per character for the survivor and colonists, 8–12 k for the infected (they come in numbers), 2 LODs. Bake skin, clothes and hair to one or two texture sets (albedo, normal, mask) at 2048² (1024² for the infected).
3. **The infected** are made from the same humans: a damaged skin texture variant (pallor, veins, wounds and dried blood painted in Blender or synthesised in `TextureSynth`), torn clothing variants, and a different animation set (lurching walk, lunge, attack, stagger).
4. **Export FBX** (binary, Y-up, scale 1, deform bones only, no leaf bones) into `Assets/_Afterfall/Characters/<name>/` with Git LFS, and set the import to **Humanoid** with an avatar created from the model.
5. **Animation**: retarget CMU clips (walk, run, crouch-walk, idle variants, turning, falling, getting up, sitting in a vehicle) to the Humanoid avatar; Mecanim handles retargeting between characters. Locomotion becomes a blend tree driven by the same speed and crouch values `HumanoidRig.Animate` receives today; foot IK stays (Unity's Humanoid IK). Attack, flinch and death are layered states.
6. **Appearance system**: `CharacterAppearance` data (body, skin tone, hair, outfit, wear and dirt, infected stage) saved with each colonist and the survivor instead of today's palette, and materials varied with property blocks or vertex colours, never one material per instance.
7. **Swap in behind the same interface**: `HumanoidRig`'s public calls (Animate, SetSeated, PlaySwing, SetDead, Flash) stay, so the player, colonists, infected and vehicles do not change. The procedural rig remains as a fallback.
8. **Record** every exported character and clip in `docs/ASSET_LICENSES.md` and `docs/ASSET_PROVENANCE.md` before committing: the MPFB2 and asset versions, the assets used, the CMU clip numbers and the acknowledgement text.

## Waiting on the owner
Downloading is not done without permission. The next step needs:
- the **MPFB2** Blender add-on (about 46 MB) and the **MakeHuman CC0 system asset packs** (about 280 MB, unpacked into Blender's MPFB2 data folder, outside this repository);
- **CMU motion capture** clips, chosen individually (a few MB);
- Blender itself (installed locally; check with `blender --version`).

Once those are approved and installed, the steps above can run here, and the scripted tour gets a character scene (walk, run, crouch, sit in a vehicle, attack, die, infected variants) with screenshots for review.
