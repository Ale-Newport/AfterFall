# World expansion progress

Phases and acceptance criteria are in WORLD_EXPANSION_ROADMAP.md; the starting point is in WORLD_EXPANSION_AUDIT.md. Every phase below was checked with the headless suite, Unity's EditMode and PlayMode suites, a macOS build and the scripted tour (`-afterfall-tour-expansion`), whose screenshots were looked at.

| Phase | State | Commit | Verified by |
|---|---|---|---|
| 0 Audit and roadmap | Done | `15559e1` (with phase 1) | Baseline 174/174 headless tests |
| 1 Creative mode, developer tools, F6 panel, F7 free flight, teleports | Done | `15559e1` | `CreativeTests` (10); tour `CreativeScene`: item browser over all items, spawning, flight to another place without walking, teleport and back, creative building, survival refusals |
| 2 World map, minimap, waypoints, markers | Done | `2d21872` | `MapTests` (6); tour `MapScene`: map drawn from the layout, fog, discovered places only, waypoint and marker, walking toward the waypoint |
| 3 Doors, keys, Millbrook Apartments | Done | `1b401e3` | `DoorTests` (7); tour `ApartmentScene`: NavMesh through doors and up both flights, locked flat and its key, an infected battering a door until it breaks |
| 4 Vehicles | Done | `5e17fef` | `VehicleTests` (7); tour `VehicleScene`: key found, drive 100+ m, park, save and reload, hotwire, run down an infected, quad bike, creative spawn |
| 5a Terrain tiles and the water model | Done | `a50bb29` | Valley unchanged in the tour; tiles stream |
| 5b Greater Millbrook (world version 2, 2 km) | Done (see below) | this change | `RegionTests` (7); tour `RegionScene` |
| 6 Realistic characters | Done: seven skinned MakeHuman (CC0) models driven by the procedural rig; motion capture not yet | this change | Model gallery (`tools/gallery.sh characters`); full tour with skinned survivor, colonists and infected |
| 7 The 64 km Kessel Basin with Kesselmouth (London-sized city), biomes and chunk streaming | Done (first version) | `4ea2937` | `BasinTests` (4); tour `BasinScene` (`-afterfall-tour-basin`): 10 stops, 0 failures, 8.3–8.4 ms average at each |
| 8 Basin follow-ups: floating origin, city interiors (flats, supermarkets, stations), street furniture, distant trees; character variety, LODs, faces, motion capture | Done | `bb0dff6` | `BasinTests` (5); tour `BasinScene`: the origin shifts with the survivor and the NavMesh, terrain and node views follow; inside a station, a block of flats and a supermarket; distant trees; model gallery (faces, gait strips) |
| 9 Real city buildings (every block enterable, every floor reachable and furnished; rows of houses), routes by road to the waypoint | Done | this change | `BasinTests` (every city building: stairs in every house, unique keys, nothing in doorways or on stairs, windows and furniture on every floor), `RouteTests` (6); basin tour `CityTowers` and `RoadRoute` |

## Phase 9 details
- **Plans from ids**: a city block's building is made from its plan id alone (kind, position, size, variant), on a worker when the survivor comes within 45 m, and cached. Floors are 3.6 m and the bay grid 3.6 m, so the merged facades seen from afar (one textured quad per wall, a window per bay and floor) show the same windows as the real walls.
- **Layout**: a core along the west side of each section holds a single-lane stair (stacked flights, all rising north) and a walkway of at least 1.7 m; floors are laid out east of it as a spine corridor with units either side, or rooms in a row off the walkway. Towers are offices with the odd residential floor, flats, or a hotel above a café; mansion blocks have shops or cafés below flats; sheds and churches are one tall room. A third of flat and hotel doors are locked, their keys in open rooms on the same floor.
- **Houses**: rows are divided on bay lines every two bays (an end house of three bays or more gives one to its neighbour), semis once near the middle; a house has its own door, hall, stair and one-bay rooms whose doors sit near their front end so the rest of the wall takes furniture.
- **Checks**: across five city chunks (502 buildings, 6 361 floors, about 188 000 fittings and 356 000 pieces of furniture and decoration), every fixture key is unique, every building fits its block, every floor but the top has a flight up in every house, nothing stands in a doorway (0.85 m) or on a stair, every floor has rooms and windows, every locked door's key is in the building, every row of houses has a hall per front door, a bedroom and a kitchen, and no house room is wider than 9 m.

