using System.Collections.Generic;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Crafting;
using Afterfall.Domain.Events;
using Afterfall.Domain.Industry;
using Afterfall.Domain.Power;
using Afterfall.Domain.World;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation.UI
{
    /// <summary>Strategic overview: resources, energy, threats, known places, selection orders and world labels.</summary>
    public sealed class TacticalPanel
    {
        private readonly UiManager _ui;
        private readonly GameController _gc;
        private readonly VisualElement _left, _right, _selection, _labels;
        private readonly Label _resources, _power, _threat, _places, _selTitle, _selStatus, _selDetail;
        private readonly VisualElement _selActions, _recipeList;
        private EntityId _builtFor = EntityId.None;
        private float _slow;
        private readonly List<Label> _labelPool = new List<Label>();

        public VisualElement Root { get; }

        public TacticalPanel(UiManager ui, GameController gc)
        {
            _ui = ui;
            _gc = gc;
            Root = UiKit.Div("layer");
            Root.pickingMode = PickingMode.Ignore;
            _labels = Root.Put(UiKit.Div("layer"));
            _labels.pickingMode = PickingMode.Ignore;

            _left = Root.Put(UiKit.Div("panel", "tactical-left"));
            _left.Add(UiKit.Text("Settlement", "title"));
            _left.Add(UiKit.Text("Stockpile (backpack + your storage)", "subtitle"));
            _resources = _left.Put(UiKit.Text("", "wrap"));
            _left.Add(UiKit.Text("Electricity", "subtitle"));
            _power = _left.Put(UiKit.Text("", "wrap"));
            _left.Add(UiKit.Text("Threats", "subtitle"));
            _threat = _left.Put(UiKit.Text("", "wrap"));
            _left.Add(UiKit.Text("Known locations", "subtitle"));
            _places = _left.Put(UiKit.Text("", "muted"));

            _right = Root.Put(UiKit.Div("panel", "tactical-right"));
            _selection = _right;
            _selTitle = _right.Put(UiKit.Text("Nothing selected", "title"));
            _selStatus = _right.Put(UiKit.Text("Click one of your structures to inspect it and give orders.", "wrap"));
            _selDetail = _right.Put(UiKit.Text("", "muted"));
            _selActions = _right.Put(UiKit.Div("row"));
            _selActions.style.flexWrap = Wrap.Wrap;
            _recipeList = _right.Put(UiKit.Div("col"));
        }

        public void Refresh()
        {
            bool on = _gc.CameraRig.TacticalActive && !_ui.AnyWindowOpen;
            UiKit.Show(Root, on);
            if (!on) return;
            UpdateLabels();
            _slow -= Time.unscaledDeltaTime;
            if (_slow > 0f) return;
            _slow = 0.25f;
            RefreshOverview();
            RefreshSelection();
        }

        private void RefreshOverview()
        {
            Dictionary<string, int> totals = _gc.Session.ResourceOverview();
            _resources.text = totals.Count == 0 ? "Nothing stored yet." : string.Join("\n", totals.OrderByDescending(k => k.Value).Take(12).Select(k => $"{_gc.Session.Content.Items.NameOf(k.Key)}: {k.Value}"));

            IReadOnlyList<PowerNetworkInfo> nets = _gc.Session.Power.Networks;
            var lines = new List<string>();
            foreach (PowerNetworkInfo n in nets)
            {
                if (n.Members.Count == 0) continue;
                string storage = n.StorageCapacityJoules > 0 ? $", battery {n.StoredJoules / n.StorageCapacityJoules * 100:0}%" : "";
                lines.Add($"Grid {n.Id}: supply {UiKit.Watts(n.CapacityWatts)} / demand {UiKit.Watts(n.DemandWatts)}{storage}\n  {n.Status}");
            }
            _power.text = lines.Count == 0 ? "No electrical network yet." : string.Join("\n", lines);
            UiKit.SetClass(_power, "warn", nets.Any(n => n.UnpoweredConsumers > 0));

            ThreatState t = _gc.Session.World.Threat;
            int seen = _gc.Tactical.SeenZombies.Count;
            string raid = t.PendingRaidAt > _gc.Session.Now ? $"\n!! Group approaching — arrives in {t.PendingRaidAt - _gc.Session.Now:0}s" : "";
            _threat.text = $"Noise/attention: {t.Heat:0}%\nInfected recently seen: {seen}{raid}";

            var places = new List<string>();
            foreach (PoiInfo p in _gc.Session.World.Layout.Pois)
                if (_gc.Session.World.Exploration.DiscoveredPois.Contains(p.Id)) places.Add($"{p.Name} — risk {p.Risk}");
            _places.text = places.Count == 0 ? "Explore to discover locations." : string.Join("\n", places);
        }

        private void RefreshSelection()
        {
            EntityId id = _gc.Tactical.Selected;
            if (!id.IsValid || !_gc.Session.World.Structures.TryGetValue(id, out Structure s))
            {
                _selTitle.text = _gc.Tactical.LinkSource.IsValid ? "Running a cable…" : "Nothing selected";
                _selStatus.text = _gc.Tactical.LinkSource.IsValid ? "Click the structure to connect to (RMB cancels)." : "Click one of your structures to inspect it and give orders. Unexplored areas stay hidden.";
                _selDetail.text = "";
                if (_builtFor.IsValid)
                {
                    _selActions.Clear();
                    _recipeList.Clear();
                    _builtFor = EntityId.None;
                }
                return;
            }
            BuildingDefinition def = _gc.Session.Content.Building(s.DefId);
            _selTitle.text = def.Name;
            _selStatus.text = TacticalController.StatusLine(_gc, s);
            UiKit.SetClass(_selStatus, "bad", TacticalController.IsBlocked(s));
            var detail = new List<string> { $"Condition {s.Health:0}/{def.MaxHealth:0}" };
            if (s.Power != null) detail.Add($"Power: {(s.Power.Powered ? "connected and powered" : s.Power.Reason)} (grid {s.Power.NetworkId})");
            if (!s.IsComplete)
            {
                var remaining = _gc.Session.Construction.RemainingCost(s);
                detail.Add(remaining.Count == 0 ? "All materials delivered — someone must do the construction work." : "Still needs: " + string.Join(", ", remaining.Select(a => $"{a.Quantity} {_gc.Session.Content.Items.NameOf(a.ItemId)}")));
                detail.Add("Orders are carried out by people: walk there and hold E to build.");
            }
            if (s.Machine != null) detail.Add($"Cycles completed: {s.Machine.CompletedCycles}");
            _selDetail.text = string.Join("\n", detail);

            if (_builtFor == id) return;
            _builtFor = id;
            _selActions.Clear();
            _recipeList.Clear();
            if (s.PlayerBuilt && (s.Machine != null || s.Generator != null || def.Light != null))
                _selActions.Add(UiKit.Btn("Switch on/off", () => Toast(_gc.Session.ToggleEnabled(id)), "btn--small"));
            if (s.Power != null && s.IsComplete)
            {
                _selActions.Add(UiKit.Btn("Connect cable…", () => _gc.Tactical.BeginLink(id), "btn--small"));
                _selActions.Add(UiKit.Btn("Cut cables", () =>
                {
                    foreach (PowerLink l in _gc.Session.World.PowerLinks.Where(x => x.Involves(id)).ToList()) _gc.Session.DisconnectPower(l.A, l.B);
                    _gc.Toast("Cables removed.");
                }, "btn--small"));
            }
            _selActions.Add(UiKit.Btn("Focus camera", () => _gc.CameraRig.Tactical.Focus(s.Position.ToUnity(), 0f), "btn--small"));
            if (s.PlayerBuilt)
            {
                // Physical actions: the session enforces that the survivor is standing next to it.
                _selActions.Add(UiKit.Btn("Repair", () => Toast(_gc.Session.Repair(id)), "btn--small"));
                _selActions.Add(UiKit.Btn(s.IsComplete ? "Demolish" : "Cancel blueprint", () => { Toast(_gc.Session.Demolish(id)); _gc.Tactical.Select(EntityId.None); }, "btn--small", "btn--danger"));
            }
            if (s.Machine != null && def.Machine != null)
            {
                _recipeList.Add(UiKit.Text("Production order", "subtitle"));
                foreach (RecipeDefinition r in _gc.Session.Content.RecipesForStation(def.Machine.Station))
                {
                    bool unlocked = _gc.Session.World.Tech.IsUnlocked(r.RequiredTech);
                    string label = $"{r.Name}  ({string.Join(" + ", r.Inputs.Select(a => $"{a.Quantity} {_gc.Session.Content.Items.NameOf(a.ItemId)}"))} -> {string.Join(", ", r.Outputs.Select(a => $"{a.Quantity} {_gc.Session.Content.Items.NameOf(a.ItemId)}"))})";
                    Button b = UiKit.Btn(unlocked ? label : "Locked: " + r.Name, () => { Toast(_gc.Session.SetMachineRecipe(id, r.Id)); _builtFor = EntityId.None; }, "btn--small");
                    if (s.Machine.RecipeId == r.Id) b.AddToClassList("btn--selected");
                    if (!unlocked) b.AddToClassList("btn--disabled");
                    _recipeList.Add(b);
                }
            }
        }

        private void Toast(Afterfall.Core.OpResult r) => _gc.Toast(r.Message, r.Ok ? Severity.Success : Severity.Warning);

        private void UpdateLabels()
        {
            Camera cam = _gc.CameraRig.Camera;
            IPanel panel = Root.panel;
            if (panel == null) return;
            int used = 0;
            Label Next(string text, string cls)
            {
                Label l;
                if (used < _labelPool.Count) l = _labelPool[used];
                else
                {
                    l = UiKit.Text("", "overlay-label");
                    l.pickingMode = PickingMode.Ignore;
                    _labels.Add(l);
                    _labelPool.Add(l);
                }
                used++;
                l.text = text;
                foreach (string c in new[] { "overlay-label--bad", "overlay-label--good", "overlay-label--threat" }) l.RemoveFromClassList(c);
                if (cls != null) l.AddToClassList(cls);
                UiKit.Show(l, true);
                return l;
            }
            void Place(Label l, Vector3 world)
            {
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z < 0f)
                {
                    UiKit.Show(l, false);
                    return;
                }
                Vector2 p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(sp.x, Screen.height - sp.y));
                l.style.left = p.x;
                l.style.top = p.y;
            }

            Place(Next("You", "overlay-label--good"), _gc.Player.transform.position + Vector3.up * 2.5f);
            foreach (Structure s in _gc.Session.World.Structures.Values)
            {
                if (!s.PlayerBuilt || used > 60) continue;
                BuildingDefinition d = _gc.Session.Content.Building(s.DefId);
                bool important = s.Machine != null || s.Generator != null || !s.IsComplete;
                if (!important) continue;
                string text = !s.IsComplete ? $"{d.Name} (blueprint)" : s.Machine != null ? $"{d.Name}: {s.Machine.Status}" : $"{d.Name}: {(s.Generator.Running ? UiKit.Watts(s.Generator.OutputWatts) : "off")}";
                bool bad = TacticalController.IsBlocked(s) || (s.Generator != null && !s.Generator.Running);
                Place(Next(text, bad ? "overlay-label--bad" : null), s.Position.ToUnity() + Vector3.up * (d.SizeY + 1f));
            }
            foreach (PoiInfo p in _gc.Session.World.Layout.Pois)
                if (_gc.Session.World.Exploration.DiscoveredPois.Contains(p.Id)) Place(Next(p.Name, null), p.Center.ToUnity() + Vector3.up * 12f);
            foreach (KeyValuePair<EntityId, float> kv in _gc.Tactical.SeenZombies)
            {
                ZombieView v = _gc.Sync.ZombieViewOf(kv.Key);
                if (v == null || used > 90) continue;
                Place(Next("!", "overlay-label--threat"), v.transform.position + Vector3.up * 2.4f);
            }
            for (int i = used; i < _labelPool.Count; i++) UiKit.Show(_labelPool[i], false);
        }
    }
}
