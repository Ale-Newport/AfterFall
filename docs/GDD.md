# AFTERFALL: Game Design (implementation-oriented)

> **Source note.** The full design document `AFTERFALL_Game_Design_Document_v1.docx` (25 sections) was **not present** in the repository or on this machine when development started (searched 2026-11-07). This document adapts the master development brief into a concrete, implementation-facing design. When the original GDD is added, reconcile it here and record deviations in `docs/decisions/`.

## 1. Vision
A single-player 3D open-world game in which a lone survivor rebuilds technology and community after a collapse: **survival → settlement → metalworking → electrification → automation → organised colony → regional civilization**. The player keeps a physical character throughout, and gains strategic tools without losing the grounded survival experience.

Inspirations are used only as genre references (creative construction, dangerous persistent-world survival, production-chain automation, colony management). No protected content is reused.

## 2. Pillars → design rules
| Pillar | Rule in this codebase |
|---|---|
| Consequential survival | Resources are finite or slow to regrow. Death drops carried items in the world. Night and noise raise danger. |
| Transformative progression | Techs unlock new *capabilities* (smelting, power, pumps), not stat bumps. Unlocks come from world activity (`UnlockTrigger`). |
| Interconnected simulation | Inventories, machines, power, construction, AI and saves share one `WorldState`. Machines work while unrendered. |
| Accessible strategy | The strategic camera inspects, selects and configures. It has fog of war, threats are visible only when seen, and it gives no remote building or teleporting. |

## 3. Perspectives & controls
- **Third person:** see README → Controls. Movement feel tuning lives in `PlayerCharacter` (walk 2.4, run 4.6, sprint 7.2, crouch 1.7 m/s; jump 1.05 m).
- **Strategic view (Tab):** elevated angled camera (58° pitch, 18–160 m zoom). Select structures, set machine recipes, switch machines and lights, run and cut cables, plan blueprints. Execution remains physical: the survivor must deliver materials and build.

## 4. World
- **Prototype region:** 512 m × 512 m (`WorldLayout.WorldSize`), with a heightfield of 513² samples and a maximum height of 90 m. It has a lake (south-west), a northern ridge, foothills at the edges, a paved main road and three dirt roads.
- **Points of interest** (designed positions; terrain detail, vegetation and loot vary by seed):
  | POI | Risk | Purpose |
  |---|---|---|
  | Survivor's Clearing (start) | Low | supply crate, fiber, branches, stones |
  | Millbrook (4 houses, clinic) | Medium | food, water, cloth, medicine, tools |
  | Route 9 Gas Station | Low | fuel pumps (generator fuel), store food, scrap |
  | Hartley's Workshop | Medium | mechanical parts, gears, copper wire, ingots |
  | Ridge Substation | High | copper wire, batteries, circuit boards, **generator schematic** |
  | Old Quarry | Medium | iron ore seams (pickaxe), stone, mining cache |
  | Dawson Farm | Low | fiber fields, berries, rope, cloth |
- **Vegetation and resources:** about 1,000 trees (pine and birch, axe required), branch piles, rocks, iron ore, fiber plants, berry bushes, scrap heaps, car wrecks, a burnt transformer and fuel pumps.
- **Vertical slice target:** about 1 km², 8–12 POIs, streaming sectors of about 128 m (not started).

## 5. Survival model
Health, stamina, hunger, thirst and carry weight are implemented. Fatigue, temperature, injuries, infection and clothing are deferred.
- The day length is 48 real minutes (configurable).
- Hunger empties in 2.2 game days and thirst in 1.4 days at drain ×1. Sprinting costs 1.6× and walking 1.1×.
- At 0 hunger you lose 8 HP per game hour; at 0 thirst, 12 HP per game hour. Health regenerates 4 HP per game hour while hunger and thirst are both ≥ 50.
- Sprinting costs 13 stamina per second. Stamina regenerates at 20 per second after a 0.9 s delay (halved while hungry or thirsty).
- Carry weight: comfortable up to 35 kg, then slower, with a hard cap of 55 kg.
- **Death (standard):** carried items and refunded crafting inputs drop as "Your belongings" (blue beacon). You respawn at your bedroll or at the start camp. **Hardcore:** permadeath.
- **Difficulty presets:** Exploration, Survival, Hardcore. Each sets enemy density, loot abundance, survival drain, enemy damage, weather severity (reserved), tactical pause and permadeath independently (`GameSettings`).

