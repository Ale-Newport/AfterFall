# ADR-0003: Runtime-built scene and procedural stylised art

- **Status:** accepted (2026-11-07)
- **Context:** Scenes and prefabs authored by hand as YAML without the editor are fragile, and the world must be identical on load (deterministic from the seed). No licensed art assets were available.
- **Decision:** `Main.unity` contains only `GameBootstrap` and a post-processing Volume. Terrain, props, resource nodes, structures, characters, cameras and UI are created in code from the deterministic `WorldLayout` and the `WorldState`. Art is an intentional low-poly blockout: procedural meshes and a curated palette over shared URP materials derived from material assets, so shader variants are included in builds.
- **Consequences:** The scene cannot be broken by missing references, and the editor shows the world only in Play Mode. A future step is to replace builder functions with imported models or prefabs per `visual` key while keeping the same data contract.
