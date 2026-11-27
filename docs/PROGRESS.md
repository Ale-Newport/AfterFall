# Progress log

_Last updated: 2026-11-27 (session 7: world expansion — creative and developer tools, map and minimap, doors and apartment blocks, vehicles, the 2 km Greater Millbrook world; character pipeline planned)._

## Snapshot
| | |
|---|---|
| Branch | `main` (every commit pushed to `origin`). Latest release: `v0.1.1` |
| Unity version | 6000.3.28f1 (arm64), installed through Unity Hub 3.22.6 with a Personal license |
| Last headless tests | 2026-11-27: **211/211 passed** (`dotnet test`, .NET 10.0.403, macOS 27, Apple M2 Max). GitHub Actions runs the same suite on Linux, Windows and macOS on each push. |
| Last Unity EditMode run | 2026-11-27: **211/211 passed** in 6000.3.28f1 (`tools/run-unity-checks.sh editmode`) |
| Last Unity PlayMode run | 2026-11-27: **7/7 passed** (5 smoke tests plus 2 Input System integration tests; the Input System package's 2 Windows mouse tests skip themselves on macOS). |
| Last player builds | 2026-11-27: macOS (universal), no errors. 2026-11-20: Windows x64 (Mono), attached to the v0.1.1 release with the macOS build |
| Last scripted tour of the build | 2026-11-27 (macOS, with every world-expansion scene and a new game in the 2 km region): **0 failed checks, 0 errors logged**, valley new game in 2.5 s, region in 7.6 s; region frames 8.3–8.4 ms average in every town. 2026-11-24: new game in 2.4 s, frames 8.3–8.6 ms average at the start camp, in the strategic view, in the rain and at night (vsync-capped, Apple M2 Max, 1600×900). 2026-11-20 (v0.1.1): Windows (GitHub-hosted `windows-latest`, WARP software rendering, 800×450, patience 25): **0 failed checks, 0 errors logged**, 46 minutes. |
| Last model gallery | 2026-11-09: **151 screenshots, 0 errors logged** (`tools/gallery.sh`, all sections), reviewed by eye. |
| Human playtest of the acceptance loop | **not yet** |
| Windows build | built (v0.1.1); the full scripted tour passes on a GitHub-hosted runner; **never run on a real Windows PC with a GPU** |

## Completed and verified headlessly
Verified means unit and integration tests pass under .NET, and the code compiles against the Unity reference assemblies.
- **Repository:** Unity-aware ignore rules, LFS attributes, local LFS, commit-msg hook blocking AI trailers, CI workflows.
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
- **Day and night:** morning, midday, afternoon and 22:00 lighting (the lamp lights the base at night).
- **Construction:** planning through the real placement path (snapping, physics probe, rules), then building by hand. The tour builds a bed, crates, a workbench, a furnace, a generator, a workshop, a lamp and planter beds.
- **Technology:** Metalworking, Electrification and Agriculture unlock from the activities that should trigger them.
- **Industry:** smelting in the furnace; machine windows; recipe selection.
- **Electricity:** the workshop auto-connects to the generator. It reports "No power" without fuel and with the generator switched off, then resumes. Grid summary and overlay labels match.
- **Colonist:** recruit Mara from the farmhouse. On her own she then:
  - leaves the house and stores workshop output;
  - hauls planks and nails to a blueprint and builds it;
  - plants and waters a bed from storage;
  - harvests potatoes into the crate.
- **Farming by hand:** planting and watering.
- **Saves:** save and reload, in both the PlayMode test and the build. Reloading keeps structures, recruited colonists, the planted crop, machine recipes, power links and battery charge.
- **Main menu screens:** New Game (difficulty and seed), Load Game, Settings (sliders, toggles, key bindings) and Controls.
- **Shelter:** a foundation with three walls, a doorway and a roof snapped onto it. Agents walk in through the doorway; a barricade in front of it seals it.
- **Defence:**
  - an infected drawn by noise stalls at the sealed shelter and batters a wall ("The infected are breaking through your Wooden Wall!");
  - repairing with a hammer restores the wall, and demolishing the barricade refunds wood;
  - a stone axe kills the infected in melee when it comes through the doorway.
- **Colony:**
  - Mara flees from an infected coming for her, eats from the crate when hungry, and sleeps in her bed at night;
  - Eli, recruited at Hartley's Workshop, walks 245 m across the valley to the settlement.
- **Water:** an electric pump on the lake shore, on its own generator, fills bottles and irrigates a planter bed 4 m away.
- **Grid:**
  - a power pole carries power 18 m to a lamp; cutting and reconnecting the cable isolates and restores it;
  - a battery bank charges from surplus;
  - at night with the generator off, the battery runs three workshops and sheds the lamp first ("1500 W available, 1560 W demanded"); everything is powered again when it is back on.
- **Death:** all carried items drop where the survivor fell, and the death screen shows the belongings beacon. After respawning, the beacon is visible from 113 m and the belongings are recovered.
- **Pause and overlay:** the pause menu pauses the simulation and resumes it on close; the F3 overlay shows FPS and per-system tick costs.

## Implemented, not yet exercised in Unity
The tour drives the game through its commands, not the mouse and keyboard, so these still need a person:
- **Input:** aiming and clicking to place, holding E to build or repair, holding X to demolish, swinging with the mouse, rebinding a key, dragging sliders, typing a seed.
- **UI by hand:** clicking through windows, tooltips on hover, work-priority buttons, the strategic view's selection and cable tools.
- **Audio:** that the procedural sounds play and are mixed well (captions were seen).
- **World up close:** the map boundary.
- **Tooling:** the Windows build; editor menus (the build ran Validate Content).

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
- **Strategic orders:** machine recipes, on/off, cables, colonist work priorities and gathering areas work. Storage designation is not done.
- **Colony:** Haul, Build, Farm, Gather, Eat, Rest and Warm up work. Guard, medic and mechanic professions, morale effects and more than two survivors are not done.
- **Accessibility:** rebinding, UI scale, sensitivity, FOV, invert Y, camera shake, captions and text-based status are done. A colour-blind review and a tactical pause toggle (the setting exists in difficulty presets but has no UI) are not.
- **Survival:** health, stamina, hunger, thirst, carry weight and body warmth are done. Fatigue, injuries and infection are not.

## Not started
Conveyors and pipes, disasters other than storms and cold snaps (fires, floods, disease), factions, wildlife, the armoured infected, firearms, vehicles, multiple regions and streaming, skinned character animation, recorded audio, localisation.

## World expansion (session 7, 2026-11-27)
Details, phases and limitations: WORLD_EXPANSION_PROGRESS.md.
- **Creative mode and developer tools** (F6 panel: item browser and spawner, vehicles, teleports, time, weather, infected, diagnostics; F7 free flight). `CreativeTests`.
- **World map and minimap** drawn from the layout, fog of war, waypoint and markers (saved). `MapTests`.
- **Doors, keys and the three-storey apartment block** (Millbrook; also Ashford and Eastgate in the region). `DoorTests`.
- **Vehicles**: sedan, pickup, quad bike; keys, hotwiring, fuel, repairs, trunks, persistence. `VehicleTests`.
- **Greater Millbrook** (world version 2, 2 × 2 km, the new default): river and reservoir with bridges, Ashford, Eastgate, Pinecrest, farms, lodge, research station, rail; terrain tiles, node-view streaming and a NavMesh window. `RegionTests`.
- **Characters**: researched and planned (CHARACTER_PIPELINE.md, ASSET_PROVENANCE.md); waiting for approval to download MPFB2 and the MakeHuman CC0 assets.
- Tests: 211 headless and in Unity EditMode; the expansion tour (`-afterfall-tour-expansion`) covers every feature above in the macOS build.

## Gathering areas (session 6, 2026-11-24)
- **Strategic order:** "Mark area…" in the strategic view, then a click on explored ground, places a gathering area (6–30 m, default 14 m; at most six). Toggles choose wood, stone, fiber and food; "Focus" and "Remove" act on it. A translucent disc and a world label show each area in the strategic view.
- **Gather work** (`JobType.Gather`, a fourth work priority in the colony panel): the planner gives each gathering colonist one node in an area that allows its kind, if its yield has storage space and any tool it needs is in storage. The colonist fetches the best tool (reserved, so it is never taken twice), harvests up to six times through the same `HarvestRules` as the player (1.3× slower, scaled by skill; tool wear; noise), then brings the load and the tool back to storage. Salvage stays the survivor's work. Each area reports what it found and what is missing.
- **Colonists now collect from a container's edge** (its footprint plus 1.8 m) instead of 1.8 m from its centre. In the tour a workshop boxed in by its neighbours could not be reached and Mara walked in place all night.
- **Tour:** a gathering scene marks an area over the trees nearest the settlement and puts a stone axe in the crate; Eli fetches it, chops a birch and brings 16 logs and the axe back. The first run failed two checks (the check counted the survivor's own axe; Mara stalled at the boxed-in workshop all night) and both were fixed. Screenshots `20b_gathering_area` and `20c_gatherer_at_work` were inspected.
- **Tests:** 6 `GatheringTests` (planning by kind, tool and gatherers; fetching the axe, chopping and bringing logs and the axe back with the axe never duplicated; bare-handed fiber and berries; zones and gather jobs survive saves; removing a zone cancels its work; explored-land and limit rules; kinds from yields). 174 EditMode tests in total.

## Weather and warmth (session 6, 2026-11-24)
- **Weather** (`Domain/Weather`): fronts of clear, overcast, rain and thunderstorm roll from a saved random stream (`WorldState.WeatherRng`), with the next front decided ahead. `WeatherReadout` derives cloud, rain, storm, cold-snap intensity and the outdoor temperature from the saved `WeatherState` and the clock. Storms are foretold about 1.5 game hours ahead; cold snaps (every two or three days, from the evening of day 2 at the earliest) three hours ahead.
- **Lightning:** during storms, every 12–40 s; it finds the settlement's exposed conductors with a chance of 10 % + 4 % each (at most 40 %). A strike takes half a structure's health and a second breaks it (never destroyed). Broken machines, generators, batteries, lamps and poles (≤ 30 % health) leave their grid until repaired, for any cause. A **Lightning Rod** (Electrification) takes every strike meant for structures within 18 m. Thunder is a noise for the infected; rain and storms shorten their sight (to 0.55× in a full storm) and hearing.
- **Body warmth:** a new vital follows the felt temperature (outdoor temperature, +10 °C under a roof or inside a valley building, + the nearest fire). Cold drains food and water faster, slows stamina and stops health regeneration; freezing costs 10 HP per game hour.
- **Campfire** (4 logs, 6 stones; Survival Basics): burns logs or charcoal (8× a furnace's burn time, faster in the rain in the open), warms within 5 m, lights the night, and is the cooking station for the new **Baked Potato** (+22 warmth). The start camp's decorative fire pit is now a real campfire, put out until the player lights it. Colonists refuel lit fires from storage and walk to a fire (or a bed under a roof) to warm up when cold.
- **Presentation:** weather-driven sky (cloud cover and drift, grey and darker colours, softer sun and shadows, thicker fog), world-anchored rain streaks around the camera, lightning bolts with a sky flash and a strike light, procedural rain, wind and thunder (delayed by distance), campfire crackle, flame animation and firelight. HUD warmth bar and weather in the clock line; storm and cold-snap warnings in the HUD and the strategic panel; a campfire panel (fuel, status, light/put out, cook).
- **Fixed (pre-existing):** the colony planner's 2 s beat and each colonist's next-assignment time were not saved, so a loaded game could plan jobs half a second apart from the unsaved game. Found by a new soak test with colonists tending a fire; that test fails on the old code.
- **Tests:** 12 `WeatherTests` (deterministic fronts with every storm foretold, cold-snap schedule and warnings, freezing and campfire recovery, campfire fuel conservation and cooking, faster burning in the rain, shelter under roofs and in houses, two-strike lightning and repair, lightning rods, thunder and perception, rain on planter beds, colonists warming up and refuelling, save and load with identical continuation) and the soak regression test. 168 EditMode tests in total.
- **Tour:** a weather scene after the power grid (every other scene holds a clear sky with `WeatherSystem.Hold`, so earlier screenshots stay comparable). Verified in the macOS build and inspected by eye: rain over the settlement waters the open bed; a first strike damages a lamp (40/80), a second breaks it (1/80, off the grid), a hammer repairs it; a lightning rod built beside it takes 11 strikes with none getting through within 18 m; a cold snap brings −2 °C, a campfire warms the survivor to 20 °C and bakes a potato; Mara still sleeps at night; save and reload keep everything. The first tour run showed faint rain, a one-frame-late flash and a truncated warmth label; all three were fixed before the final run.
- **Balance to check in a playtest:** storm frequency (storms grow out of cloud or rain: about one every day or two), lightning pressure, cold-snap depth (−18 °C at Survival severity), campfire fuel use (a log lasts about one game hour) and warmth loss rates.

## v0.1.1 (session 5, 2026-11-20)
- **Fixes:** saves with in-progress hauls survive content changes (#5); fuel hauls are planned beside ingredient hauls and counted per destination inventory (#6); each system's tick phase is saved (`WorldState.TickPhases`), so after a load farming, threats and regrowth no longer lose up to one tick and the game evolves exactly like the unsaved one; CodeQL warnings resolved (#4), with ArtPreview output byte-identical.
- **New tests:** `ProgressionTests` (reachability fixpoint on eight generated worlds; content references), `SoakTests` (two in-game days with a save and reload every quarter-day; byte-identical continuation after a load over ten minutes), regression tests for #5 and #6. Four of the new regression tests fail on the old code.
- **Windows:** Unity's Windows Build Support (Mono) module is installed. `tools/package-windows.sh` packages `Builds/Windows`; `.github/workflows/windows-player.yml` (manual) downloads the zip from a release or draft and runs the tour on `windows-latest`. The runner has no GPU, so Direct3D 11 falls back to WARP at about 1 fps; the first attempt failed only because the tour's real-time budgets ran out (the simulation advances at most 0.25 s per frame). `-afterfall-tour-patience <factor>` stretches them; the workflow uses 25. Two later runs exposed tour timing assumptions (the infected killed the survivor before the screenshot; workshops ran out of ingots before the power checks); the tour now captures as soon as the infected close in and tops workshops up before power checks. The fourth run passed: 0 failed checks, 0 errors, every system exercised. Screenshots match the macOS ones under Direct3D 11.
- **Dead player:** actions taken while dead returned an empty reason; they now explain it.
- **WebGL:** judged not realistically feasible as a straight build; the port plan is [#8](https://github.com/Ale-Newport/AfterFall/issues/8) (threaded texture, terrain and icon generation; `File.Replace` saves; URP budget; load time).
- **Repository:** no AI co-author trailers exist anywhere in history (all commits by the owner), so no history rewrite was done. No Dependabot pull requests were open. Rulesets and security settings unchanged and verified.

## Release and repository hardening (session 4, 2026-11-19)
- **Release:** `v0.1.0` on GitHub with the universal macOS build and its SHA-256 checksum (`tools/package-macos.sh`). The release workflow publishes notes from CHANGELOG.md when a `v*` tag is pushed.
- **CI:** `ci.yml` (tests on Linux, Windows and macOS; lint with `-warnaserror`, `.editorconfig` formatting, `.meta` check, actionlint, shellcheck; NuGet vulnerability audit), `codeql.yml` (C# and workflows), `security.yml` (gitleaks over the full history; dependency review on pull requests), Dependabot for actions and NuGet. Actions are pinned to commit SHAs.
- **Repository rules:** a ruleset on every branch requires a pull request with code-owner approval (stale approvals dismissed, last push approved by someone else) and blocks force pushes and deletions; `main` also requires the CI, CodeQL and security checks; `v*` tags can only be created or changed by the maintainer. The repository admin role (only the owner) bypasses all of them, so direct pushes keep working. Secret scanning with push protection, Dependabot alerts and security updates, private vulnerability reporting, approval before outside contributors' workflows run, read-only workflow tokens. The wiki is off (it was empty and editable by any GitHub user); Discussions are on.
- **Open-source files:** MIT LICENSE (Unity-template files stay under the Unity Companion License), CONTRIBUTING, CODE_OF_CONDUCT, SECURITY, CODEOWNERS, issue forms, pull request template, `.env.example`, `.editorconfig`. README has a demo GIF and screenshots from the tour in `docs/media` (plain Git, not LFS, to spare the LFS bandwidth quota).
- **Simulation fixes (code review, regression tests in `RegressionTests.cs`):** every random stream is saved (`WorldState.SessionRng`/`ZombieRng`/`ThreatRng`/`FarmRng`), as are infected attack, mode and idle timers; dead colonists free their bed; emptying a drop slot by slot removes it; watering with a full backpack no longer destroys the bottle; clearing a recipe mid-cycle no longer locks the machine; a raid whose announced origin became unfair is rerouted with a new warning or called off.
- **Open review findings, filed as issues:** saves with reserved hauls can become unloadable after a content change ([#5](https://github.com/Ale-Newport/AfterFall/issues/5)); fuel hauls wait for ingredient hauls ([#6](https://github.com/Ale-Newport/AfterFall/issues/6)). CodeQL reports no security alerts, and about 240 code-quality notes (mostly `missed-where`, `path-combine`); the useless assignments are [#4](https://github.com/Ale-Newport/AfterFall/issues/4).

## Visual overhaul (session 3, 2026-11-09)
The game used to look like a grey-box prototype: flat-coloured cubes and cones, voxel-like people, two-letter item codes and no post-processing. Everything is still generated in code. No assets were imported.

- **Rendering foundation:**
  - `Afterfall/Surface`, a URP PBR shader with triplanar procedural detail maps, so meshes need no UVs. It has vertex-colour tint and baked AO, wind sway and object-space mapping.
  - 42 `TextureSynth` surfaces (plus the terrain layers), generated on worker threads from launch.
  - `MeshBuilder` primitives with bevels.
  - The model gallery and ArtPreview review tools (docs/TESTING.md).
- **Item icons:** an illustrated icon for each of the 36 items, drawn by an anti-aliased vector rasteriser. They appear in:
  - slots;
  - the recipe list, the recipe detail and the craft queue;
  - machine recipes and fuel;
  - build and placement costs;
  - the HUD.
- **Resource nodes:**
  - four variants each of tiered pines and birches, with wind;
  - stumps with growth rings;
  - faceted rocks with moss;
  - ore seams;
  - berry bushes;
  - scrap heaps;
  - an old sedan, plus a stripped shell;
  - a 1970s fuel pump and a burnt transformer;
  - sacks, and a backpack for lost belongings.
- **Ground:**
  - 512 px terrain layers with normal maps;
  - instanced grass, flowers, ferns, reeds and pebbles around the camera;
  - a lake shader with depth tint, fresnel and waves.
- **Characters:** sculpted low-poly survivor, colonists and infected (faces, hair, clothes, backpack; gaunt infected with glowing eyes). Limbs bend at elbows and knees. The rig has swing, crouch, jump and death poses, and a model for every tool and weapon.
- **Structures and machines:**
  - plank walls with posts and shutters, a log foundation on stone footings, corrugated roofs;
  - a brick furnace with a glowing mouth, a diesel generator, a workshop with a lathe and drill press, a pressure-tank pump;
  - a battery rack, a street lamp, a raised planter with crops;
  - recognisable loot furniture.
- **Points of interest:**
  - sided houses with porches, chimneys and AC units;
  - a gambrel barn with a cupola, a corrugated garage and warehouse with roll-up doors;
  - a plaster clinic, a brick fuel-station shop under its canopy;
  - lattice pylons, a ribbed silo, a ridge tent with guy ropes, layered quarry cliffs.
- **Atmosphere:**
  - a sky shader with sun, moon, stars and clouds driven by one time-of-day palette, plus exponential fog matched to the horizon;
  - post-processing turned on in game (filmic grade, bloom, vignette, SMAA, NaN guard);
  - tuned shadows and SSAO.

Seven areas were built in parallel and merged without conflicts. They were verified with:
- EditMode 124/124 and PlayMode 7/7;
- the full tour, with 0 failures and 0 errors;
- a 151-shot gallery with 0 errors.

Fixed during the merge:
- **Zero normals on millimetre faces.** Unity's `Vector3.normalized` returns zero below a length of 1e-5. On tiny bevel corners this gave NaN lighting, which bloomed into white blobs. `MeshBuilder.Unit` replaces it.
- **Slow new games.** Texture generation now starts in the background at launch, which brought a new game from 3.3 s back down to 2.4 s.

## First runs in Unity (session 2)
Bugs that only showed up in the engine, all fixed:
- **Saves would always be rejected.** Unity's Mono prints some re-read doubles with 17 digits, so the load-side checksum never matched; 7 EditMode tests failed. The checksum now uses a runtime-independent canonical form.
- **Invisible, then unlit, terrain in builds.** Instanced terrain needs shader variants that player builds strip. Fixed by drawing the terrain non-instanced.
- **Glossy "ice" ground.** URP terrain takes smoothness from the layer texture's alpha.
- **Illegible HUD bar labels; an empty grey banner; an overflowing panel heading.**
- **A URP warning on every new game** (`Camera.layerCullSpherical`).

### Found by extending the tour to every system (all fixed)
- **Player-built doorways and foundations sealed the NavMesh.** Each structure carved one box the size of its footprint. Now each blocking part carves on its own.
- **A shelter could not be closed.** Walls meeting at a foundation corner overlapped in the placement probe. Wall pieces now meet at corners, and any wall piece on the same edge counts as a duplicate.
- **New recruits stayed where they were found.** With no work to do, Eli idled 245 m away. Idle colonists far from their bed now walk to the settlement.
- **Infected never breached anything in the game.** They took the end of a partial path as "arrived", then gave up and searched. Breaching worked only in the headless simulation.
- **Every death was handled twice.** Each death fired two death events, showed the message twice and counted as two deaths.
- **Unreadable inputs and labels.** Text fields were white, so typed text vanished, and movement keys read "Move Up/Down".
- **The tour autosaved into the player's save folder.** It now saves into its own folder and checks that the player's folder is untouched. The two tour autosaves already written there were moved to the Trash.

### Found by the settlement tour (all fixed)
- **Survivors indoors snapped onto the roof.** The NavMesh covered roofs and attics, so Mara stood on the farmhouse roof and could not be recruited. Roofs, ceilings and canopies are now not walkable.
- **Most buildings were sealed for agents.** With the default 0.5 m agent and 1.2 m doors, only 4 of 9 such doors connected inside to outside. The agent is now 0.35 m and doors are 1.6 m: all 12 POI doors connect.
- **Colonists walked forever toward unreachable targets.** They now give up after stalling 4 s, with "can't find a way there" in their recent problems.
- **Colonists burned building planks as furnace fuel.** Automatic refuelling now only uses items tagged `fuel`.
- **Raw state names in the UI** (`WaitingForInput`, "Idle: Idle"), recipe buttons overflowing the panel, and toasts covering the strategic panel.

## Known issues and risks (severity)
- **High — no human playtest yet.** The tour grants building materials and drives commands directly, so gathering pace, mouse-and-keyboard building, and the overall feel still need someone at the keyboard. Follow the manual script in docs/TESTING.md.
- **Medium — weather balance is untested by a person.** Storm frequency, lightning pressure, cold-snap depth and campfire fuel use are first guesses (see session 6).
- **Low.** Rain streaks ignore roofs: they are hidden while the survivor is sheltered in the third-person view, but still drawn around the strategic camera and through open-sided structures. There are no splashes or wet surfaces.
- **Low.** Saves made before session 6 have no campfire at the start camp (its decorative fire pit is gone); building one is cheap.
- **Low.** Repairing a generator costs a circuit board however small the damage (repair costs round each material up). Lightning now damages before it breaks, but a rarer-material rule may be needed.
- **Low.** Tree canopies can poke through structures built right next to them, and the camera can end up inside one.
- **Medium.** UI windows are still slightly see-through.
- **Medium — visuals judged on stills.** The gallery and tour take stills, so motion (wind sway, procedural animation, ground-cover pop-in) has not been seen by a person. Check them in the playtest.
- **Medium.** A new game takes 2.4 s instead of 1.1 s: procedural textures and terrain layers are generated on load. This is shorter when the player spends a few seconds in the menu first.
- **Low — draw calls.**
  - Characters use about 20 renderers each, and a POI house about 25 (one per material and part). The SRP batcher keeps this at 8.3 ms per frame, but crowds of infected may need merging.
  - Car wrecks, fuel pumps and transformers use 4 renderers, above the target of 3.
- **Low — close-up flaws.**
  - Rigid limb parts show seams at knees and elbows.
  - Terrain rock patches stretch on steep slopes (URP terrain has no triplanar mapping).
  - Grass can poke through decoration that has no collider, such as porches.
- **Low.** Low-sun shadows are deliberately dark (ACES). Raise the low-sun ambient if the playtest finds it too dark. The charcoal icon reads somewhat like dark stone.
- **Low.** At 1600×900 the HUD vitals panel overlaps the bottom-left corner of centred windows, and the strategic view's left panel reaches a few pixels into it.
- **Medium.** The fog of war is only partly opaque, and trees taller than its 9 m layer show through.
- **Medium.** The threat director treats distance as "out of sight" and does not check the camera frustum.
- **Medium.** Irrigation does not add electrical load; a pump irrigates whenever its grid has supply.
- **Low.** The colony planner, machine automation and the climate (shelter and heat for the survivor and each colonist) scan all structures (fine at prototype scale).
- **Low.** UI glyphs (·, —, …, ×) rely on the default font; untested.

## Metrics (headless)
- Simulation cost: **0.007 ms per 60 Hz frame** with 76 structures, 40 machines and 26 infected (Apple M2 Max, .NET 10), down from 0.030 ms before caching the structure order. Machines average 0.06 ms per 4 Hz tick.
- Inventory conservation errors in automated tests: **0** (randomised operations, save round trips, colonist hauls interrupted mid-carry).
- In-engine (macOS build, 1600×900, Apple M2 Max, after the visual overhaul): average frame 8.3–8.4 ms, vsync-capped at 120 Hz, at the start camp, in the strategic view and at the settlement at night; new game ready in 2.4 s. Generating the full surface catalogue takes 1.8 s across worker threads; it starts at launch.
- Not yet measured (needs playtests): time to first construction, time to first machine output, playtime without critical bugs.

## Next development tasks
0. **World expansion follow-ups**: realistic characters once the MPFB2/MakeHuman download is approved (CHARACTER_PIPELINE.md); sector streaming of props and structure views for worlds beyond 2 km; far-distance tree impostors; map detail tiles; road-aware vehicle routing; vehicle sounds and headlights.
1. **Playtest the acceptance scenario** ([#1](https://github.com/Ale-Newport/AfterFall/issues/1), docs/TESTING.md) by hand in the editor or the macOS build, including a session into days 2–3 for storms and a cold snap. Tune movement, camera, harvest and colonist pacing, day length, and weather balance. Record time to first construction and to first machine output.
2. Storage designation in the strategic view (which crates take what); the Guard profession.
3. Conveyors (automation level 3) between storage and machines.
4. Recorded audio where licensed assets are available; skinned characters to hide joint seams.
5. Play the Windows build on a real PC ([#2](https://github.com/Ale-Newport/AfterFall/issues/2)); a WebGL demo ([#8](https://github.com/Ale-Newport/AfterFall/issues/8)).
6. Visual follow-ups: power lines between poles and pylons ([#3](https://github.com/Ale-Newport/AfterFall/issues/3)), interior furnishing of POI buildings, a closable door leaf, construction-stage visuals for blueprints, a performance pass on renderer counts.

## How to run (current)
- Headless tests: `dotnet test tools/dotnet/AfterfallSim.Tests/AfterfallSim.Tests.csproj`
- Compile check: `python3 tools/unity-compile-check/compile_check.py --unity <Unity.app/Contents> --packages <PackageCache or extracted tarballs>`
- In Unity: `tools/run-unity-checks.sh` (EditMode, PlayMode, macOS build, scripted tour; results in `Logs/unity-checks/`).
- Editor: open the project in Unity Hub with 6000.3.28f1 → `Main.unity` opens automatically → Play → New Game. Do not run the script while the project is open in the editor.

## Session checkpoint notes
- Session 1 extracted the official installer (`Unity-6000.3.28f1.pkg`) and package tarballs into a temporary scratch directory for compile checks. Since session 2 the installed editor and `Library/PackageCache` can be used instead (docs/TESTING.md §2).
- The original GDD `.docx` was not found; docs/GDD.md is an adaptation of the master brief.
- `gh` is authenticated as the owner (scopes: repo, workflow, gist, read:org). Direct pushes to `main` succeed through the admin bypass of the rulesets.
