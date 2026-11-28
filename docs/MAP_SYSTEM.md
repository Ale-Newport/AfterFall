# Map system

The world map (M) and the HUD minimap share one coordinate system, one set of textures and one source of truth: the world layout and the saved exploration and navigation state.

```
WorldLayout (heights, water, roads, props, nodes)
   └─ MapFeatures.Build  ─→ road segments, building footprints, forest-density grid
        └─ MapRasterizer.Render(region, pixels)  ─→ RGBA (worker thread)
             └─ MapTextures.Overview (Texture2D)  ─→ WorldMapWindow, Minimap
ExplorationState (saved)  ─→ MapTextures.Fog (one texel per 16 m cell)
MapNavigation (saved)     ─→ waypoint, markers, bookmarks
MapSymbols.Collect        ─→ what the player knows: player, waypoint, markers, discovered places, settlement, survivors
```

No camera renders the world for either map.

## Coordinates
- World: metres, X east, Z north, origin at the south-west corner of the map; the map is `WorldLayout.Size` across.
- Map textures: row 0 is the southern edge (Unity texture order), so the UI draws them north-up without flipping.
- `MapView` (Domain/World/MapRaster.cs) converts world ↔ screen pixels for a centre, a scale (metres per pixel), a viewport and a rotation. Rotation 0 is north-up; the rotating minimap passes the camera heading, which brings the direction faced to the top. `WorldToScreen` and `ScreenToWorld` are exact inverses (tested at several rotations).
- Bearings (`MapGeometry.Bearing`) are clockwise from north; distances print as "340 m" or "2.4 km".

## Rendering
`MapRasterizer` is pure C# (no UnityEngine), deterministic and thread-safe. Per pixel it samples the height field: hypsometric tints by height above the water, hill shading lit from the north-west, contour lines every 5 m (every 25 m stronger, widths adapted to the scale), woodland from a blurred 8 m tree-density grid with a light stipple, water by depth with a dark shoreline. Then it draws building footprints (rotated rectangles, darker rims) and roads (paved roads cased, tracks plain), anti-aliased in world units. Any rectangle of the world can be rendered on its own, and a tile is pixel-for-pixel the same as the matching part of a larger render (tested), so the map can be split into tiles when the world grows. The 512 m valley renders as one 1024² overview (0.5 m per pixel) in about 80 ms on a worker thread.

Rivers and lakes are drawn at their own water levels (`WorldLayout.WaterAt`), the railway as a thin dark line under the roads, bridges as the roads that cross the water.

Preview without Unity: `dotnet run -c Release --project tools/dotnet/ArtPreview -- map <outdir> [seed] [world version]` writes `map.png` (2048² for the 2 km region, rendered in about 0.5 s).

## Exploration and fog
- The fog texture comes from `ExplorationState` (saved) and is rebuilt only when it changes. Unexplored land is covered by a dark wash through which the relief shows faintly.
- Discovered places (`ExplorationState.DiscoveredPois`) get symbols and labels; undiscovered ones do not, so a waypoint set on unknown ground is named by its coordinates, not by the place.
- Free flight never reveals land (see CREATIVE_DEBUG_MODE.md).
- With developer tools on, "show the whole map (view only)" removes the fog and shows every place. It only changes the drawing: the saved exploration is untouched, and it turns itself off if developer tools are not enabled.
- Hidden enemies are never shown.

## World map (M)
- Drag to pan, mouse wheel to zoom around the cursor, +/−, "Centre on me", "Whole map". Labels appear when zoomed in; place names always.
- Left-click sets the single waypoint; right-click places a custom marker named from the text field (or "Marker n"). Markers are listed with Show, "Go here" (makes it the waypoint) and remove.
- The side panel shows the cursor's coordinates and distance, the waypoint's distance, compass direction and bearing, exploration progress (share of land and places discovered), the minimap orientation setting and a legend.
- The game keeps running while the map is open.

## Minimap (HUD, bottom right)
- 220 px disc centred on the survivor: same textures and fog, symbols for nearby known places, markers, the settlement, recruited survivors and the waypoint (pinned to the rim when out of range), an N marker on the rim, and the current place and waypoint distance below it.
- North-up by default; "Minimap turns with you" (map side panel) rotates it with the camera.
- Zoom with = and - (five steps, 0.35–3 m per pixel). Hidden in the strategic view and when dead.
- The crafting progress panel moved above it.

