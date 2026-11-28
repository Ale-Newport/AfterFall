using System;
using System.Threading;
using Afterfall.Core;
using Afterfall.Domain.World;
using UnityEngine;

namespace Afterfall.Presentation.UI
{
    /// <summary>
    /// Textures shared by the world map and the minimap: the cartographic overview of the whole
    /// region (rendered once from the layout on a worker thread by <see cref="MapRasterizer"/>,
    /// never by a camera) and the fog-of-war mask from <see cref="ExplorationState"/>, rebuilt
    /// only when exploration changes.
    /// </summary>
    public sealed class MapTextures : System.IDisposable
    {
        /// <summary>Overview resolution limit; at 512 m this is 0.5 m per pixel.</summary>
        public const int MaxOverview = 2048;

        private readonly WorldState _world;
        private volatile byte[] _rendered;
        private volatile Exception _failed;
        private Thread _thread;
        private int _fogVersion = -1;
        private bool _fogHidden;
        private Color32[] _fogPixels;

        public MapTextures(WorldState world)
        {
            _world = world;
            WorldLayout layout = world.Layout;
            OverviewSize = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.CeilToInt(layout.Size / 0.5f)), 256, MaxOverview);
            int res = OverviewSize;
            // The features are gathered here (a chunked world's lists grow on the main thread), then drawn
            // on one low-priority thread, so drawing the map never competes with the game's first frames.
            MapFeatures features = MapFeatures.Build(layout);
            _thread = new Thread(() =>
            {
                try
                {
                    _rendered = MapRasterizer.Render(layout, features, 0f, 0f, layout.Size, layout.Size, res, res, false);
                }
                catch (Exception e)
                {
                    _failed = e;
                }
            })
            {
                IsBackground = true,
                Priority = System.Threading.ThreadPriority.BelowNormal,
                Name = "Afterfall map"
            };
            _thread.Start();
        }

        public int OverviewSize { get; }

        // ------------------------------------------------------------------ local detail (chunked worlds)

        /// <summary>Side of the local detail map in metres (1 m per pixel) around the survivor in a chunked world.</summary>
        public const float DetailSize = 1024f;
        private const float DetailRecenter = 300f;

        /// <summary>A detailed map of the survivor's surroundings (streets and buildings); null outside chunked worlds or until drawn.</summary>
        public Texture2D Detail { get; private set; }

        /// <summary>South-west corner of <see cref="Detail"/> in world metres.</summary>
        public Vector2 DetailOrigin { get; private set; }

        private volatile byte[] _detailPixels;
        private Vector2 _detailPending;
        private bool _detailBusy;
        private int _detailChunks = -1;
        private float _detailNext;

        private void UpdateDetail()
        {
            WorldLayout layout = _world.Layout;
            if (!layout.IsChunked || _world.Player == null) return;
            byte[] done = _detailPixels;
            if (done != null)
            {
                _detailPixels = null;
                _detailBusy = false;
                if (Detail == null)
                    Detail = new Texture2D((int)DetailSize, (int)DetailSize, TextureFormat.RGBA32, true, false) { name = "Local map", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
                Detail.SetPixelData(done, 0);
                Detail.Apply(true, false);
                DetailOrigin = _detailPending;
            }
            if (_detailBusy) return;
            Vec3 p = _world.Player.Position;
            Vector2 centre = new Vector2(DetailOrigin.x + DetailSize * 0.5f, DetailOrigin.y + DetailSize * 0.5f);
            bool moved = Detail == null || Mathf.Abs(p.X - centre.x) > DetailRecenter || Mathf.Abs(p.Z - centre.y) > DetailRecenter;
            // Also redraw when new chunks near the survivor have been generated (their streets and buildings).
            if (!moved && (layout.ChunkVersion == _detailChunks || Time.realtimeSinceStartup < _detailNext)) return;
            _detailNext = Time.realtimeSinceStartup + 8f;
            _detailChunks = layout.ChunkVersion;
            float x0 = Mathf.Clamp(Mathf.Round(p.X / 64f) * 64f - DetailSize * 0.5f, 0f, layout.Size - DetailSize);
            float z0 = Mathf.Clamp(Mathf.Round(p.Z / 64f) * 64f - DetailSize * 0.5f, 0f, layout.Size - DetailSize);
            MapFeatures features = MapFeatures.Build(layout, x0, z0, x0 + DetailSize, z0 + DetailSize);
            _detailPending = new Vector2(x0, z0);
            _detailBusy = true;
            int n = (int)DetailSize;
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    _detailPixels = MapRasterizer.Render(layout, features, x0, z0, x0 + DetailSize, z0 + DetailSize, n, n, false);
                }
                catch (Exception e)
                {
                    _failed = e;
                    _detailBusy = false;
                }
            });
        }

        /// <summary>The whole map (null until the worker finishes; poll <see cref="Update"/>).</summary>
        public Texture2D Overview { get; private set; }

        /// <summary>Black where the player has not explored, clear where they have.</summary>
        public Texture2D Fog { get; private set; }

        public bool Ready => Overview != null;

        /// <summary>
        /// Developer view: draw the map without fog. Only the drawing changes; the saved
        /// exploration (what the survivor has actually discovered) is untouched.
        /// </summary>
        public bool RevealAll
        {
            get => _fogHidden;
            set
            {
                if (_fogHidden == value) return;
                _fogHidden = value;
                _fogVersion = -1;
            }
        }

        /// <summary>Uploads finished work and refreshes the fog. Call once per frame from the main thread.</summary>
        public void Update()
        {
            if (_failed != null)
            {
                Debug.LogException(_failed);
                _failed = null;
            }
            byte[] done = _rendered;
            if (Overview == null && done != null)
            {
                {
                    var tex = new Texture2D(OverviewSize, OverviewSize, TextureFormat.RGBA32, true, false)
                    {
                        name = "World map",
                        wrapMode = TextureWrapMode.Clamp,
                        filterMode = FilterMode.Trilinear,
                        anisoLevel = 4
                    };
                    tex.SetPixelData(done, 0);
                    tex.Apply(true, true);
                    Overview = tex;
                }
                _rendered = null;
            }
            UpdateDetail();
            ExplorationState ex = _world.Exploration;
            if (Fog == null)
            {
                Fog = new Texture2D(ex.CellsPerSide, ex.CellsPerSide, TextureFormat.RGBA32, false, false)
                {
                    name = "Map fog",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear
                };
                _fogPixels = new Color32[ex.CellsPerSide * ex.CellsPerSide];
            }
            if (_fogVersion == ex.Version) return;
            _fogVersion = ex.Version;
            var fog = new Color32(28, 30, 32, 236);
            var clear = new Color32(28, 30, 32, 0);
            for (int i = 0; i < _fogPixels.Length; i++) _fogPixels[i] = _fogHidden || ex.Revealed[i] ? clear : fog;
            Fog.SetPixels32(_fogPixels);
            Fog.Apply(false, false);
        }

        public void Dispose()
        {
            if (Overview != null) UnityEngine.Object.Destroy(Overview);
            if (Fog != null) UnityEngine.Object.Destroy(Fog);
        }
    }
}
