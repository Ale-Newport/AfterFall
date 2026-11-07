# AFTERFALL: From Survival to Civilization

AFTERFALL is a single-player 3D open-world survival, crafting, base-building and industrial-automation game built with **Unity 6.3 LTS**. You wake up alone in a quiet, ruined valley. You scavenge, build shelter, smelt metal, bring electricity back, and automate production. The long-term goal is a settlement that grows into a connected industrial civilization.

> **Status: early playable prototype (Phases 0–5 in progress).** The simulation core is implemented and tested headlessly. The Unity presentation layer compiles against the official Unity 6000.3.28f1 reference assemblies. It **has not yet been run inside a licensed Unity Editor**, so in-engine behaviour (rendering, physics feel, UI layout) is unverified. See [docs/PROGRESS.md](docs/PROGRESS.md) for exactly what is verified.

There are no screenshots yet: none will be published until real in-engine captures exist.

## Design pillars

1. **Consequential survival.** Scarcity, preparation and danger. Death drops your gear where you fell.
2. **Transformative progression.** Knowledge unlocks new capabilities (furnaces, electricity, pumps), and you unlock it by doing things in the world, not by grinding XP.
3. **Interconnected simulation.** Inventories, machines, power networks, construction, threats and saves all run on one authoritative world state, whether or not anything is rendered.
4. **Accessible strategy.** A strategic camera lets you inspect and direct your settlement. It does not grant omniscience, and it does not teleport materials.

## What is in the prototype

