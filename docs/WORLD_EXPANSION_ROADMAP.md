# World expansion roadmap

Goal: a large, persistent, explorable region with realistic characters, driveable vehicles, a creative/debug mode and a map, built on top of the existing survival, colony, industry and construction systems. The audit is in WORLD_EXPANSION_AUDIT.md; progress is in WORLD_EXPANSION_PROGRESS.md.

Each phase ends with headless tests, the Unity EditMode and PlayMode suites, a macOS build and a scripted-tour scene with screenshots that were looked at.

| Phase | Content | Acceptance |
|---|---|---|
| 0 | Audit, baselines, this roadmap | Audit written; baseline tests pass |
| 1 | Game modes (Survival, Creative) and a developer toggle; creative/debug panel (F6) with a searchable item browser over the real catalogue, teleports (POIs, coordinates, bookmarks, back), free flight (F7), creative construction (free, instant, placement rules kept), diagnostics toggles | From the running game: spawn items from the browser, fly fast to another POI without walking animation, teleport, build for free, return to normal play on safe ground |
| 2 | Map data (projection, rasterised map from the layout, fog of war from exploration), world map (M) with pan, zoom, labels, waypoints and custom markers, minimap (bottom right), waypoint indicator; markers saved | Open the map, set a waypoint at a distant POI, close it, walk toward it and see the minimap and distance update; markers survive a reload |
| 3 | Doors (open, closed, locked with keys, barricaded, broken; persistent; NavMesh-aware) and a three-storey reference building with stairs, rooms, furniture and loot | Climb all three floors, open and lock doors, search rooms; the infected and colonists respect doors; door state survives a reload |
| 4 | Vehicles: domain state (fuel, condition, lock, key, storage, position), driving physics, doors, enter/exit, keys, an abstract hotwiring action, refuelling, repair, persistence; vehicle spawner in the creative panel | Find a car, get its key or hotwire it, open the door, drive, park, exit, save, reload, find it where it was |
| 5 | World v2: layout size as a property, terrain tiles, sector streaming of views, a NavMesh baked around the player; a larger region with a town, a remote village, forest, a river with bridges, and a distant urban district joined by roads. World v1 (the valley) stays for old saves | Drive between settlements without load stalls; nothing persistent disappears when its sector unloads |
| 6 | Characters: imported skinned humans (licensed, provenance recorded), an Animator with locomotion and actions, appearance data (body, face, hair, clothing layers), infected variants | The player, a survivor and an infected use the imported models in the running game |
| 7+ | More regions toward the 30–60 km target, more building archetypes, vehicles and furniture, road-aware routing, optimisation and benchmarks | Measured frame times and memory in representative areas |

## Ordering notes
- Characters come later than the brief's order because they are blocked on asset access (no Mixamo, Asset Store or Fab login here; downloads need the owner's approval). Every other phase can proceed now.
- Vehicles come before the large world so the world can be tuned for driving distances.
- The map system is generic over the layout, so it works for world v1 and v2.
