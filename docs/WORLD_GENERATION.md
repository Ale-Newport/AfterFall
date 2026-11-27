# World generation

The world is generated from the seed by `WorldGenerator` (pure C#) into a `WorldLayout`: terrain heights, water, roads, points of interest, props, resource nodes, loot containers, survivors, spawn points, and now planned buildings and their fixtures. The layout is **not saved**; it is regenerated on load. Everything the player can change (structures, containers, doors, nodes, drops) lives in `WorldState` and is saved.

## Compatibility rule
Because the layout is regenerated, changing the generator changes the ground under existing saves. So:
- New content is **appended** after the original content: every earlier random stream, loot roll, id and spawn point stays the same. The Millbrook Apartments are placed last in `WorldGenerator.Generate`.
- `GameSettings.WorldVersion` records which generator built a world (1 = the 512 m valley). A future larger world gets a new version; version-1 saves keep the valley.
- Keyed content (see fixtures below) that a save does not have yet is created **once, on load** (`WorldSeeding.EnsureFixtures`), so new buildings arrive in old worlds furnished and looted exactly as in a new game.

## Planned buildings
A `BuildingPlan` (Domain/World/BuildingPlan.cs) describes a multi-storey building in its own frame (X right, Z toward the front, metres):
- floors, floor height (3.2 m), slab thickness and ground-floor height;
- walls per floor (exterior or interior) with door and window openings;
- stair flights (a lane, start and end along Z) and the slab holes they rise through;
- guard rails, rooms (name and floor finish);
- **fixtures**: interactive things that become world structures (doors, containers, beds, stoves, workbenches), each with a stable key, definition, position, facing, loot table, lock and the keys inside;
- decoration (sofas, tables, chairs, toilets, washers, racks).

`WorldGenerator.PlacePlan` adds an `apartment` prop for the shell (with the plan id) and turns every fixture into a `FixtureSeed` in world coordinates (`PlacedPlan.ToWorld`). The presentation builds the shell from the same plan (`PlanBuilder`): brick and plaster walls with openings, glazing, slabs with stair holes, stepped stairs over an invisible ramp, rails, a roof with a parapet (Not Walkable), floor finishes and decoration, plus colliders for all of it so the runtime NavMesh bake covers every floor. Geometry and gameplay come from one definition, so doorways and doors always line up (tested).

Rules the plan follows (checked by `DoorTests.ThePlan_…`):
- doorways at least 1.6 m wide (NavMesh agents);
- stair flights at least 1.4 m wide and under 35° (here 30.6°: 3.2 m over 5.4 m);
- every flight rises through a slab hole of exactly its lane;
- landings at both ends at least 1.6 m deep (narrower landings disconnect the NavMesh once rails and agent radius are subtracted, which the tour caught);
- rails along stairs are upright stepped segments, not one tilted box (a tilted box leans past the ends of the flight).

### Millbrook Apartments (the reference building)
Three storeys, 16 × 12 m, at Millbrook (206, 322), facing south toward the houses and the main road.
| Floor | Rooms | Interactive fixtures |
|---|---|---|
| 0 | Stair hall (mailboxes, bench), corner shop, storage room | Entrance door; shop door; storage door (locked, "Storage"); two store shelves, a drinks fridge, the shop counter (holds the key to flat 3); a workbench (crafting station), a locker, a toolbox, a crate |
| 1 | Flat 2: living room and kitchen, bedroom, bathroom | Flat door, bedroom door, bathroom door; fridge, kitchen cabinet, gas stove (cooks like a campfire, no warmth), bookshelf, wardrobe, chest of drawers (holds the storage key), double bed (respawn point), medicine cabinet, sink |
| 2 | Flat 3 (locked): study, bedroom, bathroom | Flat door (locked, "Flat 3"), bedroom and bathroom doors; two desks, two bookshelves (a small chance of the generator schematic), a locker, wardrobe, double bed, medicine cabinet, sink |

Each furniture type has its own loot: counters hold snacks, water and batteries; desks batteries, wire and the odd circuit board; bookshelves cloth, rope and rarely a schematic; sinks bandages and bottles. 35 fixtures in all.

## Doors
A door is a structure whose definition has `isDoor` (`door_interior`, `door_entrance`). `Structure` holds `DoorOpen`, `DoorLocked`, `LockId` and `SeedKey`, all saved.
- **Open/close** (E): `DoorRules.Toggle`. A locked door opens only for someone carrying its key, which unlocks it.
- **Lock/unlock** (R, with the key): `DoorRules.SetLocked`; a door must be closed to lock.
- **Broken**: at or below 30 % health a door hangs open and cannot be closed or locked; E repairs it with a hammer (half the door's cost in planks and nails).
- **The infected**: a closed door is a barrier they batter (`ZombieSystem.NearestBlocker` includes closed doors anywhere, but only on the same floor: within 2.4 m vertically); open or broken doors are walked through. The warning names the door.
- **Colonists**: a closed door carves the NavMesh, so a colonist's path ends at it; `DoorTraffic` opens unlocked doors for them and closes them again once nobody is passing. Locked doors stay shut and the colonist reports that it cannot find a way.
- **Presentation**: the leaf is a hinged part; its collider is left out of the NavMesh bake and a NavMesh obstacle on it carves the doorway only while the door is closed and intact.

## Keys
A key is the item `key` with a **tag**: `ItemStack.Tag` names the lock it opens ("Millbrook Apartments · Flat 3"). Tagged items always occupy one slot, never merge, and keep their tag through transfers, compacting, hauling reservations, drops on death and saves (`SlotDto.Tag`). Tooltips show the tag. The same mechanism will carry vehicle keys.

## Still to do
- A larger world (world version 2): terrain tiles, sector streaming, regional NavMesh, more towns, a river, a remote village and a city district. See WORLD_EXPANSION_ROADMAP.md.
- More plans (houses with upper floors, an office block, a supermarket), and plans described as data rather than code.
- Player-built doors that fit the doorway piece.
