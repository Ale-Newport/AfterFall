# Launch kit

Ready-to-post announcements for AFTERFALL `v0.1.1`, written to be accurate about what the project is: an early prototype with procedural art, verified by tests and a scripted tour, not yet played by hand. Post them yourself from your own accounts, one community at a time, and stay in the thread to answer questions.

## When to post

Since v0.1.1 there is a Windows build, and its scripted tour runs on a GitHub-hosted Windows runner. One gate remains: [a hand playtest of the full loop](https://github.com/Ale-Newport/AfterFall/issues/1), ideally including someone on a real Windows PC ([#2](https://github.com/Ale-Newport/AfterFall/issues/2)). "Nobody has played it yet" invites the obvious first comment. A good order:

1. **Now:** r/Unity3D (developer audience, interested in the architecture) and the Discussions *Show and tell* category.
2. **After a playtest pass:** Show HN, r/gamedev on its self-promotion day, r/csharp.
3. **After a playtest on a real Windows PC:** r/IndieDev, r/SurvivalGaming and r/BaseBuildingGames, where their rules allow posts about free open-source games.

Read each community's current rules before posting; they change. Never ask for stars or upvotes, never post the same text to several subreddits on the same day, and never use extra accounts.

## Show HN

Rules: https://news.ycombinator.com/showhn.html. People must be able to try it: link the repository (the release download is one click from the README). Post Tuesday–Thursday, morning US Eastern time.

**Title** (80 characters max):

> Show HN: AfterFall – open-source survival-to-industry game on a headless C# sim

**URL:** `https://github.com/Ale-Newport/AfterFall`

**First comment** (post it right after submitting):

> Hi HN. AfterFall is a single-player 3D survival game in Unity 6 that I'm building in the open. You start with bare hands in a ruined valley, build a shelter, smelt iron, bring a generator online, and end up running a small powered settlement with colonists who haul, build and farm.
>
> The part I find most interesting technically: the whole game is a deterministic C# simulation that never references UnityEngine. Inventories, crafting, construction, machines, the power grid (with battery storage and priority load shedding), colonist jobs, the infected and saves all live in one WorldState. Unity only renders it and feeds it input. The same sources compile for netstandard2.1, so 155 tests run without Unity on Linux, Windows and macOS in CI. Items can only move through a transactional, reservation-based inventory, which has made "colonist hauling never loses items" a testable property instead of a hope.
>
> All the art is code: meshes, textures and item icons are generated procedurally, and there's no third-party art or audio.
>
> Honest status: it's a v0.1.1 prototype. A scripted tour exercises every system with no failures on the macOS build and on a GPU-less Windows CI runner, and tests check that every technology, building and recipe is reachable on generated worlds. But nobody has played the loop by hand yet. Windows and macOS downloads are in the release. I'd love feedback on the architecture, and from anyone willing to play for 20 minutes.
>
> Code is MIT licensed.

## r/Unity3D

Use the *Show-Off* flair (check the current flair names). Attach `docs/media/afterfall-demo.gif` or a short screen capture.

**Title:** I'm building an open-source survival → industry game where the whole simulation runs without UnityEngine (155 headless tests)

**Body:**

> AfterFall is a single-player survival/base-building game in Unity 6.3 LTS (URP). You go from bare hands to a powered settlement: furnace, generator, power poles, batteries with priority load shedding, an electric pump irrigating crops, and recruited colonists who haul and build.
>
> Architecture, since that's the interesting bit here:
> - Domain, persistence and services are plain C# assemblies with `noEngineReferences: true`, compiled for netstandard2.1 and tested in CI with `dotnet test`, so no Unity license is needed for the test suite.
> - MonoBehaviours only visualise WorldState and send commands; the only values written back are the player's physical position and NavMesh agent positions.
> - Procedural everything: meshes from a MeshBuilder, textures and icons synthesised at startup on worker threads, materials varied with vertex colours instead of per-instance materials.
> - UI Toolkit for all UI, Input System actions defined in code with rebinding, Cinemachine 3.
> - A scripted "tour" mode plays the build headlessly, checks every system and captures the screenshots in the README.
>
> Repo (MIT): https://github.com/Ale-Newport/AfterFall
>
> Happy to answer questions about the sim/presentation split, and feedback is very welcome.

## r/gamedev

Only on the day the subreddit allows self-promotion (check the sidebar), or as a technical write-up without a call to play.

**Title:** What keeping my survival game's simulation free of UnityEngine bought me (and cost me)

**Body:** a short post-mortem in your own words. Suggested points: tests running in seconds without the editor; saves that contain the world rather than the scene; a scripted tour as a smoke test; the cost of duplicating physical facts (NavMesh, colliders) back into the sim; bugs that only appeared in the engine (see the "First runs in Unity" section of CHANGELOG.md). End with the repository link.

## r/csharp

**Title:** A deterministic game simulation in C# 9 / netstandard2.1 with a transactional inventory and 155 tests

**Body:** focus on the code: `Inventory` reservations and two-phase `ReservedTransfers`, `DeterministicRandom` (xorshift64*) with saved state, the fixed-rate tick scheduler, and the save format (versioned JSON, SHA-256 checksum, migrations, atomic writes and backup recovery). Link to `Assets/_Afterfall/Scripts/Domain` and `docs/ARCHITECTURE.md`.

## r/IndieDev, r/SurvivalGaming, r/BaseBuildingGames

Wait for a hand playtest on Windows. Lead with the GIF and one sentence on the player fantasy ("from scavenging alone to running a powered settlement with colonists"), say plainly that it is a free, early, open-source prototype, and ask for playtest feedback rather than wishlists.

## Assets to reuse

- Demo GIF: `docs/media/afterfall-demo.gif` (720 px, 2.4 MB)
- Screenshots: `docs/media/*.jpg` (1280 px)
- Social card: `docs/media/social-preview.jpg` (1280 × 640). Upload it in **Settings → General → Social preview** so links unfurl with it.
