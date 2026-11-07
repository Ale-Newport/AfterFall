# ADR-0002: JSON content files instead of ScriptableObjects (deviation)

- **Status:** accepted (2026-11-07). Deviates from the brief's "Data definitions: ScriptableObjects".
- **Context:** ScriptableObject assets can only be created and validated inside the Unity Editor, which was unavailable. Content must also be loadable by the headless test suite.
- **Decision:** Define items, recipes, buildings, technologies, resource nodes, loot tables and archetypes as JSON under `Resources/Content`. Load them with strict member checking (`ContentJson`) and validate cross-references (`ContentValidator`) in tests, in the editor and before builds.
- **Consequences:** Content is diff-friendly, mod-friendly and fully testable without Unity. Inspector editing and asset references (icons, prefabs) are not available; visuals are selected by `visual` keys. If designers need Inspector authoring later, add ScriptableObject wrappers that export to or import from the same JSON, and keep JSON as the format the game loads.
