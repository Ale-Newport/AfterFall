# Creative and developer mode

AFTERFALL has two game modes and a developer switch. All three are real in-game features, available in the built player, not editor scripts.

| | Survival | Survival with developer tools | Creative |
|---|---|---|---|
| Chosen | New Game → Mode: Survival | F6 → "Enable developer tools" in a survival game | New Game → Mode: Creative, or F6 → World → Creative |
| Rules | All apply | All apply | No costs, no build time, every blueprint, no harm, anything can be removed |
| Creative panel (F6), item spawning, teleports, free flight (F7) | Panel opens with an "Enable" button; nothing else works | Yes | Yes |
| Save label (load list, header `Mode`) | `Survival` | `Survival · developer tools` | `Creative` |

Enabling developer tools or switching to Creative sets `GameSettings.DeveloperTools` for good, so a survival run that used them can always be told apart. Saves written before modes existed load as plain Survival.

## Controls
| Key | Action |
|---|---|
| F6 | Creative & developer panel (does not pause) |
| F7 | Free flight on/off |
| WASD | Fly along the view |
| Space / Ctrl or C | Up / down |
| Shift | Boost (×4) |
| Mouse wheel | Cruise speed (2–600 m/s) |
| F3 | Diagnostics overlay |

All are rebindable in Settings → Controls.

## Creative panel (F6)
- **Items.** A searchable list of every item in `items.json`, read from `ItemCatalog` (`CreativeTools.SearchItems`): nothing is listed by hand, so new items appear on their own. It matches names, ids and tags; filters by category, favourites and recently spawned items (kept in PlayerPrefs); shows the icon, name, id, category, stack size, weight, durability, tags and description. The list is a virtualised `ListView`, so thousands of items stay responsive. Spawning goes through the normal inventory code: into the backpack (overflow is dropped at the survivor's feet, never lost), onto the ground in front, or into the last container opened. Durable items arrive at full durability; at most 999 per spawn.
- **World.** Mode switch; flight toggle and speed; current coordinates; teleport to coordinates, to any point of interest, to the waypoint, or to a bookmark; "Back to previous location"; bookmarks (saved with the game as map markers); time of day (dawn, noon, dusk, night); weather (hold clear, overcast, rain or storm, or let it change naturally).
- **Debug.** Which sections the F3 overlay shows; test spawns of a Wanderer or a Runner ahead of the survivor; remove all infected.

## Free flight (F7)
`FreeFlight` (on the player object) carries the survivor's body with the camera and hides it, so no walking or running animation ever plays. The character controller is off: no gravity, no collision, no stamina, no footstep noise. The camera loses its shoulder offset and collision and can look straight up and down; its far plane grows to 2.5 km. Flight cannot go below the terrain or outside the map. A long frame cannot throw the camera more than 0.05 s worth of travel.

While flying, the simulation is told `PlayerActivity.Flying`: the infected cannot perceive the survivor and flying over land does not reveal it on the map (discovery happens on the ground). Turning flight off lands the survivor with `SafeGround`: a downward cast from the camera finds the first walkable surface that is not a roof or ceiling (those carry the Not Walkable NavMesh area), not deep water, with room for a standing capsule; it searches outward up to 40 m, then from high above up to 120 m, and refuses to land if nothing fits. The strategic view is unavailable while flying.

## Teleports
`GameController.TeleportTo` records where the survivor was (for "Back"), waits until `IsWorldReadyAt` the destination (today the whole world is loaded; sector streaming will answer per sector), then lands on safe ground near the target, casting from high above so a building's floor is chosen over its roof. In flight it moves the flight camera 25 m above the target instead.

## Creative construction
`ConstructionService.Creative` and `PlacementRules.Evaluate(..., creative)`: technology requirements and materials are ignored and a placed blueprint completes immediately as a real, working structure. Every physical placement rule still applies (ground, slope, water, support, obstacles, distance). Removing a structure refunds nothing and works on world containers too.

## Diagnostics (F3)
Sections, each switchable in the Debug tab: performance (FPS, CPU and GPU frame time from `FrameTimingManager`, memory, rigidbodies and how many are awake), simulation (per-system tick costs, events, dropped time), entities, position (coordinates, heading, ground height, area, flight speed), navigation (NavMesh state, agents), power (each grid's capacity, demand, supply, storage and status), economy (inventories, open reservations, colonist failures), saves (slot, last save result).

## Tests
- Headless (`CreativeTests`, 10 tests): the browser lists the whole catalogue and filters; survival refuses spawning until tools are enabled and the save is then labelled; spawning into the backpack, the world and a container, with overflow kept; creative building is free, instant and ignores technology but keeps physical rules; survival building is unchanged; creative keeps the survivor unharmed; flying reveals nothing and the infected ignore a flying survivor; teleport history and bookmarks; mode, markers and waypoint survive saves, and old saves load as Survival.
- In the build (`AutoTour.Expansion.cs`, run at the end of the full tour or alone with `-afterfall-tour-expansion`): the locked state, enabling tools, searching and spawning from the panel, a flight of more than 200 m without body animation and without revealing the map, landing on safe ground, a teleport to the quarry and back, creative building of a locked generator with no materials, invulnerability, the diagnostics overlay, and the save label.
