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
| 6 Realistic characters | Researched and planned; **blocked on approval to download** MPFB2 and the MakeHuman CC0 assets | — | CHARACTER_PIPELINE.md |
| 7+ Larger regions (toward 30–60 km), more archetypes | Not started | — | — |

## Phase 5b details
- Generation: 2 × 2 km, deterministic, about 0.7–0.9 s in a release build (debug test builds are slower).
- Content: the valley unchanged in its corner; Ashford (about 100 buildings, an apartment block, a bridge), Eastgate (about 120 buildings, an apartment block, the freight line), Pinecrest (10 buildings), two farms, a lodge, a research station, the Kessel river and reservoir, about 13 000 trees, rocks and plants, about 640 containers, about 170 infected spawn points, six vehicles.
- Measured in the macOS build (Apple M2 Max, 1600×900, vsync at 120 Hz), tour `RegionScene`: playable 7.6 s after New Game (generation, 3 of 16 terrain tiles, about 240 buildings and 640 containers built, 784 of 12 772 node views), NavMesh window baked 0.2 s later. Frame time averages 8.3–8.4 ms (the vsync cap) at the start camp, in Ashford by the bridge, in Eastgate, Pinecrest and the research station; p95 at most 9.3 ms. Agents path across the Ashford bridge bank to bank; the Ashford pickup drives 168 m along the highway and over the bridge on its deck. 0 errors logged.
- Tests (`RegionTests`): determinism and size; the valley's places and buildings unchanged; buildings on dry land, off roads and apart; no trees in buildings; the river flows downhill and holds water; bridges span the channel; no road through water except on a bridge; road grades; every inhabited place on the road network; spawns and vehicles on usable ground; save and load of a region game; version-1 saves stay the valley.

## Known limitations
- Loading the region takes about 7.6 s (the valley about 2.4 s), mostly building every prop and container view at once.
- Props and buildings are all built at load (about 240 buildings in the region); only terrain, resource nodes and the NavMesh stream. A world much larger than 2 km needs sector streaming of props and structure views.
- Beyond 260 m trees are not drawn (as in the valley), so from the air distant forests show only as the ground's forest floor.
- The map overview is 1 m per pixel in the region; detail tiles are still to do.
- Colonists far from the survivor (outside the NavMesh window) move in straight lines until the survivor comes back.
- Characters are still the procedural ones.
