## What does this change?

<!-- One or two sentences. Link the issue it closes, for example: Closes #12 -->

## How was it verified?

- [ ] `dotnet test tools/dotnet/AfterfallSim.Tests/AfterfallSim.Tests.csproj` passes
- [ ] New or changed simulation behaviour has EditMode tests
- [ ] Presentation changes were run in Unity (Play mode or `tools/run-unity-checks.sh`); screenshots attached below
- [ ] Not applicable: documentation or tooling only

## Checklist

- [ ] Domain code (`Core`, `Domain`, `Persistence`) does not reference `UnityEngine`
- [ ] New persistent state lives in `WorldState` and is mapped in `WorldStateMapper`
- [ ] Items move only through `Inventory` / `InventoryTransfers` / `ReservedTransfers`
- [ ] Simulation randomness uses `DeterministicRandom`
- [ ] C# 9 only (no records, `init`, file-scoped namespaces or global usings)
- [ ] New third-party assets are recorded in `docs/ASSET_LICENSES.md`
- [ ] `CHANGELOG.md` updated under **Unreleased** if players or contributors will notice the change

## Screenshots or notes for the reviewer
