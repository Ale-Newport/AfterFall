# ADR-0005: Compile verification against Unity reference assemblies

- **Status:** accepted (2026-11-07)
- **Context:** Unity batch mode requires a signed-in license, which an autonomous agent cannot provide. Presentation code still needs verification.
- **Decision:** `tools/unity-compile-check` compiles every asmdef assembly with the .NET SDK against the managed DLLs from the official Unity 6000.3.28f1 installer and the source of the pinned packages, in player and editor passes, with Unity-like defines.
- **Consequences:** API and type errors are caught early: it caught `UnityEngine.EntityId` ambiguity and `VisualElement.Add` returning void. It does not replace running Unity: imports, serialization, rendering and runtime behaviour still need the editor.
