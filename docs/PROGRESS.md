# Progress log

_Last updated: 2026-11-07 (end of session 1)._

## Snapshot
| | |
|---|---|
| Branch | `main` (every commit pushed to `origin`) |
| Unity version | 6000.3.28f1 (pinned; **no licensed editor available in session 1**) |
| Last successful headless tests | 2026-11-07: **119/119 passed** (`dotnet test`, .NET 10.0.403, macOS 27, Apple M2 Max). GitHub Actions runs the same suite on each push. |
| Last successful Unity compile check | 2026-11-07: player and editor passes **PASSED** against Unity 6000.3.28f1 reference assemblies (runtime, editor, EditMode and PlayMode test assemblies) |
| Independent code review of the Unity layer | 2026-11-07: 12 runtime issues found and fixed (see "Review" below) |
| Last Unity Editor run | **never** |
| Last player build | **never** |

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

## Implemented, awaiting in-Unity verification
These compile and have been code-reviewed, but have **not been run**:
- **Shell:** `Main.unity` bootstrap; main menu (continue, new game with difficulty and seed, load, settings, controls, quit); loading screens.
- **World:** runtime construction (terrain and splats, lake, boundary, POI buildings, resource, structure and crop visuals, drops with a belongings beacon).
- **Survivor:** player controller, procedural humanoid, held items, interaction, harvesting, melee, repair (E) and demolish (hold X).
- **Cameras:** Cinemachine shoulder camera and strategic camera.
- **Characters:** NavMesh zombies and colonists (runtime async bake).
- **Construction:** build mode (ghost, snapping including planned foundations, physics probe).
- **Strategic view:** selection, orders, cables, fog of war, labels, colony panel.
- **UI Toolkit:** HUD, windows, tooltips, settings (rebinding, volume, captions).
- **Presentation extras:** procedural audio and captions; day/night lighting; F3 overlay.
- **Tooling:** editor menus, command-line builds, 5 PlayMode smoke tests.

## Review (2026-11-07)
An independent review of the never-run Unity layer found:
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

## Known issues and risks (severity)
- **High — never run in engine.** Expect first-run issues: URP material look, UI layout and scale, NavMesh bake time on the 512 m terrain, Cinemachine tuning, colonists pathing to targets inside carved obstacles. Start with the PlayMode tests and the manual script in docs/TESTING.md.
- **Medium.** The threat director treats distance as "out of sight" and does not check the camera frustum.
- **Medium.** Irrigation does not add electrical load; a pump irrigates whenever its grid has supply.
- **Low.** The colony planner and machine automation scan all structures (fine at prototype scale).
- **Low.** The strategic fog is a terrain-following overlay 9 m up; tall props can poke through.
- **Low.** UI glyphs (·, —, …, ×) rely on the default font; untested.

## Metrics (headless)
- Simulation cost: **0.007 ms per 60 Hz frame** with 76 structures, 40 machines and 26 infected (Apple M2 Max, .NET 10), down from 0.030 ms before caching the structure order. Machines average 0.06 ms per 4 Hz tick.
- Inventory conservation errors in automated tests: **0** (randomised operations, save round trips, colonist hauls interrupted mid-carry).
- Not yet measurable (needs playtests): time to first construction, time to first machine output, in-engine frame time, playtime without critical bugs.

## Next development tasks
1. **With Unity available:**
   - install 6000.3.28f1 via Unity Hub and sign in to activate a Personal license;
   - open the project and enter Play Mode; fix runtime errors;
   - run the EditMode and PlayMode tests from the command line;
   - update this file with results and real screenshots.
2. Playtest the acceptance scenario (docs/TESTING.md). Tune movement, camera, harvest and colonist pacing, and day-length balance. Record time to first construction and to first machine output.
3. Work zones and storage designation in the strategic view; Guard profession (engage infected near the base).
4. Conveyors (automation level 3) between storage and machines.
5. Weather (storm or cold snap) with warnings and consequences for power and needs.
6. Recorded audio and authored character animation where licensed assets are available.

## How to run (current)
- Headless tests: `dotnet test tools/dotnet/AfterfallSim.Tests/AfterfallSim.Tests.csproj`
- Compile check: `python3 tools/unity-compile-check/compile_check.py --unity <Unity.app/Contents> --packages <PackageCache or extracted tarballs>`
- Unity: open the project in Unity Hub with 6000.3.28f1 → `Main.unity` opens automatically → Play → New Game.

## Session checkpoint notes
- Session 1 extracted the official installer (`Unity-6000.3.28f1.pkg`) and package tarballs into a temporary scratch directory for compile checks. They are **not** in the repository; docs/TESTING.md §2 explains how to recreate them.
- The original GDD `.docx` was not found; docs/GDD.md is an adaptation of the master brief.
- `gh` CLI is not authenticated; pushes use the existing HTTPS credential helper. Direct pushes to `main`, including workflow files, succeeded.
