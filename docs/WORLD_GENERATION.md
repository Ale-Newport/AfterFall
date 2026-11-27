# World generation

The world is generated from the seed by `WorldGenerator` (pure C#) into a `WorldLayout`: terrain heights, water, roads, points of interest, props, resource nodes, loot containers, survivors, spawn points, and now planned buildings and their fixtures. The layout is **not saved**; it is regenerated on load. Everything the player can change (structures, containers, doors, nodes, drops) lives in `WorldState` and is saved.

## Compatibility rule
Because the layout is regenerated, changing the generator changes the ground under existing saves. So:
- New content is **appended** after the original content: every earlier random stream, loot roll, id and spawn point stays the same. The Millbrook Apartments are placed last in `WorldGenerator.Generate`.
- `GameSettings.WorldVersion` records which generator built a world (1 = the 512 m valley). A future larger world gets a new version; version-1 saves keep the valley.
- Keyed content (see fixtures below) that a save does not have yet is created **once, on load** (`WorldSeeding.EnsureFixtures`), so new buildings arrive in old worlds furnished and looted exactly as in a new game.

## World versions
| Version | Name | Size | Choose it |
|---|---|---|---|
| 1 | Millbrook Valley | 512 × 512 m, one terrain tile | New Game → World → "Millbrook Valley (512 m)"; every save made before world version 2 |
| 2 | Greater Millbrook | 2048 × 2048 m, 4 × 4 terrain tiles of 512 m | New Game → World → "Greater Millbrook (2 km)" (the default for new games) |

`WorldGenerator.Generate(seed, version)` dispatches; `NewGameFactory` and `WorldStateMapper.FromDto` both pass `GameSettings.WorldVersion`, and `WorldState` takes the layout's size (the fog-of-war grid is 128 × 128 cells of 16 m in version 2).

## Greater Millbrook (version 2, `WorldGenerator.Region.cs`)
The valley sits in the south-west corner with every landmark, building, container and vehicle where version 1 has them (tested); the region opens east and north of it. Generation takes about 0.7–0.9 s in a release build.

| Place | Where | What |
|---|---|---|
| Millbrook Valley | (0–520, 0–520) | The original start: camp, Millbrook and its apartments, gas station, workshop, substation, quarry, Dawson Farm |
| Kessel river | north edge → reservoir → south edge, around x 1000–1290 | 16 m wide, meandering (the course is subdivided every 30 m and pushed sideways by noise), its level falling downstream |
| Kessel Reservoir | (1230, 1420), r 105 m | A lake in the hills that the river runs through |
| Ashford | (1000, 500) | A riverside town: a 6 × 5 street grid either side of the river, about 100 houses, shops, clinics and garages, the Ashford Apartments, and the highway bridge |
| Eastgate District | (1660, 570) | Industrial: a 5 × 4 grid of warehouses, garages, workshops and sheds, the Eastgate Apartments, and the disused freight line along its north edge |
| Pinecrest | (520, 1620) | A mountain village round a loop road: cabins, houses, a barn and a store, at the end of the mountain road |
| Miller Farm, Coldwater Farm | (1450, 200), (1780, 270) | Farmsteads on the southern plains: barn, house, silo, fields |
| Thornwood Lodge | (705, 895) | A hunting lodge in the western forest |
| Northgate Research Station | (1820, 1800) | A fenced compound in the north-east mountains with lockers of electrical parts and schematics |

**Terrain.** One 2049 × 2049 height field (1 m cells, heights up to 220 m). The valley's own formula is blended into rolling plains east, hills to the north and ridged mountains (55–150 m) beyond z ≈ 1100; the map edges rise. Points of interest are levelled to their centre's height.

**Water.** River levels are set first, from the levelled ground: walking downstream, each point's level is the lower of the previous level and the ground there less 1.8 m, so water never runs uphill (where the ground drops, the river runs in rapids). The channel is cut 2.6 m below the level; banks rise gently and then ever more steeply so they always meet the ground. The reservoir is a bowl at the level of the river where it passes through. `WorldLayout.WaterAt` answers "is there water here, and at what level" for any point (indexed by 64 m cells); `WaterNear` gives the nearest water level for shore painting.

**Roads.** The valley's roads, the highway east (valley → Ashford → Eastgate → the east edge), the mountain road to Pinecrest and its spur to the lodge, the river road up the west bank, the research road (which crosses the Kessel above the reservoir), two farm tracks, the town streets and the rail line. Every road is cut into lengths of at most 24 m. Points are **pinned** where a road meets one graded before it (it takes that road's height), where it crosses a river (4 m above the water) and on a town's levelled pad; the rest follow the ground, limited to a 10 % grade (up to 16 % on the mountain roads where pinned points demand it). All roads are then pressed into the terrain at once: where roads meet, their heights are averaged weighted by how near each road's middle a cell is, so junctions are smooth. Roads keep their level near rivers beyond the bridge span. Tested: no 4 m stretch of road rises more than 0.35 m per metre, and no road runs through water except on a bridge.

