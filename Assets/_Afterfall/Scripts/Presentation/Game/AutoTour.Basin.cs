using System.Collections;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.World;
using Afterfall.Services;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>The basin scene of the scripted tour: a new game in the 64 km Kessel Basin and a visit to its biomes and its city.</summary>
    public sealed partial class AutoTour
    {
        /// <summary><c>-afterfall-tour-basin</c>: run only the basin scene.</summary>
        public const string OnlyBasinFlag = "-afterfall-tour-basin";

        private IEnumerator BasinScene(GameController previous)
        {
            Line("— The Kessel Basin (world version 3) —");
            float t0 = Time.realtimeSinceStartup;
            _boot.StartNewGame(DifficultyPreset.Survival, 20261107, GameMode.Survival, 3);
            float timeout = Time.realtimeSinceStartup + P(240f);
            while ((_boot.Game == null || _boot.Game == previous || !_boot.Game.Running) && Time.realtimeSinceStartup < timeout) yield return null;
            GameController gc = _boot.Game;
            if (gc == null || gc == previous || !gc.Running)
            {
                Line("FAIL: the basin did not load");
                yield break;
            }
            float loaded = Time.realtimeSinceStartup - t0;
            while (!gc.NavReady && Time.realtimeSinceStartup < timeout) yield return null;
            GameSession s = gc.Session;
            WorldLayout l = s.World.Layout;
            s.Threats.SpawningEnabled = false;
            s.Weather.Hold(Afterfall.Domain.Weather.WeatherKind.Clear);
            SkipTo(gc, 15.5);
            Check(l.IsChunked && Mathf.Approximately(l.Size, 65536f), $"A new game in {l.RegionName}: {l.Size / 1000f:0} km across, {l.GeneratedChunkCount} chunks generated, {s.World.SeededChunks.Count} seeded, {s.World.ResourceNodes.Count} nodes, {s.World.Structures.Count} structures, {s.World.Zombies.Count} infected");
            Line($"Basin load: playable after {loaded:0.0} s");
            yield return Seconds(3f);
            yield return Capture("90_basin_start");
            yield return MeasureFrames("basin start (Millbrook)", 4f);

            var stops = new (string Name, float X, float Z, float Yaw, float Pitch)[]
            {
                ("Kesselmouth, Old Quarter", 39980f, 30250f, 40f, 4f),
                ("Kesselmouth, terraced streets", 33300f, 33900f, 120f, 6f),
                ("Kesselmouth, Docklands", 51800f, 29800f, 200f, 6f),
                ("the Aldwater in the city", 44500f, 31200f, 250f, 10f),
                ("the Greyspine (snow)", 6300f, 30400f, 90f, 2f),
                ("the High Moor", 30200f, 54200f, 0f, 8f),
                ("the Lake District", 50600f, 54200f, 30f, 8f),
                ("the Scar", 10600f, 6200f, 60f, 8f),
                ("the southern coast", 57600f, 9400f, 90f, 10f),
                ("Harrowgate", 14050f, 8880f, 20f, 8f)
            };
            int shot = 0;
            foreach ((string name, float x, float z, float yaw, float pitch) in stops)
            {
                gc.TeleportTo(new Vector3(x, l.HeightAt(x, z), z), name, false);
                yield return null;
                float tp = Time.realtimeSinceStartup + P(60f);
                while (gc.Teleporting && Time.realtimeSinceStartup < tp) yield return null;
                // Let streaming catch up: terrain, chunks, the city's meshes and the NavMesh.
                float settle = Time.realtimeSinceStartup + P(25f);
                yield return Seconds(2f);
                while ((gc.TerrainTiles.PaintingTiles > 0 || l.PendingChunks > 0 || gc.NavBaking) && Time.realtimeSinceStartup < settle) yield return null;
                yield return Seconds(2.5f);
                Vector3 here = gc.Player.transform.position;
                gc.CameraRig.Look(yaw, pitch);
                yield return Seconds(1f);
                ChunkViews v = gc.Chunks;
                Check(Vector3.Distance(Flat(here), new Vector3(x, 0, z)) < 60f && gc.IsWorldReadyAt(here),
                    $"At {name} {Fmt(here)}, {here.y:0} m: {l.GeneratedChunkCount} chunks made, city {v.DetailedChunks} detailed + {v.SkylineChunks} skyline, {gc.Sync.NodeViewCount} node views, {gc.TerrainTiles.LoadedTiles} tiles");
                if (shot == 0)
                {
                    float mw = Time.realtimeSinceStartup + P(20f);
                    while (gc.Maps.Detail == null && Time.realtimeSinceStartup < mw) yield return null;
                    yield return Seconds(1f);
                    Check(gc.Maps.Detail != null, $"The minimap draws the city's streets and buildings from a local map around the survivor (from {gc.Maps.DetailOrigin})");
                }
                yield return Capture($"9{(shot < 9 ? "" : "9")}{shot + 1}_basin_{name.Replace(" ", "_").Replace(",", "").Replace("(", "").Replace(")", "")}");
                yield return MeasureFrames(name, 3f);
                shot++;
            }
            float wait = Time.realtimeSinceStartup + P(30f);
            while (!gc.Maps.Ready && Time.realtimeSinceStartup < wait) yield return null;
            Check(gc.Maps.Ready, $"The basin's world map was drawn ({gc.Maps.OverviewSize}×{gc.Maps.OverviewSize} px for {l.Size / 1000f:0} km)");
            gc.Maps.RevealAll = true;
            gc.Ui.ToggleWorldMap();
            yield return Seconds(1.5f);
            yield return Capture("99z_basin_world_map");
            gc.Ui.CloseWindow();
            gc.Maps.RevealAll = false;
            var t = (TiledHeightField)l.Height;
            Line($"Basin streaming: {t.TilesBuilt} height tiles made ({t.CachedTiles} cached), {l.GeneratedChunkCount} chunks generated, {s.World.SeededChunks.Count} seeded, {s.World.ResourceNodes.Count} nodes and {s.World.Structures.Count} structures in the world, {s.World.Zombies.Count} infected");
            string save = Afterfall.Persistence.SaveSerializer.Serialize(s.World, "tour", 1);
            Check(save.Length < 20_000_000, $"A save after crossing the basin is {save.Length / 1024} KB");
        }
    }
}
