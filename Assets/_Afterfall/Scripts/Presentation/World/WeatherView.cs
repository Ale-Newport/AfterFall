using System.Collections.Generic;
using Afterfall.Domain.Weather;
using UnityEngine;
using UnityEngine.Rendering;

namespace Afterfall.Presentation
{
    /// <summary>
    /// Draws the weather the simulation decides: falling rain around the camera, lightning bolts at the
    /// points the <see cref="WeatherSystem"/> strikes, and the flash that lights the sky (read by
    /// <see cref="DayNightCycle"/>). The sky, fog and sounds follow the same <see cref="WeatherReadout"/>.
    /// </summary>
    [DefaultExecutionOrder(-50)] // before DayNightCycle, so a flash lights the sky on the frame after the strike
    public sealed class WeatherView : MonoBehaviour
    {
        /// <summary>Rain tiles are this wide and tall; nine columns of two layers surround the camera.</summary>
        private const float Tile = 16f;

        private const int StreaksPerTile = 800;
        private const float FallSpeed = 11f;
        private const float Slant = 0.12f;

        /// <summary>Brightness (0–1) of the current lightning flash.</summary>
        public static float Flash { get; private set; }

        private GameController _gc;
        private Mesh _rain;
        private Material _rainMat;
        private float _rainAlpha;
        private readonly List<Bolt> _bolts = new List<Bolt>();
        private readonly List<System.IDisposable> _subs = new List<System.IDisposable>();
        private float _flashPeak, _flashAge = 10f;

        private sealed class Bolt
        {
            public GameObject Go;
            public Material Mat;
            public Light Light;
            public float Age;
        }

        public void Initialize(GameController gc)
        {
            _gc = gc;
            _rain = BuildRainMesh();
            _rainMat = MaterialLibrary.UnlitTransparentTextured(StreakTexture());
            _subs.Add(gc.Session.Events.Subscribe<LightningStruck>(OnStrike));
        }

        private void OnDestroy()
        {
            foreach (System.IDisposable s in _subs) s.Dispose();
            _subs.Clear();
            foreach (Bolt b in _bolts)
                if (b.Go != null) Destroy(b.Go);
            Flash = 0f;
        }

        private void Update()
        {
            if (_gc?.Session == null || _gc.CameraRig == null) return;
            float dt = Time.deltaTime;
            WeatherReadout w = _gc.Session.WeatherNow;
            Camera cam = _gc.CameraRig.Camera;
            // Under a roof the third-person camera stays close, so the streaks (which ignore roofs) are hidden.
            bool indoors = _gc.Session.Player.Sheltered && !_gc.CameraRig.TacticalActive;
            float target = indoors ? 0f : w.Rain > 0.02f ? 0.35f + 0.45f * w.Rain : 0f;
            _rainAlpha = Mathf.MoveTowards(_rainAlpha, target, dt * 0.6f);
            if (_rainAlpha > 0.005f && cam != null) DrawRain(cam.transform.position, w.Storm);

            _flashAge += dt;
            // Two quick pulses, then a fading afterglow.
            float pulse = _flashAge < 0.07f ? 1f : _flashAge < 0.12f ? 0.25f : _flashAge < 0.2f ? 0.85f : Mathf.Exp(-(_flashAge - 0.2f) * 9f) * 0.6f;
            Flash = _flashAge > 1.2f ? 0f : _flashPeak * pulse;
            for (int i = _bolts.Count - 1; i >= 0; i--)
            {
                Bolt b = _bolts[i];
                b.Age += dt;
                bool on = b.Age < 0.07f || (b.Age > 0.12f && b.Age < 0.28f);
                b.Go.SetActive(on);
                if (b.Light != null) b.Light.intensity = on ? 6f : 0f;
                if (b.Age < 0.6f) continue;
                Destroy(b.Go);
                _bolts.RemoveAt(i);
            }
        }

        // ------------------------------------------------------------ rain

        private void DrawRain(Vector3 camPos, float storm)
        {
            MaterialLibrary.SetColor(_rainMat, new Color(0.86f, 0.9f, 0.97f, _rainAlpha));
            float fall = Mathf.Repeat(Time.time * FallSpeed * (1f + 0.3f * storm), Tile);
            float drift = Mathf.Repeat(fall * Slant, Tile);
            // Tiles are anchored to the world so the rain does not travel with the camera.
            float ox = Mathf.Floor((camPos.x - drift) / Tile) * Tile + drift;
            float oz = Mathf.Floor(camPos.z / Tile) * Tile;
            float oy = Mathf.Floor((camPos.y - 6f + fall) / Tile) * Tile - fall;
            var rp = new RenderParams(_rainMat) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false, layer = gameObject.layer };
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                    for (int k = 0; k < 2; k++)
                        Graphics.RenderMesh(rp, _rain, 0, Matrix4x4.Translate(new Vector3(ox + i * Tile, oy + k * Tile, oz + j * Tile)));
        }

