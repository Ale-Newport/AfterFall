# Testing

AFTERFALL is tested at three levels. Run the first two on every change; run the third whenever a licensed Unity Editor is available.

## 1. Headless simulation tests (.NET) — always available
The EditMode test sources in `Assets/Tests/EditMode` are pure NUnit. `tools/dotnet` compiles the simulation exactly as Unity does (netstandard2.1, C# 9) and runs those same files:

```bash
dotnet test tools/dotnet/AfterfallSim.Tests/AfterfallSim.Tests.csproj
```

Requirements: .NET SDK 10 (`brew install dotnet`). On macOS Homebrew installs you may need `export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec`. CI runs this on every push to `main` (`.github/workflows/simulation-tests.yml`).

| Suite | Covers |
|---|---|
| InventoryTests | stacking, capacity, weight, filters, durability and wear, atomic add/consume, move/merge/split/compact, transfers |
| ReservationTests | item and space reservations, single-use tokens, two-phase transfer tickets, a **3,000-step randomised conservation test** |
| ContentTests | shipped content validates; prototype scope; the industrial chain is reachable; deterministic loot |
| CraftingTests / TechnologyTests | missing ingredients, station requirement, tech locks with explanations, queue consume/deliver/refund, unlock cascade |
| SchedulerTests | per-system tick rates, hitch capping, the 48-minute day |
| ConstructionTests | placement reasons, blueprint delivery and work, cancel and demolish refunds, overflow to the ground, repair cost, destruction spills |
| MachineTests / PowerTests | every machine blocking reason, fuel pause and resume, output full, automation pull/push, missing power, out-of-fuel stop and refuel resume, load-proportional fuel use, priority shedding, batteries, topology rebuild, cable range and poles |
| SurvivalTests / ZombieBrainTests | needs drain, starvation, stamina, consumables, encumbrance; wander, investigate, chase, attack cooldown, search, perception (FOV, darkness, crouch, walls) |
| WorldGeneratorTests | determinism, seed variation, prototype content counts, terrain flattening, exploration |
| ZombieIntegrationTests / ThreatDirectorTests | detect, chase, damage and kill; stealth; fair spawning; announced raids |
| PersistenceTests | full round trip (items, ids, machines mid-cycle, generator burner, reservations, blueprints, durability, tech, exploration), production continues after load, stable re-save, tamper/truncation/newer-schema rejection, migrations, atomic writes and **backup recovery**, slot listing |
| BreachTests / ArchetypeTests | barricade stops, is battered, breaks, then the zombie reaches the player; crates are never targeted; Runner tuning and night spawn mix |
| ColonyTests / ColonySafetyTests | recruiting rules, furnace supply with **item conservation**, blueprint delivery and building, destination destroyed mid-haul (**no loss, no orphaned reservations**, failure recorded), no duplicate job ownership, eating, disabled job types, save/load mid-haul, infected hunting colonists, fleeing, death drops |
| FarmingTests / FarmJobTests | seeds unlock agriculture, growth only while watered, bottle returned, harvest, pump irrigation, crop save/load; a colonist plants, waters, harvests and replants from storage |
| AcceptanceLoopTests | **organic early game** (bare hands → tools → workbench → planks → foundation → drink → save/load → continue) and the **industrial chain** (workbench → metalworking → furnace smelting → electrification → generator + workshop → gears → water works → fuel outage → refuel → resume) |
| PerformanceTests | busy-settlement simulation budget (prints per-system timings) |

Last result: **119 passed, 0 failed** (see docs/PROGRESS.md for the date and environment).

## 2. Unity compile check — no Unity license required
`tools/unity-compile-check/compile_check.py` compiles every asmdef assembly (runtime, editor, EditMode and PlayMode tests) with the .NET SDK against **Unity's own reference assemblies** and the **source** of the pinned packages, using Unity-like scripting defines. It runs in two passes: `player` (no `UNITY_EDITOR`) and `editor`.

```bash
python3 tools/unity-compile-check/compile_check.py \
  --unity "/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents" \
  --packages "Library/PackageCache"
```

- `--unity` takes either an installed editor or the `Contents` folder extracted from the official installer. Download `https://download.unity3d.com/download_unity/8f082d2fe476/MacEditorInstallerArm64/Unity-6000.3.28f1.pkg` (5.2 GB), then `pkgutil --expand` it and extract `Payload` with `gunzip -c | cpio -id '*Contents/Resources/Scripting/*' '*Contents/Resources/PackageManager/BuiltInPackages/*'`.
- `--packages` takes either the project's `Library/PackageCache` (after Unity has opened the project once) or a folder of registry tarballs extracted as `<name>/package/...` (inputsystem 1.19.0, cinemachine 3.1.7, splines 2.8.2, mathematics 1.3.2, ai.navigation 2.0.14, nuget.newtonsoft-json 3.2.2 from `https://download.packages.unity.com/<name>/-/<name>-<version>.tgz`).
- Limitations: optional integrations (URP inside Cinemachine, uGUI inside the Input System) are compiled out. This checks types and APIs only; it does not run code, import assets or validate scenes and serialization.

It caught two real Unity 6.3 issues before Unity was ever opened: the new `UnityEngine.EntityId` type, and `VisualElement.Add` returning void.

## 3. Unity Test Runner (EditMode + PlayMode) — needs a licensed editor
In the editor, use **Window → General → Test Runner**. From the command line:

```bash
UNITY="/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity"
"$UNITY" -batchmode -projectPath . -runTests -testPlatform EditMode -testResults Logs/editmode.xml -logFile -
"$UNITY" -batchmode -projectPath . -runTests -testPlatform PlayMode -testResults Logs/playmode.xml -logFile -
```

PlayMode smoke tests (`Assets/Tests/PlayMode/GameSmokeTests.cs`) load `Main.unity`, start a new game through `GameBootstrap`, and use the Input System test fixture:
- `NewGame_BuildsCoherentWorld`: terrain, camera and more than 300 resource views exist; the player stands on the ground and does not fall through.
- `Keyboard_MovesPlayer`: holding W moves the character more than 2 m; the simulation receives the position.
- `Tab_SwitchesToStrategicCameraAndBack`
- `Harvesting_AddsItemsToInventory`
- `SaveAndLoad_RestoresPlayerAndInventory`

**These have never been executed** (no licensed editor was available). Treat them as the first thing to run.

## Manual playtest script (acceptance scenario)
1. Launch → **New Game** (Survival). You spawn at a camp beside a crate.
2. Move (WASD) and rotate the camera (mouse). Explore the forest edge and walk to Millbrook (north-east).
3. Search the crate (E). Gather fiber, branches and stones (E or LMB). Open the backpack (I) and inspect the items.
4. Craft rope ×2 and a stone axe. Equip it (right-click or 1–8). Chop trees.
5. Press B → Workbench → place it (LMB) → hold E to build. At the workbench: Saw Planks.
6. Press B → Foundation, then walls and a doorway. Check that the invalid-placement reasons make sense (slope, trees, no foundation).
7. Craft a stone pickaxe. Mine iron ore at the Old Quarry. You should see the "New technology: Metalworking" toast.
8. Build a furnace, set Smelt Iron Ore, load ore and wood, and collect ingots.
9. Loot the Ridge Substation lockers (dangerous). Getting the schematic plus 2 ingots unlocks Electrification.
10. Build a generator and a workshop within 6 m (they auto-connect). Fuel the generator with a fuel can (gas station pump) or charcoal. Machine gears.
11. Remove the generator's fuel: the workshop shows "No power: generator out of fuel". Refuel: it resumes.
12. Meet an infected: fight (LMB), crouch-sneak or run. Watch the vitals.
13. Tab → strategic view. Select the workshop, change the recipe, inspect the grid, run a cable to a lamp.
14. Esc → save. Quit to desktop. Relaunch → Continue. Check the world, inventories, structures and machines.

Record results, issues and timings (time to first construction, time to first machine output) in docs/PROGRESS.md.

## Known coverage gaps
- No in-engine verification yet: rendering, physics feel, NavMesh baking time, UI layout and scaling, and performance in builds.
- There are no UI automation tests; UI is covered only indirectly by the PlayMode smoke tests.
- The threat director's spawn validity uses distance as a proxy for "out of sight"; it does not check the camera frustum.
