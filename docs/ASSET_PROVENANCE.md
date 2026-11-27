# Asset provenance

Where every non-code asset in AFTERFALL comes from, how it was made, and what may be added. `docs/ASSET_LICENSES.md` is the license register; this file is the process.

## Current state
The human characters are made from MakeHuman's CC0 system assets (see the log below). Apart from them the game ships **no third-party art, audio, fonts or models**. Every mesh, texture, icon, material colour, sound and animation is produced by project code at runtime (`MeshFactory*`, `PropBuilder*`, `PlanBuilder`, `NodeVisuals`, `StructureVisuals*`, `HumanoidRig`, `TextureSynth*`, `ItemIconArt*`, the procedural audio in `AudioDirector`). The world, its towns, buildings, vehicles and the map are generated from the seed. The few files under `Assets/` that are not code are Unity template settings and base materials derived from URP, listed in ASSET_LICENSES.md.

The world-expansion work (creative mode, maps, apartments with doors and keys, vehicles, the 2 km region with its river, bridges, rail and towns) added **no** external assets: the vehicles, furniture, doors, bridge and track are all built in code from `MeshBuilder` parts with procedural materials.

## Rules for adding an asset
1. **The repository is public (MIT).** Only assets whose license allows redistribution in a public repository and in a commercial game may be committed: CC0, CC-BY (with credit) or the project's own work. Asset Store, Fab, Mixamo, CGTrader and similar "use but don't redistribute" licenses are not allowed. Non-commercial (NC) and no-derivatives (ND) licenses are not allowed.
2. **Record before committing**: add a row to ASSET_LICENSES.md with the source URL, author, license, version or date downloaded, and any changes made, and a short entry below.
3. **Keep the source**: for a model made in a tool (Blender, MPFB2), record the tool and add-on versions and the assets used, so it can be rebuilt.
4. **No look-alikes**: nothing that copies another game's characters, maps, UI or music.
5. **Binary files go through Git LFS** (`.gitattributes`); generated files are not committed.
6. **No downloads without the owner's approval** (file, source and size stated first).

## Planned sources (not yet used)
| Use | Source | License | Status |
|---|---|---|---|
| Character animation (motion capture) | CMU Graphics Lab Motion Capture Database (mocap.cs.cmu.edu) | Free for any use; acknowledgement requested | Not requested yet: the characters are animated by the procedural rig. Needs the owner's approval before any download; clips to be listed individually |

## Log
| Date | Asset | Source | License | Notes |
|---|---|---|---|---|
| 2026-11-27 | Seven human characters (`Resources/Characters`) | MakeHuman system assets (94 in the index, all `"license": "CC0"`, author `makehuman_system`), already installed with MPFB 2.0.17 in Blender 4.5.13; nothing had to be downloaded | CC0 1.0 | Built by `tools/characters/build_characters.py`, which refuses any asset missing from the CC0 system index and the six outfits that print the MakeHuman logo (found by scanning every outfit texture for the logo's orange). Each asset file also carries MakeHuman's "released as CC0 in September 2020" header. Textures downscaled to 1024 px |
