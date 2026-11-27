# World expansion audit

_Audit date: 2026-11-27, at commit `594be6e` (session 7). Baseline: `dotnet test` 174/174 passed on this commit; the last Unity runs (2026-11-24, same code apart from docs) were EditMode 174/174, PlayMode 7/7, macOS build and scripted tour with 0 failed checks and 0 errors._

This audit records what exists before the world, character, vehicle, creative-mode and map expansion, so that the expansion upgrades working systems instead of replacing them.

## Summary
| Area | State before the expansion | Consequence for the expansion |
|---|---|---|
| Player model and rig | `HumanoidRig`: a sculpted low-poly figure made of rigid mesh parts (head, torso, upper and lower limbs) generated in code by `MeshFactory.Characters`. No skinning, no Animator, no imported assets. Procedural poses: walk/run cycle, crouch, jump, swing, death, a flash on hit. Held tools are separate meshes. | No humanoid rig to retarget animations onto. Realistic characters need imported skinned meshes plus an Animator pipeline (see CHARACTER_PIPELINE.md). |
| NPCs and infected | Same rig with palettes (`ColonistView`, `ZombieView`), gaunt infected with glowing eyes. Two infected archetypes (Wanderer, Runner). | Visual upgrade must keep `ZombieView`'s NavMesh write-back and the domain `ZombieBrain`. |
| Animation controller | None (procedural only). | — |
| Character movement | `PlayerCharacter`: CharacterController, walk/run/sprint/crouch/jump, camera-relative, gravity. Reports its transform to the simulation every frame. | Free flight and vehicles must take over this transform cleanly and hand it back. |
| Asset libraries | None imported. Every mesh, texture, icon and sound is generated in code. `docs/ASSET_LICENSES.md` lists only Unity packages and fonts. | Any imported asset is new ground: provenance and redistribution rules are needed first (ASSET_PROVENANCE.md). |
| World size and terrain | One 512 m × 512 m valley (`WorldLayout.WorldSize` constant), one Unity Terrain with 513² heights and a 1024² splat. | Far too small for vehicles. Size must become a property of the layout, and the terrain must be split into tiles. |
| Streaming | None. The whole valley, every prop and every entity view are built at load. The NavMesh is baked once at runtime over the whole world. | A larger world needs sector streaming of views and a NavMesh baked around the player. |
| Procedural generation | `WorldGenerator`: deterministic, hand-placed landmarks (seven POIs, four roads), seeded terrain detail, vegetation and loot. The layout is regenerated from the seed and never saved. | Changing the generator changes old saves' ground. Saves must record which generator built their world. |
| Buildings and interiors | `PropBuilder` builds single-storey houses, a clinic, shop, garage, warehouse, barn and shacks with walkable interiors (1.6 m doorways) and loot furniture placed inside as world containers. No upper floors, no stairs. | A multi-storey reference building is new work. |
| Doors and furniture | Doorways are openings; there are no door leaves, locks or door state. Loot furniture (cabinet, fridge, wardrobe, toolbox, medicine cabinet, crate, shelf, locker) are structures with storage and loot tables. A bed sets the respawn point; workbench, campfire and machines are stations. | Doors must be added as persistent state. Furniture can reuse the structure, storage, bed and station mechanics. |
| Vehicles | None. `wreck_car` is a salvageable resource node (scrap), drawn as a sedan shell. | The vehicle system is new: domain state, physics, keys, persistence. |
| Items and inventory | 37 items in `items.json`, `ItemCatalog`, atomic `Inventory` with reservations and durability. Stacks carry item id, quantity and durability only. | The item browser can enumerate `ItemCatalog.All`. Per-vehicle keys need either per-stack data or keyed item definitions. |
| Construction | `ConstructionService` with placement rules, blueprints, delivery, build work, repair and demolish. Costs come from content. | Creative construction can be a rule switch inside the service (free and instant), so placement validation is unchanged. |
| Persistence | Versioned envelope (schema 1, checksum, migrations, backup). `WorldStateMapper` maps everything explicitly. `GameSettings` is stored as-is. | New state (mode, map markers, doors, vehicles) goes into new DTO fields with defaults, so schema-1 saves keep loading. |
| Strategic camera | `TacticalCamera` + `TacticalController`: orders, cables, gathering areas, fog of war from `ExplorationState` (16 m cells). | The world map must reuse `ExplorationState` for discovery, not a second fog. |
| UI and map | UI Toolkit, built in code (`UiManager`, `HudView`, windows). No minimap, no world map, no waypoints. HUD vitals are bottom-left, the hotbar bottom-centre. | The minimap goes bottom-right. |
| Debug tooling | F3 overlay (FPS, per-system tick costs, counts). `AutoTour` drives the game through commands and takes screenshots; `ModelGallery` photographs models. No in-game spawner, flight or teleport. | Creative/debug mode is new; the tour can drive it. |
| Input | `GameInput` builds rebindable action maps in code. Taken keys: Esc, I, Tab, B, F1, F3, F5, F9, WASD/arrows, Space, Shift, C/Ctrl, E, Q, R, X, 1–8, mouse. | F6, F7 and M are free. Ctrl is crouch (and descend in flight is mode-separated). |

## Engine and tools on the development machine
- Unity 6000.3.28f1 (licensed), URP 17.3, Cinemachine 3.1.7, Input System 1.19, AI Navigation 2.0.14. No Addressables, no Animation Rigging package.
- Blender is installed (`/Applications/Blender.app`); MakeHuman is not.
- `.claude/settings.json` (untracked, local) already disables commit and PR attribution with the supported `attribution` object, and `tools/git-hooks/commit-msg` rejects AI trailers.

## Risks found
- **Save compatibility.** The layout is regenerated from the seed. Any change to `WorldGenerator` moves the ground under existing saves. Mitigation: saves record a world-generation version; version 1 keeps the original valley generator.
- **Runtime NavMesh bake** covers the whole world at once. Fine at 512 m; not at several kilometres.
- **Whole-world view creation.** `WorldSync` creates a view for every node, structure and drop. Fine for about 2,000 nodes; not for a forest many times larger.
- **No skinned characters.** Realistic humans cannot be produced in code; they need imported, licensed assets, and the environment has no logins for Mixamo, the Unity Asset Store or Fab.

## Decision
Keep every working system. Add, in order: creative/debug mode with free flight, the map system, doors and a multi-storey building, vehicles, then the larger world with streaming, then imported characters (blocked on asset access; see CHARACTER_PIPELINE.md). See WORLD_EXPANSION_ROADMAP.md.
