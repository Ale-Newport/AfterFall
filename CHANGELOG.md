# Changelog

All notable changes, grouped by release and milestone. Dates are UTC. The project follows [Semantic Versioning](https://semver.org); until 1.0, minor versions may change save formats and gameplay.

## [Unreleased]

### Removed
- Local AI-assistant configuration and promotional drafts are no longer tracked (they stay on the maintainer's machine and are git-ignored), along with the obsolete pre-overhaul screenshots in `docs/screenshots` (replaced by `docs/media`).

### Changed
- Farm planning drops an always-false check (CodeQL).

## [0.1.1] - 2026-11-20

A Windows build, save-compatibility and colony fixes, and much deeper automated play testing. Saves from 0.1.0 load in 0.1.1.

### Added
- A Windows (64-bit) build next to the universal macOS build. The `Windows player` workflow runs the scripted tour of it on a GitHub-hosted Windows runner; `tools/package-windows.sh` packages it.
- Progression tests: on eight generated worlds, everything a new game can reach is worked out from what the world actually contains (starting kit, rolled loot, resource nodes, points of interest), and every technology, building, recipe and required item must be reachable. Another test checks that every content reference points at an existing definition.
- Soak tests: a settlement with two colonists, a furnace, crops and threats runs for two in-game days with a save and reload every quarter-day, checking validation, reservations, carry weight and positions throughout. A reloaded game must evolve exactly like the game that was never saved, compared over ten in-game minutes.
- Regression tests for every fix below (155 EditMode tests in total).

### Fixed
- A content update that removes an item or lowers its stack size no longer makes saves with in-progress hauls unloadable: reservations that no longer fit are dropped with a warning, and the haul is planned again ([#5](https://github.com/Ale-Newport/AfterFall/issues/5)).
- Colonists no longer hold back fuel while ingredients are on their way to a machine, and wood hauled as fuel no longer counts toward a recipe that also takes wood ([#6](https://github.com/Ale-Newport/AfterFall/issues/6)).
- After a load, systems that tick slowly (farming, the threat director, resource regrowth) lost up to one tick of time. Each system's progress toward its next tick is now saved.
- Actions taken while dead (building, crafting, transfers) now say why they fail instead of failing silently.
- Scripted tour: `-afterfall-tour-patience <factor>` for slow machines; the infected screenshot is taken as soon as they close in; the power check fails only on a consumer that is denied power.
- Code quality ([#4](https://github.com/Ale-Newport/AfterFall/issues/4)): unused assignments removed (rendered textures and icons are byte-identical), explicit null checks for harvesting without a tool, farm plots without a farm definition, unpowered machines and generator rows in the strategic panel, and a documented empty `catch` in save-slot listing.

### Changed
- Version 0.1.1 in the player settings.

## [0.1.0] - 2026-11-19

The first public release: an early playable prototype of the whole survival → industry → settlement loop. A universal macOS build is attached; the Windows build is untested, and nobody has played the loop by hand yet.

### Highlights
- A deterministic 512 m valley with six points of interest, harvestable forest, a lake and roads.
- Third-person survival: hunger, thirst, stamina, carry weight, death with recoverable belongings.
- 36 items, 20 recipes, 17 buildable structures with snapping and explained placement rules.
- Furnace, machine workshop and electric pump; generators, poles, lamps and batteries on a power grid with priority load shedding.
- Two recruitable colonists who haul, build, farm, eat, sleep and flee, directed from a strategic view without omniscience.
- Wanderer and Runner infected with vision, hearing and barrier breaching, and a fair threat director.
- Versioned, checksummed JSON saves with migrations and backup recovery.
- Verified by 131 EditMode tests (headless on Linux, Windows and macOS, and in Unity), 7 PlayMode tests, and a scripted tour of the macOS build with 0 failed checks and 0 logged errors.

### Added
- Visual overhaul (2026-11-09): a textured surface shader with procedural textures; illustrated icons for every item; sculpted low-poly characters with procedural animation and held tool models; crafted models for every structure, machine and container; weathered architecture and world props; textured terrain, instanced ground cover and a lake shader; a dynamic sky with sun, moon, stars and clouds, time-of-day lighting and a filmic grade.
- `tools/gallery.sh` and the `ArtPreview` tool for visual review of every model, icon and texture.
- Open-source project files: MIT license, contributing guide, code of conduct, security policy, issue forms, pull request template, CODEOWNERS and `.editorconfig`.
- CI on Linux, Windows and macOS with lint (warnings as errors, formatting, `.meta` files, actionlint, shellcheck) and a NuGet vulnerability audit; CodeQL for C# and workflows; a gitleaks history scan and pull-request dependency review; Dependabot for actions and NuGet; a release workflow that publishes notes from this changelog.
- `tools/package-macos.sh` packages the macOS build with a SHA-256 checksum and attaches it to a release.
- README demo GIF, screenshots and social preview image from the scripted tour (`docs/media`, kept out of Git LFS).

### Changed
- Test tooling: Microsoft.NET.Test.Sdk 18.12.0 and NUnit3TestAdapter 6.3.1. NUnit stays on 3.x and Newtonsoft.Json on 13.0.2 to match Unity.
- The CI workflow is now `ci.yml` (it was `simulation-tests.yml`).

### Fixed
- Loaded games now continue the same random sequences: the player, infected, threat and farming streams are saved (before, a load restarted harvest yields, loot, crop yields, wandering and spawns from the start of the game). Infected attack cooldowns and idle and search timers are saved too, so an attacking infected no longer strikes the instant a save loads. Older saves still load and start those streams fresh.
- A dead colonist no longer keeps their bed, so a new survivor can be recruited into it.
- Taking the last stack out of a ground drop one slot at a time removes the empty bag, as Take All already did.
- Watering a crop with a full backpack destroyed the empty bottle; it now asks you to make room first.
- Clearing a machine's recipe during a cycle locked the machine; it now waits for the cycle like changing the recipe does.
- An announced raid spawned at its original point even if the player had walked up to it or built next to it in the meantime. The group now changes course with a fresh warning, or the raid is called off when no fair point exists.
- Seven regression tests cover these fixes (131 EditMode tests in total).
- Mesh normals on millimetre bevels (NaN lighting showed as white blobs); new games start in 2.4 s instead of 3.3 s thanks to background texture warm-up.
- `tools/run-unity-checks.sh` and `tools/gallery.sh` stop if they cannot change to the repository root.

### Security
- Branch and tag rulesets: changes from anyone but the maintainer need a pull request with code-owner approval; force pushes and deletions are blocked.
- Secret scanning with push protection, Dependabot alerts and security updates, private vulnerability reporting, read-only workflow tokens, SHA-pinned actions and approval before workflows run for outside contributors.
- `.gitignore` excludes signing keys, certificates and provisioning profiles; `.env.example` documents the optional tool variables.

## [0.1.0-dev.3] - First runs in Unity (2026-11-07)

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

## [0.1.0-dev.2] - Phase 6 groundwork (2026-11-07)

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

## [0.1.0-dev.1] - Phase 0–5: prototype foundation (2026-11-07)

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

[Unreleased]: https://github.com/Ale-Newport/AfterFall/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/Ale-Newport/AfterFall/releases/tag/v0.1.1
[0.1.0]: https://github.com/Ale-Newport/AfterFall/releases/tag/v0.1.0