## Phase 8 details
- **Floating origin**: see ARCHITECTURE.md. Across the basin tour (ten teleports up to 50 km) the survivor stays within 260 m of Unity's origin after 10 shifts, on the NavMesh at every stop.
- **Interiors**: `BasinWorld.Interiors.cs` lays them after a chunk's other content, from its own random stream. 23 railway stations along the three lines (a ticket hall, kiosk and locked ticket office, a 72 m platform with a canopy); blocks of flats three to six storeys high in place of some mansion-block facades (at most one per chunk); supermarkets in place of some dock sheds and tower blocks (aisles, tills, fridges, a storeroom and a locked office). Plans are rebuilt from their ids (`BuildingPlans.CityPlanId`); every locked door's key is somewhere reachable in the same building.
- **Street furniture**: lamp posts along both pavements of every street, bins and benches, bus shelters and telephone boxes on the main roads, merged into each detailed city chunk (render-only).
- **Distant trees**: `FarTrees` draws each generated chunk's trees as low-poly stand-ins (merged per 128 m cell) from 230 m to 1.25 km, where real tree views stop; felled trees are left out when a chunk is rebuilt.
- **Performance**: the NavMesh window used to rebake on every new terrain tile anywhere; it now rebakes only for new ground inside it, at most every 1.5 s (the city stops went from 12.8–13.4 ms average with 133–183 ms spikes to 8.5 ms).

## Phase 5b details
- Generation: 2 × 2 km, deterministic, about 0.7–0.9 s in a release build (debug test builds are slower).
- Content: the valley unchanged in its corner; Ashford (about 100 buildings, an apartment block, a bridge), Eastgate (about 120 buildings, an apartment block, the freight line), Pinecrest (10 buildings), two farms, a lodge, a research station, the Kessel river and reservoir, about 13 000 trees, rocks and plants, about 640 containers, about 170 infected spawn points, six vehicles.
- Measured in the macOS build (Apple M2 Max, 1600×900, vsync at 120 Hz), tour `RegionScene`: playable 7.6 s after New Game (generation, 3 of 16 terrain tiles, about 240 buildings and 640 containers built, 784 of 12 772 node views), NavMesh window baked 0.2 s later. Frame time averages 8.3–8.4 ms (the vsync cap) at the start camp, in Ashford by the bridge, in Eastgate, Pinecrest and the research station; p95 at most 9.3 ms. Agents path across the Ashford bridge bank to bank; the Ashford pickup drives 168 m along the highway and over the bridge on its deck. 0 errors logged.
- Tests (`RegionTests`): determinism and size; the valley's places and buildings unchanged; buildings on dry land, off roads and apart; no trees in buildings; the river flows downhill and holds water; bridges span the channel; no road through water except on a bridge; road grades; every inhabited place on the road network; spawns and vehicles on usable ground; save and load of a region game; version-1 saves stay the valley.

## Known limitations
- Loading the region takes about 7.6 s (the valley about 2.4 s), mostly building every prop and container view at once.
- Props and buildings are all built at load (about 240 buildings in the region); only terrain, resource nodes and the NavMesh stream. A world much larger than 2 km needs sector streaming of props and structure views.
- Beyond 260 m trees are not drawn in the valley and the 2 km region (the basin has distant stand-ins out to 1.25 km).
- The map overview is 1 m per pixel in the region; detail tiles are still to do.
- Colonists far from the survivor (outside the NavMesh window) move in straight lines until the survivor comes back.
- Motion capture covers walking and running; other movements are procedural.
- City floors are built on the main thread, one per frame: an office floor full of desks takes up to 28 ms (one dropped frame). Building the meshes on a worker would remove it.
- The stair core is always on the west side of a building or house, and every flight rises north; floors differ in layout and furniture, not in where the stair is.