## Waypoint marker
`WaypointIndicator` projects the waypoint (2 m above the ground) to the screen. In view, a diamond with the name and distance sits over it; out of view or behind the camera, an arrow is pinned to the screen edge pointing toward it. When the way by road is much longer than the straight line, the label gives both.

## The way by road
- `RoadNetwork` (Domain/World) turns the drivable roads (not the railways) into a graph: every road is split where it crosses another or where it ends against one (T-junctions, streets meeting at chunk edges), and points closer than 0.35 m are one junction. Tracks cost 1.3 times their length; going across country costs 3 times its length.
- A route joins the network at the roads near the start (the nearest and any within 150 m more of it), runs A* along the graph and leaves it at the roads near the destination, unless walking or driving straight there is cheaper. Turns are named at junctions from the heading 20 m either side (bear, turn, sharp; left or right), plus where the route leaves the road and the arrival.
- `RoutePlanner` (Services, `GameSession.Routes`) builds the network on a worker from the roads known (the world's roads and the streets of the chunks made so far; it is rebuilt at most every 20 s as more streets appear), plans the route on a worker when the waypoint changes, follows the survivor's progress along it, and plans again from where they are after 2 s more than 35 m off it. Nothing is saved: the route follows from the waypoint.
- The world map and the minimap draw it as a gold line under the symbols (`MapLine`, the vector painter; only the stretches near the view are given each frame). The minimap's caption names the next turn and the distance left ("Turn left in 300 m · 12.4 km to go"); the map's side panel gives the distance by road and how much of it is off the road.
- In the basin the network has about 23 500 junctions and bends before any chunk streets; it builds in 40–60 ms and a 64 km route is planned in about 10 ms (headless, Apple M2 Max).

## Strategic view
The strategic camera keeps its own fog plane over the 3D world (from the same `ExplorationState`) for local management. The world map is for regional travel. Both use world coordinates and the same discovery data, so what is explored in one is explored in the other.

## Saves
`WorldDto.Navigation` holds the markers (id, name, position, kind) and the waypoint. Older saves have none and load with an empty list. Marker ids come from the world's id allocator and are never reused.

## The 64 km basin
The overview (2048², 32 m per pixel) is drawn from cached coarse grids (heights every 64 m, biomes every 256 m): the city in grey, snow, sand, heath, marsh, the rivers, lakes and sea, and the road and rail network; no terrain tiles are made for it. Around the survivor a **local map** (1024 m at 1 m per pixel, streets and building footprints of the generated chunks) is redrawn on a worker when they have moved 300 m or new chunks appear; the minimap shows it over the overview, and so does the world map when zoomed in.

## Not done yet
- Detail tiles: one overview covers the 2 km region; at close zoom it is coarser than in the valley. Worlds beyond 2 km need the same rasterizer per tile, cached and loaded by zoom level.
- Routes use the streets of generated chunks only; across ungenerated parts of the city they follow the ring roads and avenues.
- No spoken or arrow guidance on the road itself; the turn is named under the minimap.

## Tests
- Headless (`MapTests`): projection round trips at several rotations; north-up and heading-up orientation; the raster shows the lake as water, the main road, a building footprint and woodland; a tile matches the whole map at its seams; rendering is deterministic. `CreativeTests` covers waypoint and marker persistence and the waypoint rules. `RouteTests` (5): routes follow the roads and turn left or right at a crossing that is not a point of either road; road ends meeting another road are joined; railways are never driven; across country only when quicker; the paved road beats the track; a 64 km route between basin towns follows the roads within 1.6 times the straight line; the planner follows progress and plans again after straying.
- In the build (tour, `MapScene`): the map is drawn; only discovered places show in Survival; clicking sets a waypoint at a distant place; right-clicking places a marker; the player marker is at the survivor's position; facing the waypoint shows its marker on screen; walking toward it reduces the distance; facing away pins the marker to the edge; the minimap rotates.
