// Renders AFTERFALL's procedural art to PNG without Unity, so textures and icons can be reviewed headlessly.
//
//   dotnet run -c Release --project tools/dotnet/ArtPreview -- textures <outdir> [filter]
//   dotnet run -c Release --project tools/dotnet/ArtPreview -- icons <outdir> [filter]
//   dotnet run -c Release --project tools/dotnet/ArtPreview -- all <outdir>
//
// textures: for every Surface declared in the Art sources, <key>_lit.png (2x2 tiles, lit, mid-grey tint: check
//           seams and relief), <key>_albedo.png and <key>_normal.png.
// icons:    every item in Resources/Content/items.json at 128 px (<id>.png), plus icons_sheet.png
//           showing all icons on slot-coloured tiles at 128 px and at the 56 px they get in the HUD.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Afterfall.Presentation.Art;

internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: ArtPreview <textures|icons|all> <outdir> [filter]");
            return 2;
        }
        string mode = args[0], outDir = Path.GetFullPath(args[1]);
        string filter = args.Length > 2 ? args[2] : null;
        Directory.CreateDirectory(outDir);
        if (mode == "textures" || mode == "all") Textures(outDir, filter);
        if (mode == "icons" || mode == "all") Icons(outDir, filter);
        Console.WriteLine("Wrote previews to " + outDir);
        return 0;
    }

    private static void Textures(string outDir, string filter)
    {
        // The shared catalogue plus any Surface declared in other Art classes (custom recipes).
        IEnumerable<Surface> surfaces = typeof(Surface).Assembly.GetTypes()
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
            .Where(f => f.FieldType == typeof(Surface)).Select(f => (Surface)f.GetValue(null))
            .Where(s => s != null).GroupBy(s => s.Key).Select(g => g.First());
        foreach (Surface s in surfaces)
        {
            if (filter != null && !s.Key.Contains(filter)) continue;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            SurfaceMaps m = s.Generate(s.Resolution);
            sw.Stop();
            int n = m.Size;
            var albedo = new byte[n * n * 4];
            var normal = new byte[n * n * 4];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    int src = (y * n + x) * 4, dst = ((n - 1 - y) * n + x) * 4;
                    for (int c = 0; c < 3; c++) albedo[dst + c] = ToByte(LinearToSrgb(m.Albedo[src + c]));
                    albedo[dst + 3] = 255;
                    normal[dst] = ToByte(m.Detail[src]);
                    normal[dst + 1] = ToByte(m.Detail[src + 1]);
                    normal[dst + 2] = ToByte(m.Detail[src + 2]);
                    normal[dst + 3] = 255;
                }
            Png.Write(Path.Combine(outDir, s.Key + "_albedo.png"), n, n, albedo);
            Png.Write(Path.Combine(outDir, s.Key + "_normal.png"), n, n, normal);
            Png.Write(Path.Combine(outDir, s.Key + "_lit.png"), n * 2, n * 2, Lit(m, s));
            Console.WriteLine($"{s.Key,-16} {n}px  {sw.ElapsedMilliseconds,4} ms  tile {s.TileMeters} m");
        }
    }

    /// <summary>2x2 tiles shaded like the surface shader with a mid-grey tint and a low sun from the top left.</summary>
    private static byte[] Lit(SurfaceMaps m, Surface s)
    {
        int n = m.Size, w = n * 2;
        var px = new byte[w * w * 4];
        float lx = -0.45f, ly = 0.55f, lz = 0.7f;
        float ll = MathF.Sqrt(lx * lx + ly * ly + lz * lz);
        lx /= ll; ly /= ll; lz /= ll;
        for (int y = 0; y < w; y++)
            for (int x = 0; x < w; x++)
            {
                int src = ((y % n) * n + (x % n)) * 4;
                float nx = (m.Detail[src] * 2f - 1f) * s.NormalStrength, ny = (m.Detail[src + 1] * 2f - 1f) * s.NormalStrength;
                float nz = MathF.Sqrt(MathF.Max(0f, 1f - nx * nx - ny * ny));
                float ndl = MathF.Max(0f, nx * lx + ny * ly + nz * lz);
                float cav = 1f + (m.Detail[src + 2] - 1f) * s.CavityStrength;
                int dst = ((w - 1 - y) * w + x) * 4;
                for (int c = 0; c < 3; c++)
                {
                    float albedo = m.Albedo[src + c] / SurfaceMaps.AlbedoMean * 0.35f;
                    float lin = albedo * (ndl * 1.6f + 0.35f * cav);
                    px[dst + c] = ToByte(LinearToSrgb(lin));
                }
                px[dst + 3] = 255;
            }
        return px;
    }

    private static void Icons(string outDir, string filter)
    {
        string root = FindRepoRoot();
        string json = File.ReadAllText(Path.Combine(root, "Assets/_Afterfall/Resources/Content/items.json"));
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement items = doc.RootElement.ValueKind == JsonValueKind.Array ? doc.RootElement : doc.RootElement.GetProperty("items");
        var list = new List<(string id, IconCanvas icon)>();
        foreach (JsonElement it in items.EnumerateArray())
        {
            string id = it.GetProperty("id").GetString();
            if (filter != null && !id.Contains(filter)) continue;
            string category = it.TryGetProperty("category", out JsonElement c) ? c.GetString() : "";
            string color = it.TryGetProperty("color", out JsonElement col) ? col.GetString() : "#888888";
            IconCanvas icon = ItemIconArt.Render(id, category, color, ItemIconArt.DefaultSize);
            Png.Write(Path.Combine(outDir, id + ".png"), icon.Size, icon.Size, icon.ToRgba32(false));
            list.Add((id, icon));
            Console.WriteLine($"{id,-22} {(ItemIconArt.HasDrawing(id) ? "drawing" : "category symbol (" + category + ")")}");
        }
        Sheet(Path.Combine(outDir, "icons_sheet.png"), list);
    }

    /// <summary>All icons on slot-coloured tiles: a row of 128 px tiles, then the same icons at 56 px.</summary>
    private static void Sheet(string path, List<(string id, IconCanvas icon)> icons)
    {
        const int cols = 8, big = 136, small = 64;
        int rows = (icons.Count + cols - 1) / cols;
        int w = cols * big, h = rows * (big + small);
        var px = new float[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            px[i * 4] = 40 / 255f; px[i * 4 + 1] = 42 / 255f; px[i * 4 + 2] = 38 / 255f; px[i * 4 + 3] = 1f;
        }
        for (int k = 0; k < icons.Count; k++)
        {
            int cx = (k % cols) * big, cy = (k / cols) * (big + small);
            Paste(px, w, icons[k].icon, cx + 4, cy + 4, 128);
            Paste(px, w, icons[k].icon, cx + 40, cy + big + 4, 56);
        }
        var bytes = new byte[w * h * 4];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = ToByte(px[i]);
        Png.Write(path, w, h, bytes);
    }

    /// <summary>Box-filtered paste of a premultiplied icon onto an opaque sheet.</summary>
    private static void Paste(float[] dst, int w, IconCanvas icon, int ox, int oy, int size)
    {
        float k = (float)icon.Size / size;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                int n = 0;
                for (int sy = (int)(y * k); sy < (int)((y + 1) * k) && sy < icon.Size; sy++)
                    for (int sx = (int)(x * k); sx < (int)((x + 1) * k) && sx < icon.Size; sx++)
                    {
                        int s = (sy * icon.Size + sx) * 4;
                        r += icon.Px[s]; g += icon.Px[s + 1]; b += icon.Px[s + 2]; a += icon.Px[s + 3];
                        n++;
                    }
                if (n == 0) continue;
                r /= n; g /= n; b /= n; a /= n;
                int d = ((oy + y) * w + ox + x) * 4;
                dst[d] = r + dst[d] * (1 - a);
                dst[d + 1] = g + dst[d + 1] * (1 - a);
                dst[d + 2] = b + dst[d + 2] * (1 - a);
            }
    }

    private static string FindRepoRoot()
    {
        string dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "Assets/_Afterfall"))) dir = Path.GetDirectoryName(dir);
        return dir ?? Directory.GetCurrentDirectory();
    }

    private static float LinearToSrgb(float v) => v <= 0.0031308f ? v * 12.92f : 1.055f * MathF.Pow(v, 1f / 2.4f) - 0.055f;
    private static byte ToByte(float v) => (byte)Math.Clamp((int)MathF.Round(v * 255f), 0, 255);
}

