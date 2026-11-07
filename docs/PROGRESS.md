# Progress log

_Last updated: 2026-11-07 (session 1)._

## Snapshot
| | |
|---|---|
| Branch | `main` (pushed to `origin`) |
| Unity version | 6000.3.28f1 (pinned; **no licensed editor available in session 1**) |
| Last successful headless tests | 2026-11-07: **100/100 passed** (`dotnet test`, .NET 10.0.403, macOS 27 on Apple M2 Max) |
| Last successful Unity compile check | 2026-11-07: player and editor passes **PASSED** against Unity 6000.3.28f1 reference assemblies (all runtime, editor and test assemblies) |
| Last Unity Editor run | **never** |
| Last player build | **never** |

## Completed and verified headlessly
Verified means unit and integration tests pass under .NET, and the code compiles against the Unity reference assemblies.
- Repository setup: Unity-aware ignore rules, LFS attributes, local LFS enabled, commit-msg hook blocking AI trailers (self-tested), `.claude/settings.json` attribution disabled.
- Core: `EntityId`/`IdAllocator`, `Vec3`, `DeterministicRandom`, `EventBus` with journal and isolated handlers, `SimulationClock`, `TickScheduler` (per-system timing).
- Inventory economy: atomic operations, weight and filters, durability and wear, reservations, transfer tickets, randomised conservation test.
- Crafting, technology unlocks, construction services, placement rules, industry, power, survival, zombie brain, perception and noise, zombie system, threat director, harvesting, regrowth.
- World generator (deterministic) and new-game factory (nodes, looted containers, initial infected).
- `GameSession` commands with range, stamina, cooldown and access checks; death and respawn; autosave via `SaveService`.
- Persistence: full round trip, tamper and truncation detection, migrations, backup recovery.
- Headless acceptance loops: bare hands → shelter → save/load → continue; and ore → ingot → electricity → gears → pump, with a fuel outage and recovery.

## Implemented, awaiting in-Unity verification
These compile but have **not been run**:
- `Main.unity` bootstrap; main menu (continue, new game with difficulty and seed, load, settings, controls, quit).
- Runtime world construction: terrain and splats, lake, boundary, procedural POI buildings, resource and structure visuals, drops with a belongings beacon.
- Player controller and procedural humanoid, held-item models, interaction targeting, harvesting, melee.
- Cinemachine shoulder camera (collision, zoom) and strategic camera with blending.
- NavMesh runtime bake, NavMesh zombies with line of sight and stuck/path recovery.
- Build mode: ghost, snapping, rotation, physics probe, placement panel.
- Strategic view: selection, orders, cables, fog of war, labels, seen-threat markers.
- UI Toolkit HUD and windows (backpack and crafting, container, machine/generator/battery, construction, pause, settings with rebinding, help, death).
- Day/night lighting, F3 performance overlay, editor menus, command-line builds, 5 PlayMode smoke tests.

## Partially implemented
- Defence: walls and barricades block movement (NavMesh carving), but **zombies do not attack structures**; structure damage only comes from code paths (no in-game source yet).
- Strategic orders: machine recipes, on/off and cables work. Work zones, storage designation and priorities are not implemented (no NPCs yet).
- Accessibility: rebinding, UI scale, sensitivity, FOV, invert Y, camera shake toggle and text-based status are done. Subtitles (no audio yet) and a colour-blind review are not.
- Survival: health, stamina, hunger, thirst and carry weight are done. Fatigue, temperature, injuries and infection are not.

## Not started
NPC colonists and the job scheduler, conveyors and pipes, agriculture, weather and disasters, factions, wildlife, runner and armoured infected, firearms, vehicles, multiple regions and streaming, audio, authored models and animation, localisation.

## Known issues and risks (severity)
- **High — unverified in engine.** Runtime issues may surface on first Play (layer setup, URP material look, UI layout, NavMesh bake time on a 512 m terrain, Cinemachine tuning). Run the PlayMode tests and the manual script in docs/TESTING.md first.
- **Medium.** The threat director treats distance as "out of sight" and does not check the camera frustum.
- **Medium.** Machine automation neighbour search is O(n²) over structures (fine at prototype scale; it averages 0.36 ms per 4 Hz tick with 76 structures).
- **Low.** Saving while a crafting job waits for inventory space persists the job, but the waiting state is recomputed on load.
- **Low.** The strategic fog is a terrain-following overlay 9 m above ground, so tall props can poke through.
- **Low.** Some UI glyphs (·, —, …, ×) depend on the default font; untested.

## Metrics (headless)
- Simulation cost: **0.030 ms per 60 Hz frame** with 76 structures, 40 machines and 26 infected (Apple M2 Max, .NET 10). Per-system averages: Machines 0.36 ms/tick (4 Hz), Power 0.013 ms, Threat 0.053 ms, AI 0.010 ms.
- Inventory conservation errors in automated tests: **0** (3,000 randomised operations, plus round trips).
- Not yet measurable (needs playtests): time to first construction, time to first machine output, in-engine frame time, playtime without critical bugs.

## Next development tasks
1. **With Unity available:** install 6000.3.28f1 via Unity Hub, sign in to activate a Personal license, open the project, enter Play Mode, fix any runtime errors, run the EditMode and PlayMode tests from the CLI, and update this file with results and real screenshots.
2. Playtest the acceptance scenario in docs/TESTING.md; tune movement, camera, harvest rates and balance; record time to first construction and first machine output.
3. Zombie attacks on barricades and walls (domain `ConstructionService.Damage` exists), and repair UX.
4. Audio pass with licensed or original sounds (footsteps, impacts, machines, infected) and subtitles.
5. First colonist and a hauling job on `ReservedTransfers`; storage designation in the strategic view.
6. Spatial grid for structure queries; profile on target hardware; LOD and impostors for trees if needed.

## How to run (current)
- Headless tests: `dotnet test tools/dotnet/AfterfallSim.Tests/AfterfallSim.Tests.csproj`
- Compile check: `python3 tools/unity-compile-check/compile_check.py --unity <Unity.app/Contents> --packages <PackageCache or extracted tarballs>`
- Unity: open the project in Unity Hub with 6000.3.28f1 → `Main.unity` opens automatically → Play → New Game.

## Session checkpoint notes
- Session 1 extracted the official installer (`Unity-6000.3.28f1.pkg`) and package tarballs into a temporary scratch directory to compile-check. They are **not** in the repository; see docs/TESTING.md §2 to recreate them.
- The original GDD `.docx` was not found; docs/GDD.md is an adaptation of the master brief.
- `gh` CLI is not authenticated; pushes use the existing HTTPS credential helper. Direct pushes to `main` succeeded.
