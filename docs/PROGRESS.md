# Progress log

_Last updated: 2026-11-07 (session 2: first runs in Unity)._

## Snapshot
| | |
|---|---|
| Branch | `main` (every commit pushed to `origin`) |
| Unity version | 6000.3.28f1 (arm64), installed through Unity Hub 3.22.6 with a Personal license |
| Last headless tests | 2026-11-07: **120/120 passed** (`dotnet test`, .NET 10.0.403, macOS 27, Apple M2 Max). GitHub Actions runs the same suite on each push. |
| Last Unity EditMode run | 2026-11-07: **120/120 passed** in 6000.3.28f1 (`tools/run-unity-checks.sh editmode`) |
| Last Unity PlayMode run | 2026-11-07: **5/5 smoke tests passed** (plus 2 Input System integration tests) |
| Last player build | 2026-11-07: macOS (Apple Silicon), 118 MB, no errors |
| Last scripted tour of the build | 2026-11-07: **0 errors logged**. New game in 1.1 s, NavMesh in 1.4 s. Frames average 8.4 ms (holding the 120 Hz vsync cap); p95 9.3 ms, worst 21 ms. Save and reload OK. |
| Human playtest of the acceptance loop | **not yet** |
| Windows build | **not yet built** |

## Completed and verified headlessly
Verified means unit and integration tests pass under .NET, and the code compiles against the Unity reference assemblies.
- **Repository:** Unity-aware ignore rules, LFS attributes, local LFS, commit-msg hook blocking AI trailers, attribution disabled in `.claude/settings.json`, CI workflow.
- **Core:** ids, deterministic RNG, typed event bus with journal, clock, fixed-rate scheduler with per-system timings.
- **Economy:** atomic inventories, weight and filters, durability, reservations, transfer tickets, randomised conservation test.
- **Systems:**
  - crafting and technology (5 techs, including Agriculture)
  - construction and placement rules; repair and demolish
  - industry (furnace, workshop, pump; semi-automation)
  - power (priority shedding, batteries, fuel)
  - survival
- **Infected:** Wanderer and Runner, perception (player and colonists), noise, breaching barriers, threat director with fair spawns and announced raids.
- **Colony:**
  - recruitable survivors and a job planner (blueprint delivery, building, machine inputs and fuel, product storage, generator refuelling, farming)
  - assignment by priority, preference, skill and distance; one owner per job
  - reservation-backed hauling with recorded failure reasons
  - needs (eat, sleep), fleeing, death drops
- **Agriculture:** planter beds, watering, pump irrigation, harvest with returned seed.
- **World generation** (deterministic) and new-game factory; persistence of everything above, with tamper detection, migrations and backup recovery.
- **Headless acceptance loops:**
  - bare hands → shelter → save/load → continue
  - ore → ingot → electricity → gears → pump, including a fuel outage and recovery
  - colonist farming and logistics

## Verified in Unity (2026-11-07)
These were exercised in the editor (PlayMode tests) or in the macOS build (scripted tour, with screenshots inspected):
- **Shell and world:**
  - `Main.unity` bootstrap, main menu and a new Survival game;
  - runtime terrain with splats, POI buildings, resource views and real-time shadows.
- **Cameras:** shoulder camera; strategic camera with the fog of war over unexplored land.
- **Survivor:**
  - keyboard movement (W) through the Input System;
  - harvesting a branch pile into the inventory.
- **Windows and HUD:** backpack and crafting window, build menu, HUD vitals and hotbar, prompts.
- **Construction:** placement ghost for a foundation, with a valid result and its explanation.
- **Infected:** spawned Wanderer and Runner walking to and attacking the survivor (NavMesh); sound captions.
- **Day and night:** morning, midday, afternoon and 22:00 lighting.
- **Saves:** save and reload, in both the PlayMode test and the build.

## Implemented, not yet exercised in Unity
These compile and have been code-reviewed, but neither the tests nor the tour exercise them yet (next: a manual playtest):
- **Menus:** continue, load list, settings (rebinding, volume, captions, UI scale), controls and quit screens; difficulty and seed choice.
- **Survivor:** melee, held items, repair (E), demolish (hold X), death and the belongings beacon.
- **World:** lake and boundary up close, structure and crop visuals, drops.
- **Construction:** actually placing and building (delivery, hold E), snapping to planned foundations.
- **Industry and power:** machine windows, furnace, workshop, generators, cables, batteries, power shedding.
- **Strategic view:** selection, orders, cables, labels for structures, the colony panel with recruited colonists.
- **Colonists and farming:** recruitment, hauling, building, farming jobs on NavMesh.
- **Extras:** procedural audio output (captions were seen), tooltips, F3 overlay.
- **Tooling:** Windows command-line build; editor menus (Validate Content was run by the build).

## Review (2026-11-07)
Before Unity was available, an independent review of the Unity layer found:
- a **crash that froze every game on the loading screen**, introduced by the colony panel integration;
- invisible windows over the main menu;
- inside-out roof meshes;
- fog variants that builds would strip;
- agents walking during pause;
- editor-only tooltips;
- a culled death beacon;
- a row layout in the colony cards;
- placement through blueprint foundations;
- hotkeys replacing pause-menu windows;
- per-session leaks;
- an editor shortcut clash.

