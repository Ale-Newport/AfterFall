using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    /// <summary>
    /// Builds the prototype region: a 512 m × 512 m valley with a lake, a northern ridge,
    /// roads and six hand-designed points of interest. The layout of landmarks is designed
    /// (stable between seeds) while terrain detail, vegetation and loot vary with the seed.
    /// Fully deterministic for a given seed.
    /// </summary>
    public static class WorldGenerator
    {
        private struct ContainerSpec
        {
            public string Def;
            public string Table;
            public float X;
            public float Z;
            public float Yaw;

            public ContainerSpec(string def, string table, float x, float z, float yaw)
            {
                Def = def;
                Table = table;
                X = x;
                Z = z;
                Yaw = yaw;
            }
        }

        public static WorldLayout Generate(int seed)
        {
            var layout = new WorldLayout
            {
                WaterLevel = 15.5f,
                LakeCenter = new Vec3(105f, 0f, 95f),
                LakeRadius = 50f
            };

            DefinePois(layout);
            DefineRoads(layout);
            layout.Height = BuildTerrain(seed, layout);
            ProjectRoadsToGround(layout);

            PlaceStartCamp(layout);
            PlaceSettlement(layout);
            PlaceGasStation(layout);
            PlaceWorkshop(layout);
            PlaceSubstation(layout);
            PlaceQuarry(layout, seed);
            PlaceFarm(layout, seed);
            PlaceRoadside(layout, seed);
            PlaceSurvivors(layout);
            PlaceVegetation(layout, seed);
            PlaceForestSpawns(layout, seed);
            return layout;
        }

        // ------------------------------------------------------------------ layout definition

        private static void DefinePois(WorldLayout l)
        {
            l.Pois.Add(new PoiInfo { Id = "camp", Name = "Survivor's Clearing", Kind = "camp", Center = new Vec3(150, 0, 214), Radius = 14, Risk = "Low", Description = "Where you woke up. A cold fire pit and a torn tent." });
            l.Pois.Add(new PoiInfo { Id = "millbrook", Name = "Millbrook", Kind = "residential", Center = new Vec3(206, 0, 292), Radius = 40, Risk = "Medium", Description = "Abandoned homes and a small clinic. Food, cloth and medicine." });
            l.Pois.Add(new PoiInfo { Id = "gas", Name = "Route 9 Gas Station", Kind = "fuel", Center = new Vec3(300, 0, 230), Radius = 22, Risk = "Low", Description = "Fuel pumps that may still hold fuel, plus snacks and scrap." });
            l.Pois.Add(new PoiInfo { Id = "workshop", Name = "Hartley's Workshop", Kind = "workshop", Center = new Vec3(414, 0, 296), Radius = 28, Risk = "Medium", Description = "A garage and warehouse full of tools, parts and wire." });
            l.Pois.Add(new PoiInfo { Id = "substation", Name = "Ridge Substation", Kind = "substation", Center = new Vec3(448, 0, 398), Radius = 24, Risk = "High", Description = "The old power substation. Electrical components — and the infected." });
            l.Pois.Add(new PoiInfo { Id = "quarry", Name = "Old Quarry", Kind = "quarry", Center = new Vec3(296, 0, 452), Radius = 32, Risk = "Medium", Description = "Exposed iron ore seams and loose stone." });
            l.Pois.Add(new PoiInfo { Id = "farm", Name = "Dawson Farm", Kind = "farm", Center = new Vec3(262, 0, 118), Radius = 34, Risk = "Low", Description = "A farmstead with fiber fields, berries and a barn." });
            l.PlayerSpawn = new Vec3(152, 0, 210);
        }

        private static void DefineRoads(WorldLayout l)
        {
            var main = new RoadPath { Width = 8f, Paved = true };
            AddPoints(main, 0, 250, 120, 252, 200, 264, 262, 258, 300, 250, 360, 278, 412, 322, 462, 346, 512, 352);
            l.Roads.Add(main);

            var north = new RoadPath { Width = 5f, Paved = false };
            AddPoints(north, 300, 250, 308, 318, 318, 388, 302, 432);
            l.Roads.Add(north);

            var south = new RoadPath { Width = 5f, Paved = false };
            AddPoints(south, 200, 264, 214, 196, 246, 140);
            l.Roads.Add(south);

            var sub = new RoadPath { Width = 4.5f, Paved = false };
            AddPoints(sub, 462, 346, 456, 372, 450, 384);
            l.Roads.Add(sub);
        }

        private static void AddPoints(RoadPath r, params float[] xz)
        {
            for (int i = 0; i + 1 < xz.Length; i += 2) r.Points.Add(new Vec3(xz[i], 0, xz[i + 1]));
        }

        // ------------------------------------------------------------------ terrain

        private static float BaseHeight(int seed, float x, float z)
        {
            float h = 22f + Noise.Fbm(seed, x * 0.006f, z * 0.006f, 4) * 6f + Noise.Fbm(seed + 17, x * 0.03f, z * 0.03f, 2) * 0.8f;
            // Northern ridge
            float ridge = SmoothStep(400f, 505f, z);
            h += ridge * (26f + Noise.Fbm(seed + 31, x * 0.02f, z * 0.02f, 3) * 14f);
            // Eastern and western foothills keep the valley enclosed
            h += SmoothStep(470f, 512f, x) * 10f + SmoothStep(40f, 0f, x) * 6f + SmoothStep(30f, 0f, z) * 6f;
            return h;
        }

        private static HeightField BuildTerrain(int seed, WorldLayout l)
        {
            var hf = new HeightField(WorldLayout.HeightResolution, WorldLayout.WorldSize, WorldLayout.MaxTerrainHeight);
            float cell = hf.CellSize;

            // Target heights for flattened areas (computed from the unmodified base terrain).
            var poiHeights = new float[l.Pois.Count];
            for (int i = 0; i < l.Pois.Count; i++)
            {
                PoiInfo p = l.Pois[i];
                float target = BaseHeight(seed, p.Center.X, p.Center.Z);
                if (p.Kind == "quarry") target -= 4f;
                poiHeights[i] = Math.Max(target, l.WaterLevel + 2.5f);
            }

            for (int zi = 0; zi < hf.Resolution; zi++)
            {
                for (int xi = 0; xi < hf.Resolution; xi++)
                {
                    float x = xi * cell;
                    float z = zi * cell;
                    float h = BaseHeight(seed, x, z);

                    // Lake bowl
                    float dl = Vec3.DistanceXZ(new Vec3(x, 0, z), l.LakeCenter) / l.LakeRadius;
                    if (dl < 1.25f) h -= 11f * SmoothStep(1.25f, 0.2f, dl);

                    // Flatten POIs
                    for (int i = 0; i < l.Pois.Count; i++)
                    {
                        PoiInfo p = l.Pois[i];
                        float d = Vec3.DistanceXZ(new Vec3(x, 0, z), p.Center);
                        float w = SmoothStep(p.Radius * 1.6f, p.Radius * 0.85f, d);
                        if (w > 0f) h = h + (poiHeights[i] - h) * w;
                    }

                    // Flatten roads
                    float bestW = 0f, bestH = 0f;
                    foreach (RoadPath r in l.Roads)
                    {
                        for (int s = 0; s + 1 < r.Points.Count; s++)
                        {
                            float d = WorldLayout.DistanceToSegmentXZ(x, z, r.Points[s], r.Points[s + 1], out float t);
                            float w = SmoothStep(r.Width * 0.5f + 9f, r.Width * 0.5f + 1f, d);
                            if (w <= bestW) continue;
                            Vec3 a = r.Points[s], b = r.Points[s + 1];
                            float ha = RoadAnchorHeight(seed, l, poiHeights, a.X, a.Z);
                            float hb = RoadAnchorHeight(seed, l, poiHeights, b.X, b.Z);
                            bestW = w;
                            bestH = ha + (hb - ha) * t;
                        }
                    }
                    if (bestW > 0f) h = h + (bestH - h) * bestW;

                    hf.Set(xi, zi, h);
                }
            }
            return hf;
        }

        private static float RoadAnchorHeight(int seed, WorldLayout l, float[] poiHeights, float x, float z)
        {
            for (int i = 0; i < l.Pois.Count; i++)
                if (Vec3.DistanceXZ(new Vec3(x, 0, z), l.Pois[i].Center) < l.Pois[i].Radius * 1.2f) return poiHeights[i];
            return Math.Max(BaseHeight(seed, x, z), l.WaterLevel + 2f);
        }

        private static void ProjectRoadsToGround(WorldLayout l)
        {
            foreach (RoadPath r in l.Roads)
                for (int i = 0; i < r.Points.Count; i++)
                    r.Points[i] = l.Ground(r.Points[i].X, r.Points[i].Z);
            l.PlayerSpawn = l.Ground(l.PlayerSpawn.X, l.PlayerSpawn.Z);
            for (int i = 0; i < l.Pois.Count; i++) l.Pois[i].Center = l.Ground(l.Pois[i].Center.X, l.Pois[i].Center.Z);
        }

        // ------------------------------------------------------------------ POIs

        private static void PlaceStartCamp(WorldLayout l)
        {
            AddProp(l, "camp", "tent", 147, 216, 35, new Vec3(2.4f, 1.4f, 2.8f));
            AddProp(l, "camp", "firepit", 151.5f, 213, 0, new Vec3(1.2f, 0.3f, 1.2f));
            AddProp(l, "camp", "log_seat", 154, 214.5f, 80, new Vec3(1.8f, 0.4f, 0.4f));
            AddContainer(l, "camp", "loot_crate", "start_cache", 145.5f, 212.5f, 20);
            AddNode(l, "plant_fiber", 158, 219, 0, 1f);
            AddNode(l, "plant_fiber", 143, 205, 0, 1f);
            AddNode(l, "rock_small", 156, 205, 40, 1f);
            AddNode(l, "rock_small", 140, 221, 140, 1f);
            AddNode(l, "branch_pile", 160, 210, 10, 1f);
        }

        private static void PlaceSettlement(WorldLayout l)
        {
            var houseContainers = new[]
            {
                new ContainerSpec("loot_cabinet", "kitchen", -2.9f, -2.1f, 0),
                new ContainerSpec("loot_fridge", "fridge", -1.6f, -2.3f, 0),
                new ContainerSpec("loot_wardrobe", "wardrobe", 3.1f, -1.2f, -90)
            };
            PlaceBuilding(l, "millbrook", "house", 184, 280, 180, new Vec3(8, 3.2f, 6), 0, houseContainers);
            PlaceBuilding(l, "millbrook", "house", 205, 281, 180, new Vec3(8, 3.2f, 6), 1, houseContainers);
            PlaceBuilding(l, "millbrook", "house", 226, 279, 180, new Vec3(8, 3.2f, 6), 2, new[]
            {
                new ContainerSpec("loot_cabinet", "kitchen", -2.9f, -2.1f, 0),
                new ContainerSpec("loot_toolbox", "toolbox", 3.0f, 1.8f, -90)
            });
            PlaceBuilding(l, "millbrook", "house", 192, 304, 180, new Vec3(8, 3.2f, 6), 3, new[]
            {
                new ContainerSpec("loot_fridge", "fridge", -2.9f, -2.1f, 0),
                new ContainerSpec("loot_wardrobe", "wardrobe", 3.1f, -1.2f, -90)
            });
            PlaceBuilding(l, "millbrook", "clinic", 222, 305, 180, new Vec3(10, 3.4f, 8), 0, new[]
            {
                new ContainerSpec("loot_medcab", "medical", -4.0f, -3.0f, 0),
                new ContainerSpec("loot_medcab", "medical", -1.5f, -3.2f, 0),
                new ContainerSpec("loot_crate", "general", 3.8f, -2.6f, -90)
            });
            AddProp(l, "millbrook", "fence", 176, 270, 90, new Vec3(0.1f, 1.1f, 6));
            AddProp(l, "millbrook", "fence", 236, 270, 90, new Vec3(0.1f, 1.1f, 6));
            AddProp(l, "millbrook", "sign", 214, 270, 180, new Vec3(1.6f, 2.2f, 0.2f));
            AddNode(l, "wreck_car", 196, 268, 75, 1f);
            AddNode(l, "wreck_car", 230, 289, 10, 1f);
            AddNode(l, "scrap_pile", 213, 293, 0, 1f);
            AddNode(l, "scrap_pile", 180, 296, 0, 1f);
            AddSpawns(l, 196, 290, 214, 296, 230, 300, 188, 274, 220, 274);
        }

        private static void PlaceGasStation(WorldLayout l)
        {
            PlaceBuilding(l, "gas", "shop", 306, 222, 0, new Vec3(10, 3.4f, 7), 0, new[]
            {
                new ContainerSpec("loot_shelf", "store", -3.5f, -2.4f, 0),
                new ContainerSpec("loot_shelf", "store", 0.0f, -2.4f, 0),
                new ContainerSpec("loot_toolbox", "toolbox", 4.0f, 1.0f, -90)
            });
            AddProp(l, "gas", "canopy", 296, 238, 0, new Vec3(12, 4.5f, 8));
            AddNode(l, "fuel_pump", 293, 238, 0, 1f);
            AddNode(l, "fuel_pump", 299, 238, 0, 1f);
            AddNode(l, "wreck_car", 288, 230, 120, 1f);
            AddNode(l, "scrap_pile", 314, 232, 0, 1f);
            AddProp(l, "gas", "sign", 286, 246, 0, new Vec3(1.6f, 3.0f, 0.2f));
            AddSpawns(l, 302, 232, 294, 226);
        }

        private static void PlaceWorkshop(WorldLayout l)
        {
            PlaceBuilding(l, "workshop", "garage", 408, 302, 0, new Vec3(14, 5, 10), 0, new[]
            {
                new ContainerSpec("loot_toolbox", "toolbox", -5.8f, -3.8f, 0),
                new ContainerSpec("loot_toolbox", "workshop", -3.4f, -4.0f, 0),
                new ContainerSpec("loot_crate", "workshop", 5.6f, -3.6f, -90)
            });
            PlaceBuilding(l, "workshop", "warehouse", 430, 288, 0, new Vec3(16, 6, 12), 0, new[]
            {
                new ContainerSpec("loot_crate", "general", -6.5f, -4.5f, 0),
                new ContainerSpec("loot_crate", "workshop", -4.0f, -4.6f, 0),
                new ContainerSpec("loot_locker", "electrical_minor", 6.6f, -3.5f, -90)
            });
            AddNode(l, "wreck_car", 398, 290, 30, 1f);
            AddNode(l, "wreck_car", 420, 312, 95, 1f);
            AddNode(l, "scrap_pile", 400, 312, 0, 1f);
            AddNode(l, "scrap_pile", 440, 300, 0, 1f);
            AddNode(l, "scrap_pile", 418, 282, 0, 1f);
            AddProp(l, "workshop", "fence", 396, 300, 90, new Vec3(0.1f, 1.6f, 14));
            AddSpawns(l, 410, 296, 426, 296, 400, 306, 436, 284);
        }

        private static void PlaceSubstation(WorldLayout l)
        {
            const float cx = 448, cz = 398;
            // Perimeter fence with a gate gap facing the access road (south).
            AddProp(l, "substation", "fence", cx - 11, cz, 90, new Vec3(0.1f, 2.2f, 22));
            AddProp(l, "substation", "fence", cx + 11, cz, 90, new Vec3(0.1f, 2.2f, 22));
            AddProp(l, "substation", "fence", cx, cz + 11, 0, new Vec3(22, 2.2f, 0.1f));
            AddProp(l, "substation", "fence", cx - 7, cz - 11, 0, new Vec3(8, 2.2f, 0.1f));
            AddProp(l, "substation", "fence", cx + 7, cz - 11, 0, new Vec3(8, 2.2f, 0.1f));
            AddProp(l, "substation", "transformer", cx - 5, cz + 4, 0, new Vec3(3, 3, 2.4f));
            AddProp(l, "substation", "transformer", cx + 1, cz + 4, 0, new Vec3(3, 3, 2.4f));
            AddProp(l, "substation", "pylon", cx + 7, cz + 6, 0, new Vec3(1.5f, 12, 1.5f));
            AddProp(l, "substation", "pylon", cx - 20, cz + 20, 45, new Vec3(1.5f, 12, 1.5f));
            PlaceBuilding(l, "substation", "shack", cx + 4, cz - 4, 0, new Vec3(6, 3, 5), 0, new[]
            {
                new ContainerSpec("loot_locker", "electrical", -2.1f, -1.8f, 0),
                new ContainerSpec("loot_locker", "schematic", 2.1f, -1.8f, 0)
            });
            AddNode(l, "scrap_pile", cx - 6, cz - 4, 0, 1f);
            AddNode(l, "transformer_wreck", cx - 2, cz - 6, 0, 1f);
            AddSpawns(l, cx - 3, cz + 1, cx + 6, cz, cx - 7, cz - 6, cx + 2, cz + 8, cx, cz - 14);
        }

        private static void PlaceQuarry(WorldLayout l, int seed)
        {
            var rng = DeterministicRandom.Derive(seed, "quarry");
            PoiInfo q = l.Poi("quarry");
            AddProp(l, "quarry", "cliff", q.Center.X, q.Center.Z + 26, 0, new Vec3(40, 9, 5));
            AddProp(l, "quarry", "cliff", q.Center.X - 24, q.Center.Z + 8, 70, new Vec3(26, 7, 5));
            AddProp(l, "quarry", "cliff", q.Center.X + 25, q.Center.Z + 10, -65, new Vec3(26, 7, 5));
            PlaceBuilding(l, "quarry", "shack", q.Center.X + 14, q.Center.Z - 16, 200, new Vec3(5, 2.8f, 4), 1, new[]
            {
                new ContainerSpec("loot_crate", "mining", 0f, -1.2f, 0)
            });
            for (int i = 0; i < 14; i++)
            {
                float a = rng.Range(0f, (float)Math.PI);
                float r = rng.Range(8f, 22f);
                AddNode(l, "ore_iron", q.Center.X + (float)Math.Cos(a) * r, q.Center.Z + (float)Math.Sin(a) * r, rng.Range(0f, 360f), rng.Range(0.9f, 1.3f));
            }
            for (int i = 0; i < 10; i++)
            {
                float a = rng.Range(0f, (float)Math.PI * 2);
                float r = rng.Range(4f, 26f);
                AddNode(l, "rock_small", q.Center.X + (float)Math.Cos(a) * r, q.Center.Z + (float)Math.Sin(a) * r, rng.Range(0f, 360f), rng.Range(0.9f, 1.4f));
            }
            AddSpawns(l, q.Center.X - 8, q.Center.Z + 4, q.Center.X + 10, q.Center.Z - 2);
        }

        private static void PlaceFarm(WorldLayout l, int seed)
        {
            var rng = DeterministicRandom.Derive(seed, "farm");
            PlaceBuilding(l, "farm", "barn", 272, 110, 90, new Vec3(12, 6, 9), 0, new[]
            {
                new ContainerSpec("loot_crate", "barn", -4.5f, -3.4f, 0),
                new ContainerSpec("loot_toolbox", "toolbox", 4.5f, -3.4f, 0)
            });
            PlaceBuilding(l, "farm", "house", 248, 128, 140, new Vec3(8, 3.2f, 6), 3, new[]
            {
                new ContainerSpec("loot_cabinet", "kitchen", -2.9f, -2.1f, 0),
                new ContainerSpec("loot_fridge", "fridge", -1.6f, -2.3f, 0),
                new ContainerSpec("loot_wardrobe", "wardrobe", 3.1f, -1.2f, -90)
            });
            AddProp(l, "farm", "fence", 262, 96, 0, new Vec3(26, 1.1f, 0.1f));
            AddProp(l, "farm", "fence", 249, 106, 90, new Vec3(0.1f, 1.1f, 20));
            AddProp(l, "farm", "silo", 284, 124, 0, new Vec3(4, 9, 4));
            for (int i = 0; i < 18; i++)
                AddNode(l, "plant_fiber", rng.Range(236f, 262f), rng.Range(92f, 112f), rng.Range(0f, 360f), rng.Range(0.8f, 1.2f));
            for (int i = 0; i < 6; i++)
                AddNode(l, "bush_berry", rng.Range(278f, 292f), rng.Range(132f, 146f), rng.Range(0f, 360f), rng.Range(0.9f, 1.2f));
            AddNode(l, "wreck_car", 258, 140, 30, 1f);
            AddSpawns(l, 268, 120, 252, 120, 276, 100);
        }

        private static void PlaceRoadside(WorldLayout l, int seed)
        {
            var rng = DeterministicRandom.Derive(seed, "roadside");
            RoadPath main = l.Roads[0];
            for (int i = 0; i + 1 < main.Points.Count; i++)
            {
                Vec3 a = main.Points[i], b = main.Points[i + 1];
                float len = Vec3.DistanceXZ(a, b);
                for (float t = 12f; t < len; t += 30f)
                {
                    Vec3 p = Vec3.Lerp(a, b, t / len);
                    Vec3 dir = (b - a).Normalized;
                    var side = new Vec3(-dir.Z, 0, dir.X);
                    Vec3 pole = p + side * 6.5f;
                    if (l.PoiAt(pole.X, pole.Z) != null || !l.IsInsideMap(pole.X, pole.Z, 4)) continue;
                    AddProp(l, "", "power_pole_old", pole.X, pole.Z, (float)(Math.Atan2(dir.X, dir.Z) * 180 / Math.PI), new Vec3(0.3f, 7, 0.3f));
                    if (rng.Chance(0.12f))
                    {
                        Vec3 car = p + side * (rng.Chance(0.5f) ? 2.2f : -2.2f);
                        AddNode(l, "wreck_car", car.X, car.Z, rng.Range(0f, 360f), 1f);
                    }
                }
            }
        }

        private static void PlaceSurvivors(WorldLayout l)
        {
            var mara = new SurvivorSeed { Name = "Mara Voss", PoiId = "farm", Position = l.Ground(250, 126) };
            mara.Skills["Haul"] = 1.3f;
            mara.Skills["Build"] = 0.9f;
            l.Survivors.Add(mara);
            var eli = new SurvivorSeed { Name = "Eli Brandt", PoiId = "workshop", Position = l.Ground(406, 304) };
            eli.Skills["Build"] = 1.5f;
            eli.Skills["Haul"] = 0.9f;
            l.Survivors.Add(eli);
        }

        // ------------------------------------------------------------------ vegetation & resources

        private static void PlaceVegetation(WorldLayout l, int seed)
        {
            var rng = DeterministicRandom.Derive(seed, "vegetation");
            const float spacing = 9f;
            for (float z = 8f; z < WorldLayout.WorldSize - 8f; z += spacing)
            {
                for (float x = 8f; x < WorldLayout.WorldSize - 8f; x += spacing)
                {
                    float px = x + rng.Range(-3.5f, 3.5f);
                    float pz = z + rng.Range(-3.5f, 3.5f);
                    float density = Noise.Fbm(seed + 77, px * 0.011f, pz * 0.011f, 3);
                    if (!IsFreeForVegetation(l, px, pz, 2.5f)) continue;
                    // Forest is denser on the west side and thins out toward the east fields.
                    float bias = (256f - px) / 256f * 0.25f;
                    if (density + bias > -0.05f && rng.Chance(0.62f))
                    {
                        string type = rng.Chance(0.72f) ? "tree_pine" : "tree_birch";
                        AddNode(l, type, px, pz, rng.Range(0f, 360f), rng.Range(0.8f, 1.3f));
                    }
                    else if (density + bias < -0.2f && rng.Chance(0.10f))
                    {
                        AddNode(l, "plant_fiber", px, pz, rng.Range(0f, 360f), rng.Range(0.8f, 1.2f));
                    }
                    else if (rng.Chance(0.035f))
                    {
                        AddNode(l, "bush_berry", px, pz, rng.Range(0f, 360f), rng.Range(0.8f, 1.2f));
                    }
                }
            }

            // Stones everywhere, more on the ridge.
            for (int i = 0; i < 140; i++)
            {
                float px = rng.Range(10f, WorldLayout.WorldSize - 10f);
                float pz = rng.Range(10f, WorldLayout.WorldSize - 10f);
                if (pz < 360f && !rng.Chance(0.55f)) continue;
                if (!IsFreeForVegetation(l, px, pz, 2f)) continue;
                AddNode(l, pz > 420f && rng.Chance(0.15f) ? "ore_iron" : "rock_small", px, pz, rng.Range(0f, 360f), rng.Range(0.8f, 1.5f));
            }
            for (int i = 0; i < 25; i++)
            {
                float px = rng.Range(10f, WorldLayout.WorldSize - 10f);
                float pz = rng.Range(10f, WorldLayout.WorldSize - 10f);
                if (!IsFreeForVegetation(l, px, pz, 3f)) continue;
                AddNode(l, "branch_pile", px, pz, rng.Range(0f, 360f), 1f);
            }
        }

        private static bool IsFreeForVegetation(WorldLayout l, float x, float z, float clearance)
        {
            if (!l.IsInsideMap(x, z, 6f)) return false;
            if (l.IsWater(x, z) || l.HeightAt(x, z) < l.WaterLevel + 0.8f) return false;
            if (l.Height.SlopeDegrees(x, z) > 32f) return false;
            foreach (RoadPath r in l.Roads)
            {
                for (int i = 0; i + 1 < r.Points.Count; i++)
                    if (WorldLayout.DistanceToSegmentXZ(x, z, r.Points[i], r.Points[i + 1], out _) < r.Width * 0.5f + clearance + 1.5f) return false;
            }
            foreach (PoiInfo p in l.Pois)
                if (Vec3.DistanceXZ(p.Center, new Vec3(x, 0, z)) < p.Radius + clearance) return false;
            return true;
        }

        private static void PlaceForestSpawns(WorldLayout l, int seed)
        {
            var rng = DeterministicRandom.Derive(seed, "forest_spawns");
            int added = 0;
            for (int attempt = 0; attempt < 200 && added < 12; attempt++)
            {
                float x = rng.Range(30f, WorldLayout.WorldSize - 30f);
                float z = rng.Range(30f, WorldLayout.WorldSize - 30f);
                if (l.IsWater(x, z) || l.Height.SlopeDegrees(x, z) > 25f) continue;
                if (Vec3.DistanceXZ(new Vec3(x, 0, z), l.PlayerSpawn) < 70f) continue;
                l.ZombieSpawnPoints.Add(l.Ground(x, z));
                added++;
            }
        }

        // ------------------------------------------------------------------ helpers

        private static void PlaceBuilding(WorldLayout l, string poi, string kind, float x, float z, float yaw, Vec3 size, int variant, ContainerSpec[] containers)
        {
            l.Props.Add(new PropPlacement { Kind = kind, Position = l.Ground(x, z), Yaw = yaw, Size = size, PoiId = poi, Variant = variant });
            if (containers == null) return;
            foreach (ContainerSpec c in containers)
            {
                Vec3 offset = Rotate(new Vec3(c.X, 0, c.Z), yaw);
                AddContainer(l, poi, c.Def, c.Table, x + offset.X, z + offset.Z, yaw + c.Yaw);
            }
        }

        private static void AddProp(WorldLayout l, string poi, string kind, float x, float z, float yaw, Vec3 size)
        {
            l.Props.Add(new PropPlacement { Kind = kind, Position = l.Ground(x, z), Yaw = yaw, Size = size, PoiId = poi });
        }

        private static void AddContainer(WorldLayout l, string poi, string def, string table, float x, float z, float yaw)
        {
            l.Containers.Add(new ContainerSeed { DefId = def, LootTable = table, Position = l.Ground(x, z), Yaw = yaw, PoiId = poi });
        }

        private static void AddNode(WorldLayout l, string def, float x, float z, float yaw, float scale)
        {
            l.Nodes.Add(new NodeSeed { DefId = def, Position = l.Ground(x, z), Yaw = yaw, Scale = scale });
        }

        private static void AddSpawns(WorldLayout l, params float[] xz)
        {
            for (int i = 0; i + 1 < xz.Length; i += 2) l.ZombieSpawnPoints.Add(l.Ground(xz[i], xz[i + 1]));
        }

        /// <summary>Rotates a local offset by a yaw in degrees (Unity convention: +yaw turns +Z toward +X).</summary>
        public static Vec3 Rotate(Vec3 local, float yawDegrees)
        {
            double r = yawDegrees * Math.PI / 180.0;
            float c = (float)Math.Cos(r), s = (float)Math.Sin(r);
            return new Vec3(local.X * c + local.Z * s, local.Y, -local.X * s + local.Z * c);
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = (x - edge0) / (edge1 - edge0);
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            return t * t * (3f - 2f * t);
        }
    }
}
