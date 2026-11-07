# Changelog

All notable changes, grouped by milestone. Dates are UTC.

## [Unreleased] — First runs in Unity (2026-11-07)

### Added
- The tour covers every system: menus, a shelter with a barricade the infected breach, repair, demolish, melee, fleeing, eating and sleeping colonists, a second survivor's long walk, a pump outpost with irrigation, poles and cables, batteries, priority shedding, death and recovery, pause and the F3 overlay.
- The tour builds and runs a settlement: construction, technology unlocks, smelting, a powered workshop, a power outage and recovery, a recruited colonist hauling, building and farming, and a save and reload that keeps all of it. Checks print `FAIL` lines, and `tools/run-unity-checks.sh` fails on them.
- `BuildController.PlanAt` (planning through the interactive placement rules) and `CameraRig.Look` for tools.
- `-afterfall-tour <dir>`: a scripted tour of a build. It visits the main screens, captures screenshots, logs rendering state and frame times, counts logged errors, then quits.
- `tools/run-unity-checks.sh`: EditMode and PlayMode tests, a macOS build and the tour in one command.
- In-engine screenshots in the README.

### Fixed
- Saves were rejected as corrupt under Unity's Mono runtime: the checksum depended on number formatting.
- Terrain was invisible, then unlit and glossy, in player builds (instanced terrain variants stripped; smoothness read from texture alpha).
- Illegible HUD bar labels, an empty banner, an overflowing panel heading, see-through windows, and the wording of the crafting-station message.
- A URP warning on every new game (`Camera.layerCullSpherical`).
- Survivors hiding indoors snapped onto roofs (roofs, ceilings and canopies are no longer walkable).
- Most POI buildings were unreachable for colonists and infected (NavMesh agent 0.35 m, doors 1.6 m).
- Colonists walked forever toward unreachable targets; they now give up with a reason.
- Colonists burned building planks as fuel; automatic refuelling uses only items tagged `fuel`.
- Raw machine state names in the UI, overflowing recipe buttons, toasts over the strategic panel, and "Idle: Idle" in colonist cards.
- Player-built doorways and foundations sealed the NavMesh (one carve box per footprint); each blocking part now carves on its own.
- Walls could not meet at foundation corners, so shelters could not be closed.
- New recruits idled at their hideout; idle colonists far from their bed walk home.
- Infected never breached barriers in the game: the end of a partial path counted as arrival.
- Each death was handled twice (two events, two messages, a doubled death count).
- White text fields hid typed text; movement bindings read "Move Up/Down".
- Tour runs autosaved into the player's save folder.

### Changed
- Unity 6000.3.28f1 serialization upgrades of project settings and URP assets; `Packages/packages-lock.json` is committed.

## [Unreleased] — Phase 6 groundwork (2026-11-07)

### Added
- Colony: recruitable survivors, a job board (haul, build, farm, eat, rest), assignment by priority, preference, skill and distance, reservation-backed hauling with failure reasons, needs, fleeing, death, saves, and a strategic colony panel.
- Agriculture: seed potatoes, planter beds, watering, pump irrigation, harvests, the Agriculture technology.
- Runner infected archetype; infected batter barriers that block them and hunt colonists.
- Repair (E on damaged barriers, panel buttons) and demolish (hold X) controls.
- Procedural audio with captions and a master volume setting.

### Fixed
- Twelve runtime issues in the never-run Unity layer, found by an independent code review. They include a crash that would have frozen new games on the loading screen, hidden main-menu windows, inside-out roofs, build fog stripping, agents moving while paused, and editor-only tooltips.

### Changed
- Simulation performance: the busy-settlement frame cost dropped from 0.030 ms to 0.007 ms after caching the structure order.
- The loading screen is shown before world generation and save loading.

## [Unreleased] — Phase 0–5: prototype foundation (2026-11-07)

### Added
- Unity 6000.3.28f1 project with pinned packages, URP settings from the official 3D (URP) template, Git LFS rules, a repository-local commit-msg hook that blocks AI attribution, and project Claude Code settings with attribution disabled.
- Engine-independent simulation core: ids, deterministic RNG, typed events, 48-minute day clock, fixed-rate tick scheduler.
- Transactional inventory: weight limits, filters, durability, item and space reservations, two-phase transfer tickets.
- Crafting queue, 20 recipes, technology unlocks driven by world activity (4 technologies).
- Construction: placement rules with explanations, blueprints, delivery, work, cancel, demolish, repair, no item loss.
- Industry: furnace, machine workshop, water pump; explicit machine states; semi-automatic adjacent-storage transfer.
- Electricity: generators, poles, lamps, batteries, priority load shedding, load-proportional fuel use.
- Survival needs, death drops and respawn; Wanderer infected AI with perception and noise; threat director with fair spawning and announced raids.
- Deterministic 512 m world: lake, ridge, roads, ~1,000 trees, six POIs and a start camp.
- Versioned saves with checksums, migrations, validation, atomic writes and backup recovery.
- Unity presentation: runtime-built world (terrain, props, nodes, structures), third-person character with procedural animation, Cinemachine third-person and strategic cameras, NavMesh zombies, build mode with snapping, strategic view with fog of war and power overlay, UI Toolkit HUD, windows, menus and key rebinding, day/night lighting, performance overlay.
- Editor tooling (scene bootstrap, content validation, benchmark, command-line builds), PlayMode smoke tests, and a Unity compile-check harness that needs no license.
- 100 EditMode tests (run headlessly via .NET and in CI).

### Known limitations
- Not yet run in a licensed Unity Editor; in-engine behaviour is unverified (see docs/PROGRESS.md).
