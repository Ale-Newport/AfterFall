# Development roadmap

Status legend: ✅ done and verified (headless tests, plus Unity tests and the scripted tour of a build where it has a presentation side) · 🟡 implemented, awaiting a human playtest · ⏳ partial · 🔲 not started

> Since 2026-11-07 every phase below has been exercised in Unity 6000.3.28f1: the EditMode suite and PlayMode tests run in the editor, and the scripted tour drives a macOS build through every system (docs/TESTING.md). What no automated check covers is a person playing with mouse and keyboard; that playtest ([#1](https://github.com/Ale-Newport/AfterFall/issues/1)) is still outstanding, so phase acceptance below means "verified by the tour and tests", not "playtested".

## Phase 0: Preproduction & environment ✅
Repository, Git LFS, ignore rules, commit-msg hook, no-attribution settings; Unity 6000.3.28f1 with pinned packages (URP 17.3.0, Input System 1.19.0, Cinemachine 3.1.7, AI Navigation 2.0.14, Test Framework 1.6.0); architecture docs and ADRs; content data; editor tooling; EditMode and PlayMode tests; `Main.unity`; CI on three platforms.

**Acceptance:** the project opens without compile errors and enters Play Mode. ✅

## Phase 1: Player, camera & world ✅ (🟡 feel)
Third-person character, Cinemachine camera, strategic camera and switching, terrain, vegetation, resource objects, interactions, HUD. Movement and camera feel need the human playtest.

## Phase 2: Inventory, crafting, construction ✅
Items, transactional inventory with reservations, containers, pickup and drop, crafting and stations, modular pieces with snapping and explained validation, construction UI, save and load.

## Phase 3: Industrial progression ✅
Furnace, workshop and pump with explicit states; fuel; electricity with priority shedding and batteries; the ore → ingot → electricity → gears → pump chain, including a fuel outage and recovery.

## Phase 4: Survival & threats ✅
Needs, day and night, Wanderer and Runner infected with perception, chase, attack and search; melee; death and recovery; walls and barricades that the infected batter; repair.

## Phase 5: Integrated playable prototype ✅ (🟡 playtest)
| Target | Status |
|---|---|
| One coherent region | ✅ 512 m valley |
| 4–6 useful POIs | ✅ 6 + start camp |
| 15–20 functional items | ✅ 37 |
| 8–12 recipes | ✅ 21 |
| 8–10 construction pieces | ✅ 19 |
| ≥2 crafting/production stations | ✅ workbench, campfire, furnace, workshop, pump |
| One industrial chain | ✅ |
| Electricity | ✅ |
| Basic threats | ✅ |
| Persistent save | ✅ |
| Working main menu | ✅ |
| Functional gameplay UI | ✅ (mouse use 🟡) |
| Automated tests | ✅ 168 EditMode + 7 PlayMode |
| Playable build | ✅ macOS (universal) and Windows x64, released as v0.1.1 |

**Acceptance:** the short survival-to-industry loop is completable without cheats. Verified by the headless acceptance loops and the tour; the human playtest is pending.

## Phase 6: Vertical slice (⏳ in progress)
| Target | Status |
|---|---|
| About 1 km² world, 8–12 POIs | 🔲 (512 m, 6 POIs) |
| 35–50 items, 20–30 recipes, 15–20 modules | ✅ 37 items, 21 recipes, 19 modules |
| 2–5 working colonists | ⏳ 2 recruitable |
| Four professions / job types | ⏳ haul, build, farm (plus eat, sleep, warm up); guard, medic, mechanic not started |
| Multiple infected archetypes | ⏳ Wanderer, Runner; armoured not started |
| Hostile human group | 🔲 |
| Dangerous wildlife | 🔲 |
| Local logistics (conveyors) | 🔲 (semi-automatic crate pull/push and colonist hauling exist) |
| Agriculture | ✅ |
| More complete electricity | ⏳ batteries, cables, priority shedding, broken components and repair, lightning rods; switches not started |
| Basic environmental events | ✅ thunderstorms with lightning, cold snaps with body warmth, rain (fires, floods and disease not started) |
| Meaningful strategic orders | ⏳ recipes, on/off, cables, work priorities; work zones and storage designation not started |

**Acceptance:** a coherent 60–90 minute playthrough from survival into industry and colony management.

## Phase 7: Expanded alpha (🔲)
Multiple regions and streaming sectors, factions and diplomacy, advanced automation, vehicles and roads, more weather and disasters, more technologies and construction, regional expansion, deeper NPC simulation.

## Phase 8: Beta & release (🔲)
Performance optimisation on the defined hardware targets, content completion, accessibility pass (subtitles, colour-blind review), balance, save compatibility, long-session soak tests, audio and animation polish, distribution builds, release documentation.

## Immediate backlog (ordered)
1. Human playtest of the acceptance scenario and of a full day-two-and-three session with weather ([#1](https://github.com/Ale-Newport/AfterFall/issues/1)); tune movement, harvest rates, day length, and weather and cold-snap balance.
2. Work zones and storage designation in the strategic view; the Guard profession.
3. Conveyors (automation level 3) between storage and machines.
4. More colonists and professions; the armoured infected; wildlife.
5. Spatial index for structure queries (the climate, colony planner and machine automation scan all structures); profile in-engine on the target hardware.