        /// <summary>One tile of streaks: each is two crossed thin quads, slanted by the wind, fading towards its top.</summary>
        private static Mesh BuildRainMesh()
        {
            var verts = new List<Vector3>(StreaksPerTile * 8);
            var uvs = new List<Vector2>(StreaksPerTile * 8);
            var tris = new List<int>(StreaksPerTile * 12);
            var rng = new System.Random(4242);
            const float width = 0.028f;
            for (int n = 0; n < StreaksPerTile; n++)
            {
                var top = new Vector3((float)rng.NextDouble() * Tile, (float)rng.NextDouble() * Tile, (float)rng.NextDouble() * Tile);
                float len = 0.7f + (float)rng.NextDouble() * 0.6f;
                Vector3 bottom = top + new Vector3(-Slant * len, -len, 0f);
                foreach (Vector3 side in new[] { Vector3.right * width, Vector3.forward * width })
                {
                    int b = verts.Count;
                    verts.Add(top - side);
                    verts.Add(top + side);
                    verts.Add(bottom + side);
                    verts.Add(bottom - side);
                    uvs.Add(new Vector2(0f, 1f));
                    uvs.Add(new Vector2(1f, 1f));
                    uvs.Add(new Vector2(1f, 0f));
                    uvs.Add(new Vector2(0f, 0f));
                    tris.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3, b, b + 2, b + 1, b, b + 3, b + 2 });
                }
            }
            var mesh = new Mesh { name = "Rain tile" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            // Generous bounds: the tile is drawn around the camera and must never be culled by its own box.
            mesh.bounds = new Bounds(new Vector3(Tile * 0.5f, Tile * 0.5f, Tile * 0.5f), Vector3.one * Tile * 1.5f);
            return mesh;
        }

        /// <summary>A streak: bright at the leading (lower) end, fading up its tail and towards both edges.</summary>
        private static Texture2D StreakTexture()
        {
            const int w = 4, h = 64;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "Rain streak", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                float v = y / (float)(h - 1);
                float along = Mathf.SmoothStep(0f, 1f, 1f - v) * Mathf.SmoothStep(0f, 0.15f, v);
                for (int x = 0; x < w; x++)
                {
                    float across = x == 0 || x == w - 1 ? 0.45f : 1f;
                    px[y * w + x] = new Color32(255, 255, 255, (byte)(255 * along * across));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        // ------------------------------------------------------------ lightning

        private void OnStrike(LightningStruck e)
        {
            Vector3 ground = e.Position.ToUnity();
            Camera cam = _gc.CameraRig != null ? _gc.CameraRig.Camera : null;
            float dist = cam != null ? Vector3.Distance(cam.transform.position, ground) : 100f;
            _flashPeak = Mathf.Clamp(1.1f - dist / 320f, 0.35f, 1f);
            _flashAge = 0f;
            if (e.StructureId.IsValid && _gc.Session.World.Structures.TryGetValue(e.StructureId, out Afterfall.Domain.Construction.Structure hit))
                ground = hit.Position.ToUnity() + Vector3.up * (_gc.Session.Content.Building(hit.DefId)?.SizeY ?? 2f);
            _bolts.Add(MakeBolt(ground, e.StructureId.IsValid || dist < 90f));
        }

        /// <summary>A jagged channel from the cloud base down to the strike point, with a branch or two and a flash of light.</summary>
        private Bolt MakeBolt(Vector3 strike, bool light)
        {
            var rng = new System.Random(Time.frameCount * 7919 + (int)(strike.x * 31f));
            var go = new GameObject("Lightning");
            go.transform.SetParent(transform, false);
            var b = new MeshBuilder();
            Vector3 start = strike + new Vector3((float)(rng.NextDouble() - 0.5) * 40f, 110f, (float)(rng.NextDouble() - 0.5) * 40f);
            List<Vector3> main = Channel(start, strike, 14, 6f, rng);
            AddChannel(b, main, 0.35f);
            for (int n = 0; n < 2; n++)
            {
                Vector3 from = main[2 + rng.Next(main.Count / 2)];
                Vector3 to = from + new Vector3((float)(rng.NextDouble() - 0.5) * 30f, -20f - (float)rng.NextDouble() * 25f, (float)(rng.NextDouble() - 0.5) * 30f);
                AddChannel(b, Channel(from, to, 6, 3f, rng), 0.18f);
            }
            Material mat = MaterialLibrary.EmissiveInstance(new Color(0.85f, 0.9f, 1f), new Color(0.75f, 0.82f, 1f) * 12f);
            Shapes.Mesh(go.transform, "Bolt", b.Build("Lightning bolt"), mat, Vector3.zero, null, false);
            Light l = null;
            if (light)
            {
                var lgo = new GameObject("Strike light");
                lgo.transform.SetParent(go.transform, false);
                lgo.transform.position = strike + Vector3.up * 6f;
                l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 40f;
                l.color = new Color(0.8f, 0.85f, 1f);
                l.shadows = LightShadows.None;
            }
            return new Bolt { Go = go, Mat = mat, Light = l };
        }

        private static List<Vector3> Channel(Vector3 from, Vector3 to, int segments, float jag, System.Random rng)
        {
            var pts = new List<Vector3> { from };
            for (int i = 1; i < segments; i++)
            {
                float t = i / (float)segments;
                Vector3 p = Vector3.Lerp(from, to, t);
                float k = jag * (1f - t * 0.6f);
                pts.Add(p + new Vector3((float)(rng.NextDouble() - 0.5) * k, (float)(rng.NextDouble() - 0.5) * k * 0.4f, (float)(rng.NextDouble() - 0.5) * k));
            }
            pts.Add(to);
            return pts;
        }

        /// <summary>Two crossed ribbons per segment, so the channel reads from any side.</summary>
        private static void AddChannel(MeshBuilder b, List<Vector3> pts, float width)
        {
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                Vector3 a = pts[i], c = pts[i + 1];
                foreach (Vector3 side in new[] { Vector3.right * width, Vector3.forward * width })
                {
                    // Both windings, so the ribbon shows from either side.
                    b.Quad(a - side, a + side, c + side, c - side);
                    b.Quad(c - side, c + side, a + side, a - side);
                }
            }
        }
    }
}
