using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Events;
using Afterfall.Domain.Industry;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;
using UnityEngine;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation
{
    /// <summary>Something the player can aim at and use with the interact key.</summary>
    public interface IInteractable
    {
        /// <summary>Prompt text such as "[E] Chop Pine Tree"; null when nothing can be done.</summary>
        string Prompt(GameController gc);

        void Interact(GameController gc);

        /// <summary>True if holding the interact key performs continuous work (construction).</summary>
        bool HoldToInteract { get; }

        void InteractHeld(GameController gc, float dt);

        /// <summary>True if the primary action (left click) should harvest instead of attack.</summary>
        bool PrimaryHarvests { get; }

        Vector3 FocusPoint { get; }
    }

    /// <summary>Base class for GameObjects that visualise a domain entity.</summary>
    public abstract class EntityView : MonoBehaviour
    {
        public EntityId Id;
    }

    public sealed class ResourceNodeView : EntityView, IInteractable
    {
        public ResourceNodeDefinition Def;
        public bool BuiltDepleted;

        public bool HoldToInteract => false;
        public bool PrimaryHarvests => true;
        public Vector3 FocusPoint => transform.position + Vector3.up;

        public string Prompt(GameController gc)
        {
            if (!gc.Session.World.ResourceNodes.TryGetValue(Id, out ResourceNodeState n)) return null;
            if (n.Depleted) return $"{Def.Name} (exhausted)";
            string tool = Def.RequiredTool != ToolType.None ? $"  — needs {HarvestRules.ToolName(Def.RequiredTool)}" : Def.PreferredTool != ToolType.None ? $"  — faster with {HarvestRules.ToolName(Def.PreferredTool)}" : "";
            return $"[E / LMB] {Def.Verb} {Def.Name}{tool}";
        }

        public void Interact(GameController gc) => gc.HarvestTarget(this);
        public void InteractHeld(GameController gc, float dt) { }
    }

    public sealed class DropView : EntityView, IInteractable
    {
        public bool HoldToInteract => false;
        public bool PrimaryHarvests => false;
        public Vector3 FocusPoint => transform.position + Vector3.up * 0.3f;

        public string Prompt(GameController gc)
        {
            if (!gc.Session.World.Drops.TryGetValue(Id, out WorldDrop d)) return null;
            return $"[E] Open {d.Label}";
        }

        public void Interact(GameController gc)
        {
            if (!gc.Session.World.Drops.TryGetValue(Id, out WorldDrop d)) return;
            gc.Ui.OpenContainer(d.Contents.Id, d.Label);
        }

        public void InteractHeld(GameController gc, float dt) { }
    }

    public sealed class WaterInteractable : MonoBehaviour, IInteractable
    {
        public bool HoldToInteract => false;
        public bool PrimaryHarvests => false;
        public Vector3 FocusPoint => transform.position;
        public string Prompt(GameController gc) => "[E] Drink from the lake";

        public void Interact(GameController gc)
        {
            OpResult r = gc.Session.DrinkFromWater();
            gc.Toast(r.Message, r.Ok ? Severity.Info : Severity.Warning);
        }

        public void InteractHeld(GameController gc, float dt) { }
    }

    public sealed class StructureView : EntityView, IInteractable
    {
        public BuildingDefinition Def;
        public bool BuiltComplete;
        private Transform _engine, _fire, _lampHead, _indicator;
        private Light _light;
        private Material _fireMat, _lampMat;
        private float _vibe;

        public bool HoldToInteract => !BuiltComplete;
        public bool PrimaryHarvests => false;
        public Vector3 FocusPoint => transform.position + Vector3.up * Mathf.Max(0.5f, Def.SizeY * 0.5f);

        public void CacheParts()
        {
            _engine = FindDeep(transform, "Engine");
            _fire = FindDeep(transform, "Fire");
            _lampHead = FindDeep(transform, "LampHead");
            _indicator = FindDeep(transform, "Indicator");
            if (_fire != null)
            {
                _fireMat = MaterialLibrary.EmissiveInstance(new Color(0.1f, 0.08f, 0.07f), Color.black);
                _fire.GetComponent<Renderer>().sharedMaterial = _fireMat;
            }
            if (_lampHead != null && BuiltComplete)
            {
                _lampMat = MaterialLibrary.EmissiveInstance(new Color(0.85f, 0.82f, 0.7f), Color.black);
                _lampHead.GetComponent<Renderer>().sharedMaterial = _lampMat;
                var lgo = new GameObject("Light");
                lgo.transform.SetParent(_lampHead, false);
                lgo.transform.localPosition = new Vector3(0, -0.3f, 0);
                _light = lgo.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.range = Def.Light?.Range ?? 12f;
                _light.intensity = 0f;
                _light.color = new Color(1f, 0.86f, 0.62f);
                _light.shadows = LightShadows.None;
            }
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform c in t)
            {
                Transform f = FindDeep(c, name);
                if (f != null) return f;
            }
            return null;
        }

        /// <summary>Updates animated state (machine activity, lights) from the domain.</summary>
        public void RefreshVisualState(Structure s, float dt)
        {
            if (s == null || !s.IsComplete) return;
            if (_engine != null && s.Generator != null)
            {
                _vibe += dt * 40f;
                float k = s.Generator.Running && s.Generator.OutputWatts > 1f ? 0.012f : 0f;
                _engine.localPosition = new Vector3(Mathf.Sin(_vibe) * k, _engine.localPosition.y, Mathf.Cos(_vibe * 1.3f) * k);
            }
            if (_fireMat != null && s.Machine != null)
            {
                bool burning = s.Machine.FuelBurnRemaining > 0f && s.Machine.Status == MachineStatus.Working;
                float flicker = burning ? 2.2f + Mathf.PerlinNoise(Time.time * 6f, Id.Value) * 1.4f : 0f;
                MaterialLibrary.SetEmission(_fireMat, new Color(1f, 0.45f, 0.1f) * flicker);
            }
            if (_indicator != null && s.Machine != null)
            {
                _indicator.localScale = s.Machine.Status == MachineStatus.Working ? new Vector3(0.2f, 0.1f + Mathf.PingPong(Time.time, 0.05f), 0.2f) : new Vector3(0.2f, 0.1f, 0.2f);
            }
            if (_light != null)
            {
                bool on = s.Power != null && s.Power.Powered && s.Enabled;
                _light.intensity = Mathf.MoveTowards(_light.intensity, on ? 2.2f : 0f, dt * 6f);
                MaterialLibrary.SetEmission(_lampMat, on ? new Color(1f, 0.85f, 0.55f) * 2.5f : Color.black);
            }
        }

        public string Prompt(GameController gc)
        {
            if (!gc.Session.World.Structures.TryGetValue(Id, out Structure s)) return null;
            if (!s.IsComplete)
            {
                List<ItemAmount> remaining = gc.Session.Construction.RemainingCost(s);
                if (remaining.Count == 0) return $"[Hold E] Build {Def.Name}  {gc.Session.Construction.WorkProgress01(s) * 100:0}%";
                var parts = new List<string>();
                foreach (ItemAmount a in remaining)
                {
                    int have = gc.Session.Player.Inventory.Available(a.ItemId);
                    parts.Add($"{a.Quantity} {gc.Session.Content.Items.NameOf(a.ItemId)}{(have > 0 ? $" (have {have})" : "")}");
                }
                return $"[Hold E] Deliver & build {Def.Name} — needs {string.Join(", ", parts)}";
            }
            if (!s.PlayerBuilt && s.Storage != null) return $"[E] Search {Def.Name}{(s.Searched ? (s.Storage.IsEmpty ? " (empty)" : " (searched)") : "")}";
            if (s.Storage != null) return $"[E] Open {Def.Name}";
            if (s.Machine != null) return $"[E] Operate {Def.Name} — {s.Machine.Status}: {s.Machine.StatusDetail}";
            if (s.Generator != null) return $"[E] Generator — {s.Generator.Status}";
            if (!string.IsNullOrEmpty(Def.CraftingStation)) return $"[E] Use {Def.Name}";
            if (Def.IsBed) return gc.Session.Player.SpawnStructureId == s.Id ? $"{Def.Name} (your respawn point)" : $"[E] Rest — set respawn point";
            if (Def.Light != null) return $"[E] Switch {(s.Enabled ? "off" : "on")} {Def.Name} — {s.Power?.Reason}";
            if (s.Battery != null) return $"{Def.Name} — {s.Battery.StoredJoules / 1000:0}/{Def.Battery.CapacityKJ:0} kJ";
            if (Def.PowerNode != null) return $"{Def.Name} — network {s.Power?.NetworkId}";
            return Def.Name;
        }

        public void Interact(GameController gc)
        {
            if (!gc.Session.World.Structures.TryGetValue(Id, out Structure s)) return;
            if (!s.IsComplete)
            {
                InteractHeld(gc, 0.25f);
                return;
            }
            if (s.Storage != null) gc.Ui.OpenContainer(s.Storage.Id, Def.Name);
            else if (s.Machine != null || s.Generator != null || s.Battery != null) gc.Ui.OpenMachine(s.Id);
            else if (!string.IsNullOrEmpty(Def.CraftingStation)) gc.Ui.OpenInventory(true);
            else if (Def.IsBed)
            {
                OpResult r = gc.Session.SetSpawnPoint(s.Id);
                gc.Toast(r.Message, r.Ok ? Severity.Success : Severity.Warning);
            }
            else if (Def.Light != null)
            {
                OpResult r = gc.Session.ToggleEnabled(s.Id);
                gc.Toast(r.Message);
            }
        }

        public void InteractHeld(GameController gc, float dt)
        {
            OpResult r = gc.Session.BuildStep(Id, dt);
            gc.Player.Rig.PlaySwingIfIdle();
            if (!r.Ok && !string.IsNullOrEmpty(r.Message)) gc.ToastThrottled(r.Message, Severity.Warning);
        }
    }
}
