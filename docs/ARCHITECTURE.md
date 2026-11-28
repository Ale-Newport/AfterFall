# Architecture

AFTERFALL is **simulation-first**: the game is a deterministic C# simulation that runs without Unity, and the Unity layer renders it, feeds it input and physical facts. A furnace keeps smelting whether or not its GameObject exists; a save file contains the world, not the scene.

## Layers and dependency direction

```
            ┌────────────────────────────────────────────────────────┐
 Unity      │ Afterfall.Presentation  (MonoBehaviours, UI Toolkit)   │
            │   GameBootstrap → GameController → views, player,      │
            │   cameras, BuildController, TacticalController, UI     │
            └───────────────┬────────────────────────────────────────┘
                            │ commands (OpResult)        ▲ typed events, state reads
            ┌───────────────▼────────────────────────────┴───────────┐
 pure C#    │ Afterfall.Services  GameSession (application layer),   │
 (no Unity) │   PlayerSystem, NewGameFactory, SaveService            │
            ├────────────────────────────────────────────────────────┤
            │ Afterfall.Domain  items · crafting · technology ·      │
            │   construction · industry · power · survival · AI ·    │
            │   threats · weather · world (state + generator) ·      │
            │   content                                              │
            ├───────────────────────────┬────────────────────────────┤
            │ Afterfall.Persistence     │ Afterfall.Core             │
            │  DTOs, mapper, serializer,│  EntityId, Vec3, RNG,      │
            │  file store, JSON content │  clock, scheduler, events  │
            └───────────────────────────┴────────────────────────────┘
 Editor:  Afterfall.Editor → Presentation (tooling only)
 Tests:   Afterfall.Tests.EditMode → Core/Domain/Persistence/Services
          Afterfall.Tests.PlayMode → Presentation (+ Input System test fixture)
```

