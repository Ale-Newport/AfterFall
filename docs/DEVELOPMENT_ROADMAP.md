# Development roadmap

Status legend: ✅ done & verified headlessly · 🟡 implemented, awaiting in-Unity verification · 🔲 not started · ⏳ partial

> "Verified headlessly" means compiled and tested under .NET with the EditMode suite, and compile-checked against Unity 6000.3.28f1 reference assemblies. Nothing has been verified by running in a licensed Unity Editor yet. Every phase therefore stays **open** until its acceptance check is performed in Unity.

## Phase 0: Preproduction & environment
| Deliverable | Status |
|---|---|
| Repository inspection, Git LFS (local), ignore rules, commit-msg hook, no-attribution settings | ✅ |
| Unity 6000.3.28f1 pinned; package versions pinned (URP 17.3.0, Input System 1.19.0, Cinemachine 3.1.7, AI Navigation 2.0.14, Test Framework 1.6.0) | ✅ |
| URP pipeline assets and ProjectSettings (from the official URP template) | 🟡 |
| Architecture docs, ADRs, content data foundations | ✅ |
| Editor tooling (scene bootstrap, validation, benchmark, builds) | 🟡 |
| Test environment: EditMode suite (.NET + Unity), PlayMode smoke tests | ✅ (.NET) / 🟡 (Unity) |
| First runnable scene (`Main.unity`) | 🟡 |

**Acceptance:** the project opens without compile errors and can enter Play Mode. *Compile errors: none against the reference assemblies. Play Mode: not yet run.*

## Phase 1: Player, camera & world
Third-person character, responsive movement, Cinemachine third-person camera, strategic camera and switching, terrain, vegetation, resource objects, interactions, initial HUD. **Status: 🟡 all implemented.**
**Acceptance:** move through the world, interact with resources, switch camera modes. *(Covered by PlayMode tests `Keyboard_MovesPlayer`, `Tab_SwitchesToStrategicCameraAndBack` and `Harvesting_AddsItemsToInventory`; not yet run.)*

## Phase 2: Inventory, crafting, construction
Item definitions ✅, central inventory with reservations ✅, containers ✅, pickup/drop ✅, crafting and stations ✅, modular pieces ✅, ghost placement 🟡, placement validation ✅ (rules) / 🟡 (physics probe), construction UI 🟡, save/load ✅.
**Acceptance:** gather → craft tools → build shelter → quit → reload → state restored. *(Headless: `AcceptanceLoopTests.EarlyGame_FromBareHandsToShelter_Organic` passes. In Unity: PlayMode `SaveAndLoad_RestoresPlayerAndInventory`, not yet run.)*

## Phase 3: Industrial progression
Processing machines ✅, configurable recipes ✅, production cycles ✅, fuel ✅, machine inventories ✅, electricity ✅, power distribution ✅, industrial status UI 🟡, complete chain (ore → ingot → gear → pump) ✅.
**Acceptance:** process resources in powered machinery and diagnose shortages. *(Headless: `AcceptanceLoopTests.IndustrialChain_SmeltPowerMachineAndRecover` passes.)*

## Phase 4: Survival & threats
Survival needs ✅, day/night effects ✅ (perception, threat) / 🟡 (lighting), Wanderer zombie ✅, detection, chase and attack ✅, player melee ✅, damage, death and recovery ✅, defensive construction ✅ (walls and barricades block movement; stalled infected batter them; repair with a hammer).
**Acceptance:** survive, fight or escape, recover resources, defend a small base. *(Headless: zombie integration and threat director tests pass.)*

## Phase 5: Integrated playable prototype (current)
| Target | Status |
|---|---|
| One coherent region | 🟡 512 m valley |
| 4–6 useful POIs | 🟡 6 + start camp |
| 15–20 functional items | ✅ 34 |
| 8–12 recipes | ✅ 20 |
| 8–10 construction pieces | ✅ 16 |
| ≥2 crafting/production stations | ✅ workbench, furnace, workshop, pump |
| One industrial chain | ✅ |
| Electricity | ✅ |
| Basic threats | ✅ |
| Persistent save | ✅ |
| Working main menu | 🟡 |
| Functional gameplay UI | 🟡 |
| Automated tests | ✅ 119 EditMode (.NET) + 5 PlayMode (unrun) |
| Playable build | 🔲 needs a licensed Unity Editor |

**Acceptance:** the short survival-to-industry loop is completable without cheats. *(Headless loop proven; in-engine playthrough pending.)*

## Phase 6: Vertical slice (⏳ groundwork started)
Done headlessly: recruitable colonists with haul and build jobs (✅), second infected archetype (Runner ✅), agriculture (✅), barrier breaching (✅), procedural audio (🟡). Remaining: more colonists and professions (farmer, guard), work zones and storage designation, conveyors, hostile human group, wildlife, environmental events, larger map.

About 1 km² world with 8–12 POIs, 35–50 items, 20–30 recipes, 15–20 modules, 2–5 colonists with 4 professions and a job scheduler, multiple infected archetypes (runner, armoured), a hostile human group, wildlife, conveyors and logistics, agriculture, more complete electricity (switches, damage), environmental events (storm, cold), meaningful strategic orders (work zones, priorities, storage designation). **Acceptance:** a polished 60–90 minute playthrough.

## Phase 7: Expanded alpha (🔲)
Multiple regions and streaming sectors, factions and diplomacy, advanced automation, vehicles and roads, weather, more technologies and construction, regional expansion, deeper NPC simulation.

## Phase 8: Beta & release (🔲)
Performance optimisation on the defined hardware targets, content completion, accessibility pass (subtitles, colour-blind review), balance, save compatibility, long-session soak tests, audio and animation polish, distribution builds, release documentation.

## Immediate backlog (ordered)
1. Open in Unity 6000.3.28f1; fix any import, runtime or visual issues; run EditMode and PlayMode tests; capture the first real screenshots.
2. Playtest the acceptance scenario end to end; tune movement feel, harvest rates and the day-length balance.
3. Tune colonist pacing and job planning in engine; add work zones, storage designation and more professions (farmer: water and harvest plots; guard: engage infected).
4. Replace procedural audio with recorded sounds where licensed assets are available (keep the captions).
5. Spatial index for structure queries; profile in-engine on the target hardware.
