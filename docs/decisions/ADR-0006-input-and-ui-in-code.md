# ADR-0006: Input actions and UI Toolkit trees defined in code

- **Status:** accepted (2026-11-07)
- **Decision:** `GameInput` builds `InputActionAsset` maps (Global, Player, Tactical) in code. Maps are enabled per game mode, and overrides are saved as JSON in PlayerPrefs (with a rebinding UI). UI Toolkit screens are built in C#, with one USS stylesheet and a runtime-created `PanelSettings` that uses a theme file importing Unity's default theme.
- **Consequences:** No generated assets to keep in sync, and rebinding works out of the box. UXML/UI Builder authoring is not used yet. Visual polish of the UI needs in-engine iteration.