## 6. Items & economy
There are 8 categories: Natural, Salvaged, Processed, Food/Medicine, Tool, Construction, Mechanical, Electrical. Each item has a stable id, name, description, stack size, weight, volume, optional durability, tags, icon code and colour, plus consumable, tool, burn-time or energy data. Economic correctness is a hard requirement (see ARCHITECTURE → Economic invariants).

## 7. Crafting & technology
- Recipes are data: inputs, outputs, station ("" for hand), required tech, duration, category.
- There are three ways to craft: hand crafting (anywhere), workbench station crafting (within 5 m of the station), and machine processing (furnace, workshop, water pump).
- Technology tree (prototype):
  | Tech | Era | Unlocked by | Grants |
  |---|---|---|---|
  | Survival Basics | I | start | tools, shelter pieces, crate, workbench, bedroll, barricade |
  | Metalworking | III | build a workbench + acquire iron ore | furnace, iron tools |
  | Electrification | IV | acquire the generator schematic + smelt 2 ingots | generator, poles, lamps, battery bank, machine workshop |
  | Water Works | V | machine 4 gears | electric water pump |
- Unlocking grants knowledge only. Every material cost still applies.

## 8. Construction
Flow: **Blueprint → materials delivered → construction work → complete**. You can place a blueprint without having the materials. Delivering and working happen by holding E, and a hammer doubles build speed. Snapping: foundations snap edge to edge on a 3 m module, walls snap to foundation edges, and roofs snap above walls. Placement explains every failure: technology, distance, water, support, slope, obstruction, outside the map, a nearby water requirement, or a duplicate. Demolition refunds 50% and spills any contents. Repair costs a share of the build cost proportional to the damage, and needs a hammer.

## 9. Industry
Machines have explicit states: Idle, Working, WaitingForInput, OutputFull, MissingPower, MissingFuel, Damaged, RecipeNotConfigured, Disabled, Unbuilt. Each comes with a human-readable reason. Inputs are consumed at cycle start and outputs are delivered at cycle end. A machine never starts if its outputs cannot fit. Fuel burns only while the machine is working.

Automation levels:
1. **Manual:** the player carries materials (done).
2. **Semi-automatic:** furnace, workshop and pump pull inputs and fuel from player storage crates within 3.5 m and push outputs into them (done).
3. Conveyors (planned).
4. NPC logistics (planned; `ReservedTransfers` exists for it).
5. Regional routes (planned).

## 10. Electricity
Structures with power components form networks: union-find over cables, rebuilt only when the topology changes. Poles relay to everything within 14 m; other parts link to the nearest node within 5–6 m, and cables can also be run manually in the strategic view. Each tick, consumers are sorted by priority (1 critical … 5 optional) and either fully powered or shed with a stated reason. Batteries absorb surplus and cover deficits. Generators burn fuel in proportion to delivered energy (fuel can 600 kJ, charcoal 150 kJ). Lamps draw power only at night.