- Core → nothing. Domain → Core. Persistence → Domain, Core. Services → Domain, Persistence, Core. Presentation → everything above, plus Unity packages.
- Core, Domain, Persistence and Services set `noEngineReferences: true`. The same sources also compile in `tools/dotnet/AfterfallSim` (netstandard2.1, C# 9), which enforces the boundary.

## Simulation flow

`GameController.Update` (Unity frame):
1. Global input (pause, inventory, build, strategic toggle, saves).
2. `GameSession.Update(dt)`: the clock advances, then `TickScheduler.Advance(dt)` runs each system at its own fixed rate with time accumulation (at most 8 catch-up steps per frame; dropped time is recorded):

| System | Rate | Responsibility |
|---|---|---|
| PlayerSystem | 10 Hz | needs, stamina, body warmth (felt temperature from weather, shelter and fires), crafting queue, fog of war, POI discovery, item-acquisition knowledge, death |
| PowerSystem | 4 Hz | rebuilds networks only when `PowerTopologyVersion` changes; balances supply and demand with priority shedding, batteries and fuel |
| MachineSystem | 4 Hz | machine state machines: inputs, fuel, power, outputs, automation pull/push |
| ColonySystem | 4 Hz | job planning (every 2 s), assignment, physical execution, needs, fleeing |
| FarmingSystem | 1 Hz | crop growth, soil water, pump irrigation |
| ZombieSystem | 10 Hz | perception (player and colonists) → `ZombieBrain` → adapter (NavMesh in game, straight-line headless); breaching of blocking barriers |
| ThreatDirector | 0.5 Hz | heat from noise and industry, population, fair spawns, announced raids, dusk warning |
| ResourceRespawnSystem | 0.2 Hz | regrows renewable nodes out of view |
| WeatherSystem | 1 Hz | fronts (clear, overcast, rain, thunderstorm) rolled ahead from `WeatherRng`, storm and cold-snap warnings, lightning strikes on exposed conductors (lightning rods take them), rain watering planter beds in the open |
| HeaterSystem | 2 Hz | campfires burn fuel while switched on (faster in the rain when exposed) |

3. Presentation: `PlayerCharacter` reports its physical transform, `WorldSync` reconciles views when `WorldState.Version` changes, and views animate machine and light state. Zombie views write NavMesh positions back.
4. The UI reads state and turns events (`Notification`, `PlayerDamaged`, `SaveCompleted`, …) into toasts and feedback.

Power runs before machines in each tick, so machines see this tick's allocation.

**Weather and climate.** `WeatherState` (saved) holds the current and previous front, the next one (rolled in advance so it can be foretold), the scheduled cold snap and the warnings already given. `WeatherReadout.Of(world)` derives cloud, rain, storm, cold-snap intensity and the outdoor temperature from it and the clock; nothing derived is stored. `Climate.At` adds shelter (a finished roof overhead or the inside of a valley building: +10 °C, no rain or wind) and the strongest heat source (a lit campfire, or a furnace while it burns). Rain and storms shorten the infected's sight and hearing.

**Broken components.** A machine, generator, battery, lamp or pole at or below 30 % health is broken (`BuildingDefinition.IsBroken`): it leaves its grid (the topology is rebuilt when a component breaks or is repaired) and reports "Broken — repair it with a hammer". A lightning strike takes half a structure's health, so the second one breaks it; lightning never destroys a structure. A machine requests power for the next tick, which can cause a single-tick "Starting…" state.

## Entity model

`WorldState` is the aggregate root:
- `Player` (`PlayerState`): position, vitals, backpack `Inventory` (hotbar = first 8 slots), crafting queue, spawn point.
- `Structures` (`Structure`): a definition id, transform, `Blueprint` or `Complete` state, delivered materials, work, health. Optional components: `Storage`, `MachineState`, `GeneratorState`, `BatteryState`, `PowerState`. World loot containers are structures with `PlayerBuilt = false`.
- `PowerLinks`: cables between structure ids.
- `ResourceNodes`, `Drops` (ground containers, including the player's belongings after death), `Zombies`.
- `Colonists` (`ColonistState`: needs, carry inventory, skills, work priorities, current job, home bed, recent failures) and `Jobs` (`Job`: type, priority, owner, state, haul data including live reservation tokens, and for gathering the target node and work progress).
- `WorkZones` (`WorkZone`: centre, radius, allowed gather kinds): gathering areas marked in the strategic view.
- Structures may also carry a `CropState` (planter beds) or a `HeaterState` (campfires: fuel inventory, burn time left).
- `Tech` (unlocked techs and trigger counters), `Exploration` (revealed cells, discovered POIs), `Threat`, `Weather`, `Metrics`.
- Every random stream (`SessionRng`, `ZombieRng`, `ThreatRng`, `FarmRng`, `WeatherRng`), each system's tick phase, the colony planner's next pass and each colonist's next assignment time are saved, so a loaded game continues exactly like the unsaved one.
- `Layout` (`WorldLayout`): terrain heights, roads, POIs, props, spawn points. It is **regenerated from the seed** and not saved.

Every inventory has its own `EntityId` and is registered in `WorldState` with its owner, so commands address inventories by id and range checks use the owner's position.

### Economic invariants
- Every mutating inventory operation is atomic. `TryAddAll` reserves capacity for every item before committing any of them.
- Reservations: items reserved for removal (outgoing) and capacity reserved for arrival (incoming). Tokens are single-use. `ReservedTransfers` provides begin/commit/abort tickets for long-running actors (future NPC haulers).
- Refunds that do not fit go into a ground drop, never into the void. Destroyed or demolished structures spill their contents.
- A randomised 3,000-operation conservation test and save round-trip tests guard these rules.

## Static content

`Assets/_Afterfall/Resources/Content/*.json` holds items, recipes, buildings, technologies, resource nodes, loot tables and archetypes. It is loaded by `ContentJson` with strict member checking and validated by `ContentValidator` (in tests, in the editor on load, and before builds). See ADR-0002 and docs/CONTENT_PIPELINE.md.

## Persistence

`SaveSerializer` writes an envelope `{format, schema, checksum, payload}`. The payload holds `SaveHeader` plus `WorldDto` from `WorldStateMapper`. Loading verifies the checksum, applies `SaveMigrations` for older schemas, maps the DTOs back while preserving all ids, and runs `SaveValidator`. `SaveFileStore` writes to a temp file, then `File.Replace`s the old save, keeping a `.bak`. `SaveService.Load` falls back to the backup if the main file is damaged. Saves go in `Application.persistentDataPath/Saves`.

## Presentation layer notes

- **Scene:** `Main.unity` contains only `GameBootstrap` and a post-processing Volume. The world, player, cameras and UI are built in code from the deterministic layout (ADR-0003).
- **Art:** everything is generated in code except the human characters (MakeHuman CC0 models, docs/CHARACTER_PIPELINE.md).
  - **Meshes:** `MeshBuilder` and `MeshFactory` build low-poly meshes (bevelled boxes, tapered cylinders, displaced spheres, lathes). Every mesh carries vertex colours: rgb tints the material and alpha is baked ambient occlusion. Feature areas add shapes in partial files (`MeshFactory.<Area>.cs`) and cache them by a key holding every parameter.
  - **Surfaces:** the `Afterfall/Surface` shader (`Assets/_Afterfall/Shaders`) is URP physically based lighting with **triplanar** detail maps, so code-built meshes need no UVs and keep a constant texel density. It also does optional wind sway and object-space mapping for moving parts. `Art/TextureSynth` recipes (wood, planks, bark, foliage, stone, metal, brick, shingles, fabric…) produce tileable albedo, normal and cavity maps in plain C#. `ProceduralTextures` starts generating every declared surface on worker threads at launch (about 1.8 s of work on an M2 Max, mostly hidden behind the main menu) and uploads each texture once. `MaterialLibrary.Surface(surface, tint, …)` returns cached materials.
  - **Shader variants:** all runtime materials derive from material assets in `Resources/Materials`, so their shaders and variants ship in builds.
  - **Icons:** `Art/IconCanvas` is a small anti-aliased vector rasteriser (signed-distance shapes, gradients, outline, drop shadow). `Art/ItemIconArt` draws each item, and `UI/ItemIcons` caches the textures for the UI.
  - **Review:** the art code under `Presentation/Art` has no UnityEngine dependency, so `tools/dotnet/ArtPreview` can render it headlessly. The model gallery photographs every model in the built player (docs/TESTING.md).
- **Input:** `GameInput` builds `InputActionAsset` maps in code (Global, Player, Tactical). Maps are enabled per mode. Overrides persist in PlayerPrefs.
- **Cameras:** `CinemachineBrain` blends between a `CinemachineThirdPersonFollow` camera and a transform-driven strategic camera.
- **Navigation:** a `NavMeshSurface` on the world root is baked asynchronously at runtime from physics colliders. Completed blocking structures carve with `NavMeshObstacle`. Zombies use direct movement until the bake finishes. Constraints learned in the engine:
  - The baked agent is 0.35 m in radius and 1.8 m tall (`ProjectSettings/NavMeshAreas.asset`), matching the characters.
  - Doorways are at least 1.6 m wide; at 1.2 m, only 4 of 9 POI doors connected.
  - Roofs, ceilings and canopies carry `Shapes.NotWalkable`, so nobody is placed or routed on top of them.
  - `IColonistMovement.CannotReach` lets the colony system fail a job ("can't find a way there") when an agent stalls at the end of a partial path.
  - Completed structures carve one `NavMeshObstacle` per solid part that blocks a walking person. Parts below step height (0.6 m) and above head height (1.9 m) are skipped, so doorway openings stay passable and foundations stay walkable.
  - For infected, the end of a partial path more than 1 m from the goal is not "reached". They keep pursuing, stall against the barrier and start breaching it.
  - Idle colonists more than 18 m from their bed walk home, so new recruits come to the settlement.
- **Floating origin (the 64 km basin):** float precision would make positions shake tens of kilometres from Unity's origin, so everything placed in world coordinates (terrain tiles, water, props, the city, views of structures, nodes, vehicles and people) hangs under one anchor, `GameController.WorldRoot`, positioned at `-Conversions.Origin`, with world coordinates as local positions. When the survivor is more than 2 048 m from Unity's origin, `GameController.ShiftOrigin` moves the origin in 512 m steps: the anchor moves, the survivor and camera are moved back by the same amount (Cinemachine is told of the warp), NavMesh agents are re-placed, the shader global `_AfterfallOrigin` keeps world-space texture mapping still, and the NavMesh data is re-registered at the anchor. Conversions between simulation and Unity positions (`ToUnity`, `ToSim`, `ToWorld`, `FromWorld`) include the origin; code that streams by distance works in world coordinates. The NavMesh data is built relative to the anchor (`NavMeshData.position`), so its tiles stay valid across shifts. Smaller worlds keep the origin at zero.
- **City buildings (the basin):** every Kesselmouth block (`ChunkContent.Blocks`: towers, mansion blocks, terraces, semis, wharf sheds, churches) is a real building. `BuildingPlans.CityBlockPlan` makes its plan from the plan id alone (`cb|kind|x_z|size|variant`, cached for the 48 most recent): 3.6 m floors and bays; a stair core with a flight up from every floor but the top (and a lift panel in towers and blocks of five floors or more); each floor laid out for its use (lobby, offices, flats, hotel rooms, shop, café, warehouse, nave); windows in every bay of the outer walls except where an inner wall, a door or the lift meets them; furniture and fittings, with the keys to locked doors left in open rooms on the same floor. Terraces and semis are rows of houses between party walls on bay lines, each with its own front door, hall, stair and rooms furnished by where they are (front room, kitchen-diner, dining room or study; bedrooms, box room and bathroom upstairs), with pictures on inner walls only. Far away the chunk draws its blocks merged, with facade textures whose windows match the real ones (`CityMesher`). Within 45 m `CityBuildingViews` builds the real building: its shell (facades, caps and roof) and, one per frame, the floors near the survivor (inside, the floor below to the floor above; outside within 22 m, the ground and first floors), and the merged stand-in is left out of the chunk mesh. `CityInteriors` (Services) adds the plan to the layout and seeds a floor's doors and furniture into the world the first time it is built (`WorldState.SeededFloors`); floors more than 260 m away that nobody touched are dropped again.
- **Large-world NavMesh:** a 512 m window around the survivor is rebaked asynchronously when it has moved 80 m, or when new ground or buildings arrive inside it (terrain tiles and city chunks report their rectangle with `MarkNavDirty`), at most every 1.5 s.
- **Characters:** skinned MakeHuman models driven by the procedural rig, with blend-shape faces, a `LODGroup` and motion-captured walk and run cycles (docs/CHARACTER_PIPELINE.md).
- **UI:** a single `UIDocument` with layers (HUD, overlay, windows, menus, toasts), built in C# and styled by `Resources/UI/Afterfall.uss`.

## Performance

Headless measurement (`PerformanceTests`, Apple M2 Max, .NET 10): a busy settlement with 76 structures (40 machines plus generators) and 26 infected simulates at about **0.007 ms per 60 Hz frame** after caching the sorted structure list (it was 0.03 ms before); machine ticks average 0.06 ms at 4 Hz. In-engine rendering and physics costs have **not** been measured yet. Known optimisation target: the machine automation and colony planner neighbour searches are O(n²) over structures (use a spatial grid at larger scales).
