# Content pipeline

Static game content is **data**: JSON files in `Assets/_Afterfall/Resources/Content/`. They are loaded at runtime with `Resources.Load<TextAsset>` and in tests directly from disk. The loader (`ContentJson`) rejects unknown fields, so a typo fails loudly. `ContentValidator` checks every cross-reference. It runs:
- in the EditMode tests (`ContentTests.ShippedContent_HasNoValidationErrors`),
- when the editor loads (`AFTERFALL → Development → Validate Content` for details),
- before every command-line build,
- at game start (errors are shown as a toast and logged).

Field names are case-insensitive camelCase versions of the C# properties. Item amounts use the compact form `"item_id:quantity"`.

## Items: `items.json`
```json
{ "id": "iron_ingot", "name": "Iron Ingot", "description": "...", "category": "Processed",
  "maxStack": 30, "weight": 1.0, "icon": "IN", "color": "#B8BCC2",
  "burnSeconds": 0, "energyKJ": 0, "maxDurability": 0, "tags": [],
  "consumable": { "hunger": 0, "thirst": 0, "health": 0, "stamina": 0, "returns": "" },
  "tool": { "type": "Axe|Pickaxe|Hammer|Knife|Weapon", "gatherPower": 2, "damage": 12, "staminaCost": 7, "cooldown": 0.85, "range": 1.8, "wearPerUse": 1 } }
```
- `category` is one of: Natural, Salvaged, Processed, FoodMedicine, Tool, Construction, Mechanical, Electrical.
- Durable items (`maxDurability > 0`) must have `maxStack: 1`.
- `burnSeconds > 0` makes an item furnace fuel; `energyKJ > 0` makes it generator fuel.
- `icon` (2 letters) and `color` drive the procedural UI icon and the held-item colour.

## Recipes: `recipes.json`
```json
{ "id": "gears", "name": "Machine Gears", "inputs": ["iron_ingot:1"], "outputs": ["gear:2"],
  "station": "workshop", "requiredTech": "electricity", "duration": 6, "category": "Machining" }
```
- `station: ""` makes a hand recipe. `"workbench"` needs a completed workbench within 5 m. Machine stations (`furnace`, `workshop`, `water_pump`) are run by machines and selected in the machine panel.
- Every station must be provided by some building's `craftingStation` or `machine.station`.

## Buildings: `buildings.json`
```json
{ "id": "workshop", "name": "Machine Workshop", "category": "Industry",
  "cost": ["planks:8", "iron_ingot:6"], "buildSeconds": 8, "maxHealth": 350,
  "sizeX": 2.4, "sizeY": 1.6, "sizeZ": 1.4, "snap": "Free|Foundation|Wall|Roof",
  "requiresSupport": false, "providesSupport": false, "maxSlopeDegrees": 25, "requiresWater": false,
  "requiredTech": "electricity", "visual": "workshop", "color": "#5D6D7E",
  "machine": { "station": "workshop", "inputSlots": 4, "outputSlots": 4, "fuelSlots": 0,
               "powerWatts": 500, "powerPriority": 2, "noiseRadius": 12, "autoTransferRange": 3.5 } }
```
Optional components: `storage {slots, maxWeight}`, `craftingStation`, `machine`, `generator {capacityWatts, fuelSlots, noiseRadius}`, `battery {capacityKJ, maxRateWatts}`, `powerNode {range, isRelay}`, `light {powerWatts, range, powerPriority}`. Flags: `isDoor`, `isBed`, `isDefensive`, `blocksMovement`, `buildable` (`false` for world loot props).

**Visuals:** `visual` selects a builder in `Presentation/World/StructureVisuals.cs`. Add a `case` there for a new look; unknown keys fall back to a coloured box.

## Machines
A machine is a building with a `machine` block whose `station` matches its recipes' `station`. Inventory filters are derived automatically: inputs accept only recipe inputs, outputs only recipe outputs, fuel only burnable items. A machine with exactly one recipe is pre-configured. Set `powerWatts > 0` for electric machines and `fuelSlots > 0` for combustion machines.

## Technologies: `technologies.json`
```json
{ "id": "electricity", "name": "Electrification", "era": 4, "prerequisites": ["metalworking"],
  "conditions": [ { "trigger": "AcquireItem", "target": "schematic_generator", "count": 1, "description": "..." } ] }
```
Triggers: `Start`, `AcquireItem` (first time the item is in the backpack), `BuildStructure`, `CraftItem` (hand, station or machine output; counts units), `DiscoverPoi`. All conditions must be met, along with all prerequisites.

## Resource nodes: `resource_nodes.json`
```json
{ "id": "ore_iron", "name": "Iron Ore Seam", "yields": [ { "itemId": "iron_ore", "min": 1, "max": 2, "chance": 1 } ],
  "harvests": 6, "requiredTool": "Pickaxe", "preferredTool": "Pickaxe", "respawnSeconds": 0,
  "actionSeconds": 1.5, "noiseRadius": 8, "verb": "Mine", "visual": "ore" }
```
Visuals live in `Presentation/World/NodeVisuals.cs`. Placement is in `Domain/World/WorldGenerator.cs` (`PlaceVegetation` for scattered nodes, `AddNode` inside POI builders).

## Loot tables: `loot_tables.json`
Each entry is rolled independently: `{ "itemId", "min", "max", "chance" }`. Entries with `chance ≥ 1` are guaranteed and unaffected by low loot abundance. Containers reference tables by id in `WorldGenerator` (`ContainerSpec`).

## Enemies: `archetypes.json`
`ZombieArchetype` fields: health, walk, investigate and chase speed, sight range and FOV, proximity sense, hearing, attack range, damage and cooldown, lose-target and search time, wander radius. Spawns currently use `"wanderer"`. New behaviours belong in `Domain/AI/ZombieBrain.cs` (pure, unit-testable).

## Points of interest
POIs are designed in code (`WorldGenerator.DefinePois` plus a `PlaceX` method). To add one:
1. Add a `PoiInfo` (id, name, kind, centre, radius, risk, description). Terrain is flattened automatically.
2. Add buildings with `PlaceBuilding(layout, poi, kind, x, z, yaw, size, variant, containers)`. `kind` must have a builder in `Presentation/World/PropBuilder.cs`. The door is on local +Z.
3. Add containers (`ContainerSpec` with a loot table), nodes (`AddNode`) and infected spawn points (`AddSpawns`).
4. Give it a reason to visit: unique loot or resources tied to progression.
5. Run the tests. `WorldGeneratorTests` checks determinism and content counts.

## NPCs (future)
Colonists will be domain entities in `WorldState` (id, needs, skills, inventory, job) driven by a job scheduler that uses `ReservedTransfers` for hauling. No content format exists yet.