/// <summary>Minimal RGBA8 PNG writer.</summary>
internal static class Png
{
    private static readonly uint[] Crc = BuildCrc();

    public static void Write(string path, int w, int h, byte[] rgba)
    {
        using var fs = File.Create(path);
        fs.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var ihdr = new byte[13];
        Be(ihdr, 0, (uint)w);
        Be(ihdr, 4, (uint)h);
        ihdr[8] = 8; ihdr[9] = 6;
        Chunk(fs, "IHDR", ihdr);
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, true))
        {
            for (int y = 0; y < h; y++)
            {
                z.WriteByte(0);
                z.Write(rgba, y * w * 4, w * 4);
            }
        }
        Chunk(fs, "IDAT", raw.ToArray());
        Chunk(fs, "IEND", Array.Empty<byte>());
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        Be(len, 0, (uint)data.Length);
        s.Write(len);
        byte[] t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        uint c = 0xffffffffu;
        foreach (byte b in t) c = Crc[(c ^ b) & 0xff] ^ (c >> 8);
        foreach (byte b in data) c = Crc[(c ^ b) & 0xff] ^ (c >> 8);
        var crc = new byte[4];
        Be(crc, 0, c ^ 0xffffffffu);
        s.Write(crc);
    }

    private static void Be(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
    }

    private static uint[] BuildCrc()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xedb88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }
}
