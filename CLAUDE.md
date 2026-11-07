# CLAUDE.md: AFTERFALL project instructions

## Purpose
AFTERFALL is a Unity 6.3 LTS single-player 3D survival → industry → settlement game. Build it as a real, playable, maintainable game, not as a demo. The design source of truth is `docs/GDD.md` (adapted from the master brief; the original `AFTERFALL_Game_Design_Document_v1.docx` was never added to the repository). Current state and next steps are in `docs/PROGRESS.md`. Read it first in every session, and update it before you stop.

## Architecture (see docs/ARCHITECTURE.md)
```
Input → Presentation (MonoBehaviours, UI) → GameSession commands (Afterfall.Services)
      → Domain systems (Afterfall.Domain, pure C#) → WorldState → typed events → Presentation
Persistence (Afterfall.Persistence) reads and writes WorldState through DTOs.
```
- `Assets/_Afterfall/Scripts/Core`: ids, RNG, clock, tick scheduler, event bus (no UnityEngine).
- `.../Domain`: items/inventory, crafting, technology, construction, industry, power, survival, AI, threats, world generation (no UnityEngine).
- `.../Persistence`: save DTOs, mapper, serializer, file store, JSON content loader (Newtonsoft; no UnityEngine).
- `.../Services`: `GameSession` (application layer), `NewGameFactory`, `SaveService`.
- `.../Presentation`: Unity-side views, player, cameras, AI adapter, building, strategic view, UI Toolkit.
- `.../Editor`: editor tooling and command-line builds.
- Content data: `Assets/_Afterfall/Resources/Content/*.json`.

Rules:
- The domain never references UnityEngine. Its asmdefs set `noEngineReferences: true`, and the same sources compile for `netstandard2.1` / C# 9 in `tools/dotnet`.
- All state that must survive a save lives in `WorldState` and is mapped explicitly in `WorldStateMapper`. GameObjects only visualise state. The physical player and NavMesh zombie positions are the only values written back.
- Item movement goes through `Inventory` / `InventoryTransfers` / `ReservedTransfers`. Never add or remove items "visually".
- Stable `EntityId`s for every persistent entity; relationships are stored by id.
- In Presentation files, alias `using EntityId = Afterfall.Core.EntityId;`. Unity 6.3 has its own `UnityEngine.EntityId`.
- In UI code, use the `Put<T>()` extension when you need the added element back. `VisualElement.Add` returns void.
- Navigation: keep doorways at least 1.6 m wide and mark roofs and ceilings with `Shapes.NotWalkable` (see ARCHITECTURE.md → Navigation).

## Coding standards
- C# 9 maximum: no file-scoped namespaces, records, `init`, or global usings. 4-space indentation, braces on new lines, `_camelCase` private fields.
- Domain operations return `OpResult` with a player-facing reason; machines and placement must always explain why they are not working.
- Deterministic simulation: use `DeterministicRandom`, never `System.Random` or `UnityEngine.Random`, for anything saved or simulated.
- Keep comments at the level of the surrounding code: XML summaries on public types, a short "why" where needed.

## Commands
- Headless tests (EditMode suite on .NET): `dotnet test tools/dotnet/AfterfallSim.Tests/AfterfallSim.Tests.csproj`
- Compile-check the Unity assemblies without a license: `python3 tools/unity-compile-check/compile_check.py --unity <Unity.app/Contents> --packages <extracted package sources>` (see docs/TESTING.md)
- Missing `.meta` files: `python3 tools/generate_meta.py` (`--check` to verify)
- In-Unity checks (tests, macOS build, scripted tour with screenshots): `tools/run-unity-checks.sh [editmode|playmode|build|tour]`
- Unity tests directly: `<Unity> -batchmode -projectPath . -runTests -testPlatform EditMode|PlayMode -testResults <file>`
- Builds: `-executeMethod Afterfall.EditorTools.BuildCommands.BuildMacOS` / `BuildWindows`
- One-time repository setup: `./tools/setup-dev.sh` (local Git LFS plus the commit-msg hook)

## Commit rules (non-negotiable)
- Commit and push completed, verified increments directly to `main` (no PRs for normal work). Never force-push or rewrite published history.
- Use the configured Git identity only. **Never add AI attribution**: no `Co-Authored-By: Claude…`, no "Generated with Claude Code", no `Claude-Session:` trailer, no AI credit in docs. `.claude/settings.json` disables attribution, and `tools/git-hooks/commit-msg` rejects such trailers.
- Conventional messages: `feat(scope): …`, `fix(scope): …`, `test(scope): …`, `docs: …`, `chore: …`.
- After each commit, check `git log -1 --format=%B` for trailers before pushing.
- Never commit secrets, `Library/`, `Temp/`, `Logs/`, `UserSettings/`, builds or `tools/**/bin|obj`.

## Current milestone
Phase 5 (integrated playable prototype). Unity 6000.3.28f1 is installed and licensed on the development Mac. EditMode (122) and PlayMode (5) tests pass in Unity. A macOS build passes the scripted tour, which builds a powered settlement with a recruited colonist and crops. No human playtest has happened yet. Next priorities are in `docs/PROGRESS.md` → "Next development tasks". After any presentation change, run `tools/run-unity-checks.sh` and look at the tour screenshots before claiming it works.

## Constraints
- Do not claim Unity-tested or visually verified behaviour unless it was actually run.
- Prefer finishing and verifying systems over adding new partial ones.
- Strategic mode must not grant omniscience or teleport materials or construction.
- Fair threats: warnings before groups arrive; no spawns inside or next to a base, or in the player's view.
- Record asset sources and licenses in `docs/ASSET_LICENSES.md`. Never add assets with unclear licensing.
