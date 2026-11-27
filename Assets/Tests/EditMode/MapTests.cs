using System;
using Afterfall.Core;
using Afterfall.Domain.World;
using NUnit.Framework;

namespace Afterfall.Tests
{
    /// <summary>Map projection, the cartographic rasterizer and its tiles.</summary>
    public sealed class MapTests
    {
        private static WorldLayout _layout;
        private static MapFeatures _features;

        private static WorldLayout Layout => _layout ?? (_layout = WorldGenerator.Generate(20261107));
        private static MapFeatures Features => _features ?? (_features = MapFeatures.Build(Layout));

        [Test]
        public void WorldToScreen_RoundTrips_AtAnyRotation()
        {
            foreach (float rot in new[] { 0f, 37f, 90f, 180f, 271.5f })
            {
                var v = new MapView(200f, 300f, 0.75f, 400f, 300f, rot);
                v.WorldToScreen(200f, 300f, out float cx, out float cy);
                Assert.AreEqual(200f, cx, 1e-3);
                Assert.AreEqual(150f, cy, 1e-3);
                v.WorldToScreen(231.5f, 278.25f, out float px, out float py);
                v.ScreenToWorld(px, py, out float x, out float z);
                Assert.AreEqual(231.5f, x, 1e-3);
                Assert.AreEqual(278.25f, z, 1e-3);
            }
        }

        [Test]
        public void NorthUp_PutsNorthAtTheTop_EastToTheRight()
        {
            var v = new MapView(100f, 100f, 1f, 200f, 200f);
            v.WorldToScreen(100f, 150f, out float nx, out float ny);
            Assert.AreEqual(100f, nx, 1e-3);
            Assert.AreEqual(50f, ny, 1e-3, "north is up");
            v.WorldToScreen(130f, 100f, out float ex, out float ey);
            Assert.AreEqual(130f, ex, 1e-3, "east is right");
            Assert.AreEqual(100f, ey, 1e-3);
        }

        [Test]
        public void HeadingUp_PutsTheDirectionFacedAtTheTop()
        {
            // Facing east (bearing 90°): a point due east must appear straight above the player.
            var v = new MapView(100f, 100f, 1f, 200f, 200f, 90f);
            v.WorldToScreen(140f, 100f, out float px, out float py);
            Assert.AreEqual(100f, px, 1e-3);
            Assert.AreEqual(60f, py, 1e-3);
            // North is then on the left.
            v.WorldToScreen(100f, 140f, out float qx, out float qy);
            Assert.AreEqual(60f, qx, 1e-3);
            Assert.AreEqual(100f, qy, 1e-3);
            Assert.IsTrue(v.Contains(px, py) && !v.Contains(-1f, 10f));
        }

        [Test]
        public void Raster_ShowsWaterRoadsBuildingsAndForest()
        {
            WorldLayout l = Layout;
            const int res = 512;
            float size = l.Size;
            byte[] map = MapRasterizer.Render(l, Features, 0f, 0f, size, size, res, res);
            Assert.AreEqual(res * res * 4, map.Length);

            int[] Pixel(float x, float z)
            {
                int col = Math.Min(res - 1, (int)(x / size * res)), row = Math.Min(res - 1, (int)(z / size * res));
                int o = (row * res + col) * 4;
                return new[] { (int)map[o], map[o + 1], map[o + 2], map[o + 3] };
            }

            int[] lake = Pixel(l.LakeCenter.X, l.LakeCenter.Z);
            Assert.Greater(lake[2], lake[0] + 30, "the lake is blue");

            RoadPath main = l.Roads[0];
            Vec3 a = main.Points[3], b = main.Points[4];
            int[] road = Pixel((a.X + b.X) * 0.5f, (a.Z + b.Z) * 0.5f);
            Assert.Greater(road[0], 220, "paved roads are drawn light");
            Assert.Greater(road[1], 200);

            PropPlacement house = l.Props.Find(p => p.Kind == "warehouse");
            int[] roof = Pixel(house.Position.X, house.Position.Z);
            Assert.Less(roof[0] + roof[1] + roof[2], 3 * 170, "buildings are dark footprints");

            // Somewhere in the western forest is greener (relative to red) than the open fields.
            float bestForest = 0f;
            float bx = 0, bz = 0;
            for (float x = 20; x < 240; x += 8)
            {
                for (float z = 20; z < 400; z += 8)
                {
                    float f = Features.ForestAt(x, z);
                    if (f > bestForest && !l.IsWater(x, z))
                    {
                        bestForest = f;
                        bx = x;
                        bz = z;
                    }
                }
            }
            Assert.Greater(bestForest, 0.5f);
            int[] wood = Pixel(bx, bz);
            Assert.Greater(wood[1], wood[0] + 15, "woodland is green");
            Assert.AreEqual(255, wood[3]);
        }

        [Test]
        public void Tiles_MatchTheWholeMap_AtTheirSeams()
        {
            WorldLayout l = Layout;
            float size = l.Size;
            byte[] whole = MapRasterizer.Render(l, Features, 0f, 0f, size, size, 256, 256);
            // The south-east quarter as its own tile, at the same metres per pixel.
            byte[] tile = MapRasterizer.Render(l, Features, size * 0.5f, 0f, size, size * 0.5f, 128, 128);
            int worst = 0;
            for (int row = 0; row < 128; row++)
            {
                for (int col = 0; col < 128; col++)
                {
                    int o = (row * 128 + col) * 4;
                    int ow = (row * 256 + col + 128) * 4;
                    for (int ch = 0; ch < 4; ch++) worst = Math.Max(worst, Math.Abs(tile[o + ch] - whole[ow + ch]));
                }
            }
            Assert.LessOrEqual(worst, 1, "a tile is pixel-for-pixel the same part of the whole map");
        }

        [Test]
        public void Raster_IsDeterministic()
        {
            byte[] a = MapRasterizer.Render(Layout, Features, 100f, 100f, 300f, 300f, 64, 64);
            byte[] b = MapRasterizer.Render(Layout, MapFeatures.Build(Layout), 100f, 100f, 300f, 300f, 64, 64);
            CollectionAssert.AreEqual(a, b);
            Assert.Greater(Features.BuildingCount, 10);
            Assert.Greater(Features.RoadSegmentCount, 10);
        }
    }
}
