# ADR-0001: Engine-independent simulation core

- **Status:** accepted (2026-11-07)
- **Context:** The brief requires simulation-first design (factories produce when unrendered), testable economic invariants and save/load correctness. No licensed Unity Editor was available on the development machine.
- **Decision:** Put all game rules and state in pure C# assemblies (Core, Domain, Persistence, Services) with `noEngineReferences: true`. Compile the same sources as a netstandard2.1 / C# 9 library in `tools/dotnet` and run the EditMode NUnit tests there. Unity code adapts input and physics to domain commands and renders domain state.
- **Consequences:** Most logic is verifiable without Unity and runs in CI. Systems tick from a fixed-rate scheduler instead of `MonoBehaviour.Update`. Presentation has to sync views with state (`WorldSync`). Domain code is limited to C# 9 and netstandard2.1 APIs.