**Bridges.** Wherever a road crosses a river a `bridge` prop is placed: long enough to clear the cut banks at the crossing angle, its deck laid between the road surfaces just beyond each end (`PropPlacement.Pitch`). The deck and parapets are solid, so people, vehicles and the NavMesh use it.

**Towns.** `PlanTown` lays a street grid; streets stop 24 m short of water. Parcels line both sides of every street, facing it, sized by kind (houses, shops, clinics and garages in Ashford; warehouses, garages, shops and sheds in Eastgate), with about one lot in seven left empty. A parcel is dropped if it is near water, on another road, too close to another parcel, or on a reserved lot (the apartment blocks and parked vehicles). Each building gets the containers its kind has (kitchen cabinets, fridges, wardrobes, store shelves, counters, medicine cabinets, toolboxes, lockers, crates) and a chance of infected nearby.

**Nature.** Trees, bushes, plants, rocks and iron ore on a 10 m jittered grid, by a woodland density (`WorldGenerator.RegionForest`, shared with ground painting) that is thick in the west and the northern hills and thin on the eastern plains and the peaks, kept off roads, water, steep slopes, places and building footprints. About 13 000 nodes.

**Infected.** Every town building has a chance of spawn points around it, the remote places have their own, and 40 more are scattered over the region (none in water or on steep ground).

**Vehicles.** Besides the valley's three: a locked grey sedan in Eastgate whose key is in the Eastgate Apartments, a pickup on an Ashford street with its key in the ignition, and a quad bike in Pinecrest with little fuel.

### How the game handles the larger world
- **Terrain** streams as 512 m tiles around the survivor (load within 700 m, unload beyond 1100 m), painted on worker threads.
- **Resource nodes** only get GameObjects within 300 m of the survivor (they are distance-culled at 260 m anyway), refreshed every time the survivor has moved 24 m.
- **NavMesh**: in a world larger than 512 m it is baked in a 512 m window around the survivor that follows them (re-centred after 80 m, and rebaked when a terrain tile finishes loading; only changed tiles rebuild, in the background). Colonists whose ground leaves the window fall back to straight-line movement and are put back on the NavMesh when it returns.
- **Water**: a ribbon mesh along each river at its level (in chunks, so it culls) and a disc for each lake, all with the lake shader.
- **Ground painting** uses the region's own forest density, raises the rock line to the mountains (125 m) and dusts the peaks with scree; shores and reeds follow the nearest river or lake.
- **The map** draws rivers and lakes at their own levels, the railway as a thin dark line, and the whole 2 km region at 2048 × 2048 px.

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

### More apartment blocks
`BuildingPlans.Apartments(id, name, carKey)` builds the same block under another name: its lock names and keys follow the name, and a vehicle key may be left in flat 2's kitchen. Version 2 places three: Millbrook, Ashford (935, 540) and Eastgate (1724, 650, facing south; the Eastgate sedan's key is in it). Fixture keys start with the plan id, so the blocks never share keys or loot rolls.

## Still to do
- Beyond 2 km: a city district with taller buildings, more villages, a 4–8 km world with sector streaming of props and buildings (today all props are built at load). See WORLD_EXPANSION_ROADMAP.md.
- Far-distance trees (impostors beyond 260 m), so forests stay visible from the air.
- More plans (houses with upper floors, an office block, a supermarket), and plans described as data rather than code.
- Player-built doors that fit the doorway piece.
