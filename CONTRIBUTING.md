# Contributing to AFTERFALL

Thanks for your interest. AFTERFALL is a single-player survival → industry → settlement game built with Unity 6.3 LTS and a pure C# simulation. Bug reports, playtest notes, tests, tools, art code and gameplay systems are all welcome.

## Ways to help

- **Playtest** a [release build](https://github.com/Ale-Newport/AfterFall/releases/latest) and report what felt wrong, slow or confusing. Nobody has played the full loop by hand yet, so this is the most valuable contribution right now.
- Pick an issue labelled [`good first issue`](https://github.com/Ale-Newport/AfterFall/labels/good%20first%20issue) or [`help wanted`](https://github.com/Ale-Newport/AfterFall/labels/help%20wanted).
- Ask questions and share ideas in [Discussions](https://github.com/Ale-Newport/AfterFall/discussions).

## How changes get in

Only the maintainer ([@Ale-Newport](https://github.com/Ale-Newport)) can push to this repository. Everyone else contributes through a fork:

1. Fork the repository and create a branch (`fix/inventory-weight`, `feat/weather`).
2. Make the change with tests, and keep each pull request focused on one thing.
3. Open a pull request against `main` and fill in the template.
4. CI must pass, and the maintainer must approve as code owner. New commits after an approval dismiss it, so the final state is always reviewed.

Workflows on pull requests from new contributors start after the maintainer approves them to run.

## Setup

You need Git with [Git LFS](https://git-lfs.com) (binary art is stored in LFS), the [.NET 10 SDK](https://dotnet.microsoft.com/download) for the headless tests, and optionally Unity 6000.3.28f1 for the game itself.

```bash
git clone https://github.com/<you>/AfterFall.git
```

```bash
cd AfterFall && ./tools/setup-dev.sh
```

```bash
dotnet test tools/dotnet/AfterfallSim.Tests/AfterfallSim.Tests.csproj
```

`setup-dev.sh` enables Git LFS for the repository and installs the commit-msg hook. The test suite runs the 124 EditMode tests without Unity. With Unity installed and licensed, `tools/run-unity-checks.sh` adds the PlayMode tests, a macOS build and a scripted tour of the game (see [docs/TESTING.md](docs/TESTING.md)).

## Architecture rules

Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) before changing code. In short:

- `Core`, `Domain` and `Persistence` are pure C# and never reference `UnityEngine`. The same sources compile for `netstandard2.1` in `tools/dotnet`, which is how CI tests them.
- Everything that must survive a save lives in `WorldState` and is mapped in `WorldStateMapper`. GameObjects only show state.
- Items move only through `Inventory`, `InventoryTransfers` and `ReservedTransfers`.
- Simulation randomness uses `DeterministicRandom`, never `System.Random` or `UnityEngine.Random`.
- Domain operations return `OpResult` with a reason a player can read: machines and placement always explain why they are not working.
- Art is code: meshes come from `MeshBuilder` / `MeshFactory`, materials from `MaterialLibrary`.

## Code style

- C# 9 at most: no records, `init`, file-scoped namespaces or global usings.
- 4-space indentation, braces on new lines, `_camelCase` private fields. `.editorconfig` encodes this and CI checks it with `dotnet format whitespace --verify-no-changes`.
- XML summaries on public types; comments explain why, not what.
- Builds must be warning-free (`-warnaserror` in CI).

## Commits

- Conventional messages: `feat(scope): …`, `fix(scope): …`, `test(scope): …`, `docs: …`, `chore: …`.
- Commits carry your own Git identity only. The commit-msg hook rejects AI co-author trailers and generated-by footers; please do not add them.
- Never commit secrets, `Library/`, `Temp/`, `Logs/`, `UserSettings/` or builds.

## Assets

Every third-party asset must be recorded in [docs/ASSET_LICENSES.md](docs/ASSET_LICENSES.md) with its source, author and license before it is committed. Do not add assets with unclear licensing, or anything copied from other games. Binary assets go through Git LFS (`.gitattributes`).

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).

Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md).
