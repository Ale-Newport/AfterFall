# Changelog

All notable changes, grouped by milestone. Dates are UTC.

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