## 11. Threats
- **Runner** (night 25%, day 8% of spawns; leads announced groups): chase 5.0 m/s (faster than running, slower than sprinting), 35 HP, sight 26 m in a 150° cone, hearing ×1.3, 9 damage, 1.1 s attack cooldown.
- **Breaching:** infected that stop making progress for 2.5 s next to a player-built barrier (wall, doorway, window wall, barricade) batter it for 1.5× their attack damage. This makes noise (14 m), and the player is warned. Crates and machines are never targeted.
- **Prey:** the infected chase the nearest visible living target: the survivor or a recruited colonist.
- **Wanderer:** sight 22 m in a 130° cone (darkness ×0.45–1, crouching ×0.6, sprinting ×1.15), proximity sense 2.5 m (never through walls), hearing of noise events (footsteps 4.5 m, sprinting 13 m, combat 9 m, harvesting 2–10 m). Chase speed 3.1 m/s (slower than running), 12 damage, 1.6 s attack cooldown. It loses the target after 5 s out of sight, then searches the last known position for 12 s.
- **Threat director:** the population target is 7 by day and 14 by night, multiplied by density, plus heat / 12. Spawns happen 50–150 m from the player and never within 30 m of any player structure. Heat comes from running generators and machines and from lit lamps at night. At heat ≥ 35 at night, a group is announced with a compass direction 30 s before it arrives (15-minute cooldown). A dusk warning is issued every day.
- Runner, armoured infected, animals and human factions are planned.

## 11b. Colony (Phase 6 foundation)
- Two survivors (Mara Voss at Dawson Farm, a hauler; Eli Brandt at Hartley's Workshop, a builder) can be recruited by talking to them when you have a free bedroll. The bedroll becomes their home.
- **Planner (every 2 s):** creates jobs from base state: deliver missing blueprint materials, build fully supplied blueprints, keep machine inputs at three cycles' worth, keep fuel stocked, carry finished products to storage, refuel generators.
- **Assignment:** urgency bucket (P0–P4) → personal work priority per job type (0 = never, 1–4) → skill-weighted distance. Each job has exactly one owner. Personal needs come first: put away carried items, eat or drink from storage below 35%, sleep in their bed at night.
- **Execution is physical:** walk, pick up, carry (6 slots, 30 kg), deliver, build at the colonist's skill multiplier. Every haul holds a source reservation and a destination reservation (capacity, or a blueprint quota) from claim to delivery. A failure (target destroyed, items gone, fled) releases them, leaves the items with the colonist (later put away), and records the reason for the strategic UI.
- **Safety:** colonists flee infected that are hunting within 9 m (their job fails cleanly), can be wounded and killed, and drop their pack where they fall.

## 11c. Agriculture
- Seed potatoes (start cache, barn, kitchens) unlock **Agriculture** (era II): planter beds (4 planks + 6 fiber).
- A plot grows in 15 minutes of *watered* time; soil water drains at 0.08 per second. Water with a water bottle (the empty bottle is returned) or irrigate automatically within 10 m of an enabled electric water pump on a powered grid.
- A harvest gives 3–5 potatoes (+16 food each) and 1–2 seeds back.

## 11d. Audio
All sound is synthesised at runtime: footsteps by distance travelled, harvest impacts by material, combat, infected groans and lunges, machine and generator loops tied to their state, wind and night ambience, UI and warning cues. **Captions** (on by default) describe important sounds with a direction ("[Infected groaning to your left]").

## 12. Strategic UI
Shows the stockpile, electricity per grid, threat (heat, recently seen infected, announced groups), known locations, the selected structure's status, health and power with actions, world labels for machines, blueprints, POIs and seen infected, and fog of war over unexplored 16 m cells.

## 13. Persistence
Everything listed in the brief's §22 that exists in the prototype is saved: seed and settings, time, player, inventories (with reservations and durability), structures (blueprint progress, machine progress and fuel, generator burner, batteries, power state), cables, resource depletion, drops, zombies, tech progress, exploration, threat state and metrics. The terrain and props are regenerated from the seed.

## 14. Out of scope for the prototype (tracked in the roadmap)
Further NPC professions (farmer, guard, medic, mechanic), work zones, conveyors and pipes, weather and disasters, factions, animals, firearms, vehicles, multiple regions and streaming, authored animation, models and recorded audio, localisation.
