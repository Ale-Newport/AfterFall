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
| 2 | Greater Millbrook | 2048 × 2048 m, 4 × 4 terrain tiles of 512 m | New Game → World → "Greater Millbrook (2 km)" |
| 3 | The Kessel Basin | 65 536 × 65 536 m, 128 × 128 chunks of 512 m, generated as the survivor travels | New Game → World → "Kessel Basin (64 km)" (the default for new games) |

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

## The Kessel Basin (version 3: `BasinWorld*.cs`, `TiledHeightField.cs`, `Services/ChunkStreaming.cs`)
A 64 km world can't be built up front (4 billion height samples, millions of buildings). Only the **plan** is made at load (about 1–2 s, with Greater Millbrook): places, rivers, lakes, the road and rail network with its grades and bridges, and the city's rules. Everything else is made **per 512 m chunk, on demand, from the seed and the position alone**, so the same seed is the same world whatever order chunks are made in (tested).

### What is where
| Area | Where (km) | Character |
|---|---|---|
| Greater Millbrook | 0–2, 0–2 (south-west corner) | Version 2 exactly (its heights, content and keys), blended into the basin over 1.2 km, with highlands rising north of it |
| The Greyspine | west, x < ~16, z > ~8 | Ridged mountains to ~800 m, rock above ~400 m, snow above ~550 m, pine forest to the tree line (~500 m); the Aldwater's source; Cragholm village |
| The Thornwood | around (19, 44) | Dense old forest between the mountains and the moor |
| The High Moor | north, z > ~47 | Rolling heather and peat at 120–250 m, rocks, tarns; High Tor hamlet; Kingsmoor town |
| The Lake District | north-east, around (51, 55) | Wooded hills and five lakes; Dunmere village |
| The Scar | south-west, around (11, 6.5) | Red-sand badlands of terraced mesas; Redrock outpost |
| Farmland | south and west-centre | A patchwork of ploughed, ripe and grazed fields (260 × 210 m) with hedgerows; Harrowgate, Oxley, Ashby Cross, Stonebridge |
| Estuary marshes | around (53.5, 20) | Mud and reeds barely above the sea; Fenwick hamlet |
| The coast | east, x ≈ 58.5 ± 2.6 km | Beaches and dunes in the south (Saltcombe), cliffs in the north (Gullhaven); the sea beyond (level 12 m) |
| **Kesselmouth** | centred (40, 30), radius 21 km | The metropolis: ~1 400 km² of city mask, about the area of Greater London (~1 570 km²) |

Rivers: the **Aldwater** (Greyspine → Kesselmouth → the sea, 28 m wide upstream, 90 m in the city, 160 m in the docks, a 300 m estuary), the **Brack** (moor → the Aldwater in the city) and the **Fennel** (lake district → the sea), meandering, with levels that only fall and never drop below the sea. Twelve towns and villages, ring roads and radial avenues in the city, a motorway from Millbrook to the docks, A-roads to every village, a mountain road, and three railway lines out of the city centre. Every road and rail line is graded (pinned at crossings and on village pads, at most 9 %, rail 4 %) and gets a bridge wherever it crosses water.

### Kesselmouth
The street plan is the contour lines of two smoothly **warped fields** U and V on a constant 104 m grid (a varying grid spacing would be multiplied by the distance from the origin and squeeze the blocks): streets bend like an old city's, and every chunk can build the blocks whose centres it holds while matching its neighbours exactly. Dense blocks (density > 0.6) are halved by a lane. Each block, by density and district:
- **Towers** in the Old Quarter (40–220 m, glass curtain walls, banding or fins, crowns) with plazas;
- **Mansion blocks** (4–8 storeys, brick or stone, shops on the high streets);
- **Terraces** of brick or rendered houses (2–4 storeys, sash windows, front doors, slate roofs, chimneys, shopfronts on arterials);
- **Semi-detached houses** with hipped tile roofs and front gardens in the suburbs;
- **Docks**: corrugated sheds with roller doors along the estuary;
- **Parks** (about one block in seven, and the great Hollin Heath) with trees that can be felled;
- churches with spires; avenues every sixth street; abandoned cars; parked vehicles to find; infected.

