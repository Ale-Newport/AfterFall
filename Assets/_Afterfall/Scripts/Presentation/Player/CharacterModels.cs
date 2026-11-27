using System;
using System.Collections.Generic;
using UnityEngine;

namespace Afterfall.Presentation
{
    /// <summary>
    /// The skinned human models in Resources/Characters, made from MakeHuman CC0 assets with MPFB
    /// (tools/characters/build_characters.py). Each model has a manifest (<c>name.json</c>) naming its
    /// mesh parts and their textures; materials are built here from three URP Lit material assets
    /// (opaque, opaque with a normal map, alpha-cut and double-sided) and shared between characters.
    /// The infected use the same pipeline with greyed, bloodless skin, grimy clothes and glowing eyes.
    /// </summary>
    public static class CharacterModels
    {
        [Serializable]
        public sealed class Part
        {
            public string mesh = "";
            public string kind = "";
            public string albedo = "";
            public string normal = "";
            public int triangles;
        }

        [Serializable]
        public sealed class Manifest
        {
            public string name = "";
            public string role = "";
            public bool female;
            public int triangles;
            public Part[] parts = Array.Empty<Part>();
        }

        private const string Folder = "Characters/";
        private static readonly string[] SurvivorsMale = { "survivor_m", "colonist_m" };
        private static readonly string[] SurvivorsFemale = { "survivor_f", "colonist_f" };
        private static readonly string[] InfectedMale = { "infected_m1", "infected_m2" };
        private static readonly string[] InfectedFemale = { "infected_f1" };

        private static readonly Dictionary<string, Manifest> Manifests = new Dictionary<string, Manifest>();
        private static readonly Dictionary<string, GameObject> Models = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static Material _opaque, _normal, _cutout;
        private static bool? _available;

        /// <summary>
        /// Set false to fall back to the procedural characters everywhere (tests, comparisons); the
        /// <c>-afterfall-procedural-characters</c> command-line flag does the same.
        /// </summary>
        public static bool Enabled = Array.IndexOf(Environment.GetCommandLineArgs(), "-afterfall-procedural-characters") < 0;

        /// <summary>True when the character assets are present in this build.</summary>
        public static bool Available
        {
            get
            {
                if (_available == null) _available = Resources.Load<TextAsset>(Folder + "survivor_m") != null;
                return _available.Value;
            }
        }

        /// <summary>
        /// Loads every model, manifest, texture and material up front (during the loading screen), so
        /// the first infected or survivor to come into view does not stall a frame.
        /// </summary>
        public static void Preload()
        {
            if (!Enabled || !Available) return;
            foreach (string[] set in new[] { SurvivorsMale, SurvivorsFemale, InfectedMale, InfectedFemale })
                foreach (string name in set)
                {
                    Manifest m = Load(name);
                    if (m == null) continue;
                    if (!Models.ContainsKey(name)) Models[name] = Resources.Load<GameObject>(Folder + name);
                    bool infected = m.role == "infected";
                    foreach (Part p in m.parts) MaterialFor(p, infected);
                }
        }

        /// <summary>The model for a palette: the player's look, a colonist or an infected (chosen by variant).</summary>
        public static string Pick(HumanoidRig.Palette p, bool infected)
        {
            string[] set = infected ? (p.Female ? InfectedFemale : InfectedMale) : (p.Female ? SurvivorsFemale : SurvivorsMale);
            int i = p.Backpack && !infected ? 0 : ((p.Variant % set.Length) + set.Length) % set.Length;
            return set[i];
        }

        public static Manifest Load(string name)
        {
            if (Manifests.TryGetValue(name, out Manifest m)) return m;
            var text = Resources.Load<TextAsset>(Folder + name);
            m = text != null ? JsonUtility.FromJson<Manifest>(text.text) : null;
            Manifests[name] = m;
            return m;
        }

        /// <summary>
        /// A new instance of the model with its materials, or null when the model is missing.
        /// Renderers are returned so the rig can hide them (first person, vehicles).
        /// </summary>
        public static GameObject Instantiate(string name, Transform parent, bool infected, List<Renderer> renderers)
        {
            Manifest manifest = Load(name);
            if (manifest == null) return null;
            if (!Models.TryGetValue(name, out GameObject prefab) || prefab == null)
            {
                prefab = Resources.Load<GameObject>(Folder + name);
                Models[name] = prefab;
            }
            if (prefab == null) return null;
            GameObject go = UnityEngine.Object.Instantiate(prefab, parent, false);
            go.name = "Model_" + name;
            var parts = new Dictionary<string, Part>(StringComparer.Ordinal);
            foreach (Part p in manifest.parts) parts[p.mesh] = p;
            foreach (SkinnedMeshRenderer r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!parts.TryGetValue(r.gameObject.name, out Part part) && !parts.TryGetValue(r.sharedMesh != null ? r.sharedMesh.name : "", out part))
                {
                    r.enabled = false;
                    continue;
                }
                r.sharedMaterial = MaterialFor(part, infected);
                r.updateWhenOffscreen = false;
                r.skinnedMotionVectors = false;
                r.shadowCastingMode = part.kind == "lashes" || part.kind == "eyes" ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
                renderers?.Add(r);
            }
            return go;
        }

        private static Material MaterialFor(Part part, bool infected)
        {
            // The infected's eyes glow like the procedural ones (the shared emissive material).
            if (infected && part.kind == "eyes") return MaterialLibrary.Emissive(new Color(1f, 0.86f, 0.42f), new Color(1f, 0.74f, 0.2f) * 3.2f);
            string key = $"{part.kind}:{part.albedo}:{part.normal}:{(infected ? 1 : 0)}";
            if (Materials.TryGetValue(key, out Material m) && m != null) return m;
            bool cutout = part.kind == "hair" || part.kind == "lashes";
            bool normal = !cutout && !string.IsNullOrEmpty(part.normal);
            Material template = cutout ? Base(ref _cutout, "AfterfallCharacterCutout") : normal ? Base(ref _normal, "AfterfallCharacterNormal") : Base(ref _opaque, "AfterfallCharacter");
            m = new Material(template) { name = "Character " + key, enableInstancing = true };
            var albedo = Resources.Load<Texture2D>(Folder + part.albedo);
            if (albedo != null) m.SetTexture("_BaseMap", albedo);
            if (normal)
            {
                var bump = Resources.Load<Texture2D>(Folder + part.normal);
                if (bump != null) m.SetTexture("_BumpMap", bump);
            }
            Color tint = Color.white;
            float smooth = part.kind == "skin" ? 0.32f : part.kind == "eyes" ? 0.85f : part.kind == "hair" ? 0.22f : 0.12f;
            if (infected)
            {
                switch (part.kind)
                {
                    case "skin": tint = new Color(0.66f, 0.72f, 0.6f); smooth = 0.42f; break; // grey-green, clammy
                    case "cloth": tint = new Color(0.62f, 0.58f, 0.52f); smooth = 0.08f; break; // grime
                    case "hair": tint = new Color(0.7f, 0.68f, 0.64f); break;
                }
            }
            m.SetColor("_BaseColor", tint);
            m.SetFloat("_Smoothness", smooth);
            Materials[key] = m;
            return m;
        }

        private static Material Base(ref Material slot, string name)
        {
            if (slot != null) return slot;
            slot = Resources.Load<Material>("Materials/" + name) ?? MaterialLibrary.Lit(Color.white);
            return slot;
        }
    }
}