| Area | Implemented |
|---|---|
| World | Deterministic 512 m × 512 m valley: Unity Terrain, lake, northern ridge, roads, forest (~1,000 harvestable trees), 6 points of interest (Millbrook settlement, Route 9 gas station, Hartley's Workshop, Ridge Substation, Old Quarry, Dawson Farm) plus a starting camp |
| Survivor | Third-person movement: walk, run, stamina-limited sprint, crouch, jump. Health, stamina, hunger, thirst, carry weight, death and respawn with recoverable belongings |
| Cameras | Cinemachine 3 shoulder camera with collision and zoom. Tab switches to a strategic view with pan, rotate and zoom |
| Items | 34 items in 8 categories, with stacking, weight limits, durability, filters and a reservation-based transactional inventory |
| Crafting | 20 data-driven recipes: hand crafting, the workbench station, and machine processing |
| Construction | 16 buildable structures. Ghost preview, snapping (foundations, walls, roofs), rotation, explained placement validation, blueprint → delivery → construction work, repair and demolition |
| Industry | Stone furnace (burns fuel), powered machine workshop, electric water pump. Explicit machine states with reasons. Semi-automatic pull/push from adjacent crates |
| Electricity | Generators (fuel cans or charcoal), power poles, lamps and battery banks. Graph networks, priority load shedding, fuel burned in proportion to load |
| Technology | Survival → Metalworking → Electrification → Water Works, unlocked by world activity |
| Threats | Wanderer infected: vision, hearing and occlusion; investigate, chase, attack and search states; NavMesh movement. A threat director with fair spawning, night pressure and announced attacks on noisy bases |
| Persistence | Versioned JSON saves with SHA-256 checksums, migrations, validation, atomic writes and automatic backup recovery. Quick save, autosave and save slots |
| UI | UI Toolkit HUD, backpack and crafting, containers, machine and generator panels, construction menu, strategic overview, main and pause menus, settings with key rebinding |

## Industrial progression (the prototype chain)

```
fiber → rope ┐
branches/stone ─┴→ stone axe → logs → workbench → planks → shelter
iron ore (quarry, pickaxe) + workbench ⇒ Metalworking → furnace → iron ingots
generator schematic (Ridge Substation) + 2 ingots ⇒ Electrification
generator (+ fuel cans / charcoal) ⇢ cables ⇢ machine workshop → gears → mechanical parts, pipes, motors
4 gears machined ⇒ Water Works → electric water pump → clean water
```

## Technology stack

- Unity **6000.3.28f1** (6.3 LTS), Universal Render Pipeline 17.3.0
- C# 9: pure .NET Standard 2.1 simulation assemblies plus a Unity presentation assembly
- Input System 1.19.0 (actions defined in code, rebindable), Cinemachine 3.1.7, AI Navigation 2.0.14, UI Toolkit, Newtonsoft JSON 3.2.2
- Unity Test Framework 1.6.0 (EditMode + PlayMode), NUnit
- Git + Git LFS (binary art and audio), GitHub Actions for the headless test suite

## Requirements

- Unity Hub with **Unity 6000.3.28f1** (another 6000.3.x patch should also work) and the macOS or Windows build support module
- macOS (Apple silicon or Intel) or Windows 10/11, keyboard and mouse
- Optional, for headless verification: .NET SDK 10, Python 3

## Getting started

```bash
git clone https://github.com/Ale-Newport/AfterFall.git
```

```bash
cd AfterFall && ./tools/setup-dev.sh
```

`setup-dev.sh` enables Git LFS for this repository only and installs the commit-msg hook.

1. Open the folder in Unity Hub (**Add project from disk**) using Unity 6000.3.28f1.
2. On first import, the editor opens `Assets/_Afterfall/Scenes/Main.unity` and validates the content files.
3. Press **Play**, or use **AFTERFALL → Play Main Scene** (⌘⇧P / Ctrl+Shift+P).
4. Choose **New Game**, pick a difficulty and start.

To build a standalone player, use **AFTERFALL → Build → macOS / Windows (64-bit)**, or run:

```bash
"/Applications/Unity/Hub/Editor/6000.3.28f1/Unity.app/Contents/MacOS/Unity" -batchmode -quit -projectPath . -executeMethod Afterfall.EditorTools.BuildCommands.BuildMacOS -logFile -
```

## Controls

| Action | Default |
|---|---|
| Move / sprint / crouch / jump | WASD / Shift / C or Ctrl / Space |
| Look / zoom | Mouse / wheel |
| Interact (hold to build) | E |
| Attack or harvest | Left mouse button |
| Hotbar / drop held item | 1–8 / Q |
| Backpack and crafting | I |
| Construction menu / rotate blueprint / cancel | B / R or wheel / right mouse button |
| Strategic view | Tab (WASD pan, Q/E rotate, wheel zoom, left-click select) |
| Quick save / quick load | F5 / F9 |
| Performance overlay / help / menu | F3 / F1 / Esc |

Every binding can be changed in **Settings**.

## Testing

```bash
dotnet test tools/dotnet/AfterfallSim.Tests/AfterfallSim.Tests.csproj
```

This runs the EditMode test suite (100 tests) headlessly: inventory conservation, reservations, crafting, construction, machines, power, AI, threats, world generation, saves and full gameplay-loop scenarios. The same test files run inside Unity's Test Runner, where PlayMode smoke tests also run. See [docs/TESTING.md](docs/TESTING.md), including how to compile-check the Unity code without a licensed editor.

## Documentation

- [docs/GDD.md](docs/GDD.md): implementation-oriented game design
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): modules, dependency rules, simulation flow
- [docs/DEVELOPMENT_ROADMAP.md](docs/DEVELOPMENT_ROADMAP.md): phases and acceptance criteria
- [docs/PROGRESS.md](docs/PROGRESS.md): what is done, verified, partial or missing
- [docs/TESTING.md](docs/TESTING.md) and [docs/CONTENT_PIPELINE.md](docs/CONTENT_PIPELINE.md)
- [docs/decisions/](docs/decisions/): architecture decision records
- [docs/ASSET_LICENSES.md](docs/ASSET_LICENSES.md) and [CHANGELOG.md](CHANGELOG.md)

## License

All rights reserved by the repository owner unless a license file states otherwise. The project settings and URP pipeline assets derived from Unity's 3D (URP) template, and the URP-derived material assets, are used under the Unity Companion License. See [docs/ASSET_LICENSES.md](docs/ASSET_LICENSES.md).