A few buildings per block can be **entered and searched** (houses, shops, clinics, warehouses, garages with their containers); the rest are solid façades. A city chunk holds 200–360 buildings, 30–80 containers and 50–90 street segments, generated in 80–250 ms on a worker.

### Generation on demand
- **Heights** (`TiledHeightField`): 513² tiles at 1 m, made on demand (10–50 ms) and kept in a 160-tile cache: the coarse ground on a 4 m grid, fine detail by biome (rough in the mountains and badlands, none in the city), then village pads, roads, river channels and lake bowls pressed in. Coarse heights (`CoarseHeight`, `MapHeight` on a cached 64 m grid) serve maps and distant things without making tiles. City chunks use the coarse ground directly (city ground has no fine detail), so the distant skyline never makes height tiles.
- **Chunks** (`WorldLayout.RequestChunk`/`EnsureChunk`): generated on worker threads (at most 6 at once, nearest first) and merged on the main thread; their content is appended to the layout's lists, so the rest of the game sees one layout.
- **Seeding** (`ChunkStreaming`, twice a second): chunks within 1.15 km are requested; within 640 m they are **seeded once** (containers with loot, fixtures, vehicles, campfires, infected; remembered in `WorldState.SeededChunks`, saved) and their trees, rocks and plants become resource nodes. Nodes nobody has touched are dropped again beyond 1 km of the survivor and every recruited colonist and are **not saved**: they are remade from the layout when someone returns (a felled tree is kept and saved with its key). Crossing the whole basin leaves a ~2.6 MB save.
- **Indexes**: structures and nodes are kept in 32 m spatial grids, so the infected's checks for doors and walls look only nearby.

### How the game draws it (`ChunkViews`, `CityMesher`)
- Terrain tiles stream as before (within 700 m), painted with **ten layers** (snow and sand added) by biome: snow above the snow line, sand on beaches and in the Scar, heather and peat on the moor, mud in the marsh, field patterns on farmland, worn urban ground and pavements in the city.
- Props (bridges, rail, enterable buildings, village houses) within 900 m.
- The city: **merged meshes per chunk** (one per material, prepared on a worker, one chunk finished per frame) with a box collider per building under a Not Walkable modifier, within ~760 m; a **skyline** of boxes out to 2.6 km.
- A **far-terrain** mesh (12 km, 96 m grid, coloured by biome) under the detailed tiles so the land reaches the horizon; the sea in strips along the coast.
- In the basin the camera sees 3.2 km and the fog is thinner.
- Structures and parked vehicles have views only within 350 m / 700 m (player-built structures and the driven vehicle always).
- The map: the overview from cached grids (biome colours, the city's grey), and a **1 km local map** at 1 m per pixel around the survivor (streets and buildings) on the minimap and, zoomed in, on the world map.

### Measured (macOS build, Apple M2 Max, 1600×900)
New game playable after ~9 s. In the basin tour (teleports to the city centre, terraced streets, docklands, the river, the snow, the moor, the lakes, the Scar, the coast and Harrowgate): frame time 8.3–8.4 ms average (the vsync cap) at every stop, worst single frame ≤ 17 ms; 15 city chunks detailed and ~37 in the skyline at the centre.

### Known limits
- Positions are plain floats: 64 km from the origin they are good to ~4 mm. No jitter was seen in stills; motion at the far corners has not been checked. A floating origin would remove the risk.
- Only a few city buildings can be entered; towers have no interiors; streets have no pavement kerbs or street furniture yet.
- Distant forests beyond 300 m show only as the far terrain's colour.
- The world map's detail covers 1 km around the survivor; elsewhere it shows the overview.

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
