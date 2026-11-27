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

Preview without Unity: `dotnet run -c Release --project tools/dotnet/ArtPreview -- map <outdir> [seed]` writes `map.png`.

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
`WaypointIndicator` projects the waypoint (2 m above the ground) to the screen. In view, a diamond with the name and distance sits over it; out of view or behind the camera, an arrow is pinned to the screen edge pointing toward it.

## Strategic view
The strategic camera keeps its own fog plane over the 3D world (from the same `ExplorationState`) for local management. The world map is for regional travel. Both use world coordinates and the same discovery data, so what is explored in one is explored in the other.

## Saves
`WorldDto.Navigation` holds the markers (id, name, position, kind) and the waypoint. Older saves have none and load with an empty list. Marker ids come from the world's id allocator and are never reused.

## Not done yet
- Detail tiles: the overview is enough at 512 m (0.5 m per pixel). For the larger world the plan is the same rasterizer per tile, cached and loaded by zoom level.
- Road-aware route planning for vehicles.
- Vehicle positions on the map (vehicles do not exist yet).

## Tests
- Headless (`MapTests`): projection round trips at several rotations; north-up and heading-up orientation; the raster shows the lake as water, the main road, a building footprint and woodland; a tile matches the whole map at its seams; rendering is deterministic. `CreativeTests` covers waypoint and marker persistence and the waypoint rules.
- In the build (tour, `MapScene`): the map is drawn; only discovered places show in Survival; clicking sets a waypoint at a distant place; right-clicking places a marker; the player marker is at the survivor's position; facing the waypoint shows its marker on screen; walking toward it reduces the distance; facing away pins the marker to the edge; the minimap rotates.