All twelve are fixed (commit `fix(game): runtime issues found in presentation review`). This shows how much risk remains until the game runs in Unity.

## Partially implemented
- **Strategic orders:** machine recipes, on/off, cables and colonist work priorities work. Work zones and storage designation are not done.
- **Colony:** Haul, Build, Farm, Eat and Rest work. Guard, medic and mechanic professions, morale effects and more than two survivors are not done.
- **Accessibility:** rebinding, UI scale, sensitivity, FOV, invert Y, camera shake, captions and text-based status are done. A colour-blind review and a tactical pause toggle (the setting exists in difficulty presets but has no UI) are not.
- **Survival:** health, stamina, hunger, thirst and carry weight are done. Fatigue, temperature, injuries and infection are not.

## Not started
Conveyors and pipes, weather and disasters, factions, wildlife, the armoured infected, firearms, vehicles, multiple regions and streaming, authored models and animation, recorded audio, localisation.

## First runs in Unity (session 2)
Bugs that only showed up in the engine, all fixed:
- **Saves would always be rejected.** Unity's Mono prints some re-read doubles with 17 digits, so the load-side checksum never matched; 7 EditMode tests failed. The checksum now uses a runtime-independent canonical form.
- **Invisible, then unlit, terrain in builds.** Instanced terrain needs shader variants that player builds strip. Fixed by drawing the terrain non-instanced.
- **Glossy "ice" ground.** URP terrain takes smoothness from the layer texture's alpha.
- **Illegible HUD bar labels; an empty grey banner; an overflowing panel heading.**
- **A URP warning on every new game** (`Camera.layerCullSpherical`).

## Known issues and risks (severity)
- **High — no human playtest yet.** Feel, pacing and the full acceptance loop (machines, electricity, colonists, farming) still need someone at the keyboard. Follow the manual script in docs/TESTING.md.
- **Medium.** UI windows are still slightly see-through, and some visual tuning remains: ground colours, morning light levels.
- **Medium.** The fog of war is only partly opaque, and trees taller than its 9 m layer show through.
- **Medium.** The threat director treats distance as "out of sight" and does not check the camera frustum.
- **Medium.** Irrigation does not add electrical load; a pump irrigates whenever its grid has supply.
- **Low.** The colony planner and machine automation scan all structures (fine at prototype scale).
- **Low.** UI glyphs (·, —, …, ×) rely on the default font; untested.

## Metrics (headless)
- Simulation cost: **0.007 ms per 60 Hz frame** with 76 structures, 40 machines and 26 infected (Apple M2 Max, .NET 10), down from 0.030 ms before caching the structure order. Machines average 0.06 ms per 4 Hz tick.
- Inventory conservation errors in automated tests: **0** (randomised operations, save round trips, colonist hauls interrupted mid-carry).
- In-engine (macOS build, 1600×900, Apple M2 Max): average frame 8.4 ms, vsync-capped at 120 Hz, at the start camp and in the strategic view; new game ready in 1.1 s; NavMesh bake 0.3 s after the world.
- Not yet measured (needs playtests): time to first construction, time to first machine output, playtime without critical bugs.

## Next development tasks
1. **Playtest the acceptance scenario** (docs/TESTING.md) by hand in the editor or the macOS build. Tune movement, camera, harvest and colonist pacing, and day-length balance. Record time to first construction and to first machine output.
2. Extend the scripted tour to machines, electricity, colonists and farming, so those paths are exercised in the build on every run.
3. Work zones and storage designation in the strategic view; Guard profession (engage infected near the base).
4. Conveyors (automation level 3) between storage and machines.
5. Weather (storm or cold snap) with warnings and consequences for power and needs.
6. Recorded audio and authored character animation where licensed assets are available.

## How to run (current)
- Headless tests: `dotnet test tools/dotnet/AfterfallSim.Tests/AfterfallSim.Tests.csproj`
- Compile check: `python3 tools/unity-compile-check/compile_check.py --unity <Unity.app/Contents> --packages <PackageCache or extracted tarballs>`
- In Unity: `tools/run-unity-checks.sh` (EditMode, PlayMode, macOS build, scripted tour; results in `Logs/unity-checks/`).
- Editor: open the project in Unity Hub with 6000.3.28f1 → `Main.unity` opens automatically → Play → New Game. Do not run the script while the project is open in the editor.

## Session checkpoint notes
- Session 1 extracted the official installer (`Unity-6000.3.28f1.pkg`) and package tarballs into a temporary scratch directory for compile checks. Since session 2 the installed editor and `Library/PackageCache` can be used instead (docs/TESTING.md §2).
- The original GDD `.docx` was not found; docs/GDD.md is an adaptation of the master brief.
- `gh` CLI is not authenticated; pushes use the existing HTTPS credential helper. Direct pushes to `main`, including workflow files, succeeded.
