using System.Collections.Generic;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Crafting;
using Afterfall.Domain.Events;
using Afterfall.Domain.Industry;
using Afterfall.Domain.Items;
using Afterfall.Domain.Power;
using Afterfall.Domain.World;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation.UI
{
    /// <summary>Shared window chrome and a backpack grid with selection/move/split/use semantics.</summary>
    public abstract class WindowBase : IWindow
    {
        protected readonly UiManager Ui;
        protected readonly GameController Gc;

        protected WindowBase(UiManager ui, GameController gc, string title)
        {
            Ui = ui;
            Gc = gc;
            Root = UiKit.Div("window", "window-center");
            var header = Root.Put(UiKit.Div("row"));
            header.Add(UiKit.Text(title, "title"));
            header.Add(UiKit.Div("spacer"));
            header.Add(UiKit.Btn("Close (Esc)", () => Ui.CloseWindow(), "btn--small"));
        }

        public VisualElement Root { get; }
        public abstract void Refresh();
        public virtual void OnClose() { }

        protected void Report(OpResult r)
        {
            if (!string.IsNullOrEmpty(r.Message)) Gc.Toast(r.Message, r.Ok ? Severity.Info : Severity.Warning);
        }

        protected static ItemSlot[] Grid(VisualElement parent, int count, int hotbar, System.Action<ItemSlot, PointerDownEvent> onClick)
        {
            var grid = parent.Put(UiKit.Div("grid"));
            var slots = new ItemSlot[count];
            for (int i = 0; i < count; i++)
            {
                var s = new ItemSlot(i, i < hotbar ? (i + 1).ToString() : null);
                if (i < hotbar) s.AddToClassList("slot--hotbar");
                s.Clicked = onClick;
                slots[i] = grid.Put(s);
            }
            return slots;
        }

        protected static void Fill(ItemSlot[] slots, Inventory inv, ItemCatalog items)
        {
            for (int i = 0; i < slots.Length; i++) slots[i].Set(i < inv.SlotCount ? inv.GetSlot(i) : ItemStack.Empty, items);
        }

        protected string Name(string itemId) => Gc.Session.Content.Items.NameOf(itemId);
    }

    /// <summary>Backpack, item details and crafting.</summary>
    public sealed class InventoryWindow : WindowBase
    {
        private readonly ItemSlot[] _slots;
        private readonly Label _weight, _detailTitle, _detailText;
        private readonly VisualElement _detailActions, _recipeList, _queue;
        private readonly Label _recipeInfo;
        private readonly Button _craftButton;
        private int _selected = -1;
        private string _recipe;
        private readonly Dictionary<string, Button> _recipeButtons = new Dictionary<string, Button>();
        private int _queueCount = -1;

        public InventoryWindow(UiManager ui, GameController gc, bool craftingFocus) : base(ui, gc, "Backpack & Crafting")
        {
            var body = Root.Put(UiKit.Div("row", "grow"));
            body.style.alignItems = Align.FlexStart;

            var left = body.Put(UiKit.Div("col"));
            left.style.width = 600;
            left.Add(UiKit.Text("Click to select · click another slot to move · right-click to use/equip · shift-click to split · first row = hotbar", "muted"));
            _slots = Grid(left, PlayerState.BackpackSlots, PlayerState.HotbarSize, OnSlot);
            _weight = left.Put(UiKit.Text("", "muted"));
            _detailTitle = left.Put(UiKit.Text("", "subtitle"));
            _detailText = left.Put(UiKit.Text("", "wrap"));
            _detailActions = left.Put(UiKit.Div("row"));

            var right = body.Put(UiKit.Div("col", "grow"));
            right.style.marginLeft = 16;
            right.Add(UiKit.Text("Crafting", "subtitle"));
            right.Add(UiKit.Text(craftingFocus ? "You are at a workbench: station recipes are available." : "Hand recipes work anywhere; station recipes need the station nearby.", "muted"));
            var scroll = right.Put(new ScrollView());
            scroll.style.height = 300;
            _recipeList = scroll;
            foreach (RecipeDefinition r in gc.Session.PlayerRecipes())
            {
                RecipeDefinition rr = r;
                Button b = UiKit.Btn("", () => { _recipe = rr.Id; Refresh(); }, "btn--small");
                b.style.unityTextAlign = TextAnchor.MiddleLeft;
                _recipeButtons[r.Id] = _recipeList.Put(b);
            }
            _recipeInfo = right.Put(UiKit.Text("Select a recipe.", "wrap"));
            _craftButton = right.Put(UiKit.Btn("Craft", () =>
            {
                if (_recipe != null) Report(Gc.Session.Craft(_recipe));
            }, "btn--primary"));
            right.Add(UiKit.Text("Queue", "subtitle"));
            _queue = right.Put(UiKit.Div("col"));
        }

        private void OnSlot(ItemSlot slot, PointerDownEvent e)
        {
            Inventory inv = Gc.Session.Player.Inventory;
            if (e.button == 1)
            {
                Report(Gc.Session.UseItem(slot.Index));
                return;
            }
            if (e.shiftKey)
            {
                ItemStack st = inv.GetSlot(slot.Index);
                if (st.Quantity > 1 && !inv.SplitSlot(slot.Index, st.Quantity / 2)) Gc.Toast("No free slot to split into.", Severity.Warning);
                return;
            }
            if (_selected >= 0 && _selected != slot.Index && !inv.GetSlot(_selected).IsEmpty)
            {
                inv.MoveSlot(_selected, slot.Index);
                _selected = -1;
            }
            else _selected = _selected == slot.Index ? -1 : slot.Index;
            Refresh();
        }

        public override void Refresh()
        {
            Inventory inv = Gc.Session.Player.Inventory;
            ItemCatalog items = Gc.Session.Content.Items;
            Fill(_slots, inv, items);
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].SetSelected(i == _selected);
                _slots[i].SetActive(i == Gc.Session.Player.SelectedHotbar);
            }
            _weight.text = $"Weight {inv.TotalWeight:0.0} / {PlayerState.CarryMaxKg:0} kg (comfortable up to {PlayerState.CarryComfortKg:0} kg)";
            RefreshDetail(inv, items);
            RefreshRecipes(inv);
        }

        private void RefreshDetail(Inventory inv, ItemCatalog items)
        {
            ItemStack st = _selected >= 0 ? inv.GetSlot(_selected) : ItemStack.Empty;
            if (st.IsEmpty)
            {
                _detailTitle.text = "";
                _detailText.text = "Select an item to see details.";
                _detailActions.Clear();
                return;
            }
            ItemDefinition d = items.Get(st.ItemId);
            _detailTitle.text = $"{d.Name} ×{st.Quantity}";
            var lines = new List<string> { d.Description, $"Category: {d.Category} · {d.Weight:0.##} kg each" };
            if (d.HasDurability) lines.Add($"Durability {st.Durability}/{d.MaxDurability}");
            if (d.Consumable != null)
            {
                var fx = new List<string>();
                if (d.Consumable.Hunger != 0) fx.Add($"food {d.Consumable.Hunger:+0;-0}");
                if (d.Consumable.Thirst != 0) fx.Add($"water {d.Consumable.Thirst:+0;-0}");
                if (d.Consumable.Health != 0) fx.Add($"health {d.Consumable.Health:+0;-0}");
                lines.Add("Effects: " + string.Join(", ", fx));
            }
            if (d.Tool != null) lines.Add($"Tool: {d.Tool.Type} · damage {d.Tool.Damage:0} · gather ×{d.Tool.GatherPower:0.#}");
            if (d.BurnSeconds > 0) lines.Add($"Burns for {d.BurnSeconds:0}s in a furnace");
            if (d.EnergyKJ > 0) lines.Add($"Generator fuel: {d.EnergyKJ:0} kJ");
            _detailText.text = string.Join("\n", lines);
            if (_detailActions.childCount == 0 || (string)_detailActions.userData != st.ItemId + _selected)
            {
                _detailActions.Clear();
                _detailActions.userData = st.ItemId + _selected;
                int slot = _selected;
                if (d.Consumable != null || d.Tool != null) _detailActions.Add(UiKit.Btn(d.Consumable != null ? "Use" : "Equip", () => Report(Gc.Session.UseItem(slot)), "btn--small"));
                _detailActions.Add(UiKit.Btn("Drop 1", () => Report(Gc.Session.DropFromSlot(slot, 1)), "btn--small"));
                _detailActions.Add(UiKit.Btn("Drop stack", () => { Report(Gc.Session.DropFromSlot(slot, 9999)); _selected = -1; }, "btn--small"));
            }
        }

        private void RefreshRecipes(Inventory inv)
        {
            foreach (KeyValuePair<string, Button> kv in _recipeButtons)
            {
                RecipeDefinition r = Gc.Session.Content.Recipe(kv.Key);
                CraftCheck c = Gc.Session.CheckRecipe(kv.Key);
                string where = r.IsHandRecipe ? "hand" : CraftingRules.StationName(r.Station);
                kv.Value.text = $"{(c.CanCraft ? "+ " : c.Locked ? "[locked] " : "- ")}{r.Name}  ({where})";
                UiKit.SetClass(kv.Value, "btn--selected", kv.Key == _recipe);
                UiKit.SetClass(kv.Value, "btn--disabled", !c.CanCraft);
            }
            if (_recipe != null)
            {
                RecipeDefinition r = Gc.Session.Content.Recipe(_recipe);
                CraftCheck c = Gc.Session.CheckRecipe(_recipe);
                string ins = string.Join(", ", r.Inputs.Select(a => $"{Name(a.ItemId)} {Mathf.Min(inv.Available(a.ItemId), a.Quantity)}/{a.Quantity}"));
                string outs = string.Join(", ", r.Outputs.Select(a => $"{a.Quantity} {Name(a.ItemId)}"));
                _recipeInfo.text = $"{r.Name}: {ins}  ->  {outs}  ({r.Duration:0.#}s)\n{(c.CanCraft ? "Ready to craft." : c.Reason)}";
                UiKit.SetClass(_recipeInfo, "bad", !c.CanCraft);
                UiKit.SetClass(_craftButton, "btn--disabled", !c.CanCraft);
            }
            List<CraftJob> jobs = Gc.Session.Player.Crafting.Jobs;
            if (jobs.Count != _queueCount)
            {
                _queueCount = jobs.Count;
                _queue.Clear();
                for (int i = 0; i < jobs.Count; i++)
                {
                    int idx = i;
                    var row = _queue.Put(UiKit.Div("row"));
                    row.Add(UiKit.Text(Gc.Session.Content.Recipe(jobs[i].RecipeId)?.Name ?? jobs[i].RecipeId, "grow"));
                    row.Add(UiKit.Btn("Cancel", () => { Report(Gc.Session.CancelCraft(idx)); _queueCount = -1; }, "btn--small"));
                }
                if (jobs.Count == 0) _queue.Add(UiKit.Text("Nothing queued.", "muted"));
            }
        }
    }

    /// <summary>Two-pane transfer between a container (storage, loot, drop) and the backpack.</summary>
    public sealed class ContainerWindow : WindowBase
    {
        private readonly EntityId _inventoryId;
        private readonly ItemSlot[] _containerSlots, _bagSlots;
        private readonly Label _status;

        public ContainerWindow(UiManager ui, GameController gc, EntityId inventoryId, string title) : base(ui, gc, title)
        {
            _inventoryId = inventoryId;
            Inventory inv = gc.Session.World.FindInventory(inventoryId);
            Root.Add(UiKit.Text("Click a stack to move it to the other side (shift-click moves half).", "muted"));
            var body = Root.Put(UiKit.Div("row"));
            body.style.alignItems = Align.FlexStart;
            var left = body.Put(UiKit.Div("col"));
            left.style.width = 600;
            var head = left.Put(UiKit.Div("row"));
            head.Add(UiKit.Text(title, "subtitle"));
            head.Add(UiKit.Btn("Take all", () => Report(Gc.Session.TakeAll(_inventoryId)), "btn--small", "btn--primary"));
            _containerSlots = Grid(left, inv?.SlotCount ?? 0, 0, (s, e) => Move(_inventoryId, Gc.Session.Player.Inventory.Id, s.Index, e.shiftKey));
            var right = body.Put(UiKit.Div("col"));
            right.style.marginLeft = 24;
            right.style.width = 600;
            right.Add(UiKit.Text("Backpack", "subtitle"));
            _bagSlots = Grid(right, PlayerState.BackpackSlots, PlayerState.HotbarSize, (s, e) => Move(Gc.Session.Player.Inventory.Id, _inventoryId, s.Index, e.shiftKey));
            _status = Root.Put(UiKit.Text("", "muted"));
        }

        private void Move(EntityId from, EntityId to, int slot, bool half)
        {
            Inventory src = Gc.Session.World.FindInventory(from);
            if (src == null) return;
            ItemStack st = src.GetSlot(slot);
            if (st.IsEmpty) return;
            OpResult r = Gc.Session.Transfer(from, slot, to, half ? Mathf.Max(1, st.Quantity / 2) : st.Quantity);
            if (!r.Ok || !string.IsNullOrEmpty(r.Message)) Report(r);
            Refresh();
        }

        public override void Refresh()
        {
            Inventory inv = Gc.Session.World.FindInventory(_inventoryId);
            if (inv == null || !Gc.Session.CanAccess(_inventoryId, out _))
            {
                Ui.CloseWindow();
                return;
            }
            Fill(_containerSlots, inv, Gc.Session.Content.Items);
            Fill(_bagSlots, Gc.Session.Player.Inventory, Gc.Session.Content.Items);
            _status.text = $"Backpack {Gc.Session.CarriedWeight:0.0}/{PlayerState.CarryMaxKg:0} kg";
        }
    }

    /// <summary>Machine, generator and battery control panel.</summary>
    public sealed class MachineWindow : WindowBase
    {
        private readonly EntityId _id;
        private readonly Label _status, _detail, _power, _stats;
        private readonly Bar _progress;
        private readonly ItemSlot[] _input, _fuel, _output, _bag;
        private readonly VisualElement _recipes;
        private string _recipeShown;
        private readonly Button _toggle;

        public MachineWindow(UiManager ui, GameController gc, EntityId structureId) : base(ui, gc, Title(gc, structureId))
        {
            _id = structureId;
            gc.Session.World.Structures.TryGetValue(structureId, out Structure s);
            BuildingDefinition def = gc.Session.Content.Building(s.DefId);
            Root.Add(UiKit.Text(def.Description, "muted"));
            var statusRow = Root.Put(UiKit.Div("row"));
            _status = statusRow.Put(UiKit.Text("", "subtitle"));
            statusRow.Add(UiKit.Div("spacer"));
            _toggle = statusRow.Put(UiKit.Btn("Switch off", () => { Report(Gc.Session.ToggleEnabled(_id)); }, "btn--small"));
            _detail = Root.Put(UiKit.Text("", "wrap"));
            _progress = Root.Put(new Bar(new Color(0.8f, 0.6f, 0.25f)));
            _power = Root.Put(UiKit.Text("", "muted"));
            _stats = Root.Put(UiKit.Text("", "muted"));

            var body = Root.Put(UiKit.Div("row"));
            body.style.alignItems = Align.FlexStart;
            var left = body.Put(UiKit.Div("col"));
            left.style.width = 520;
            if (s.Machine != null)
            {
                left.Add(UiKit.Text("Recipe", "subtitle"));
                _recipes = left.Put(UiKit.Div("col"));
                left.Add(UiKit.Text("Input", "subtitle"));
                _input = Grid(left, s.Machine.Input.SlotCount, 0, (sl, e) => Take(s.Machine.Input, sl.Index));
                if (s.Machine.Fuel != null)
                {
                    left.Add(UiKit.Text("Fuel (wood, planks, charcoal)", "subtitle"));
                    _fuel = Grid(left, s.Machine.Fuel.SlotCount, 0, (sl, e) => Take(s.Machine.Fuel, sl.Index));
                }
                var outHead = left.Put(UiKit.Div("row"));
                outHead.Add(UiKit.Text("Output", "subtitle"));
                outHead.Add(UiKit.Btn("Collect all", () => Report(Gc.Session.TakeAll(s.Machine.Output.Id)), "btn--small", "btn--primary"));
                _output = Grid(left, s.Machine.Output.SlotCount, 0, (sl, e) => Take(s.Machine.Output, sl.Index));
            }
            else if (s.Generator != null)
            {
                left.Add(UiKit.Text("Fuel (fuel cans or charcoal)", "subtitle"));
                _fuel = Grid(left, s.Generator.Fuel.SlotCount, 0, (sl, e) => Take(s.Generator.Fuel, sl.Index));
            }
            var right = body.Put(UiKit.Div("col"));
            right.style.marginLeft = 20;
            right.style.width = 600;
            right.Add(UiKit.Text("Backpack — click to load into the machine", "subtitle"));
            _bag = Grid(right, PlayerState.BackpackSlots, PlayerState.HotbarSize, (sl, e) => Load(sl.Index, e.shiftKey));
        }

        private static string Title(GameController gc, EntityId id)
        {
            return gc.Session.World.Structures.TryGetValue(id, out Structure s) ? gc.Session.Content.Building(s.DefId).Name : "Machine";
        }

        private void Take(Inventory from, int slot)
        {
            ItemStack st = from.GetSlot(slot);
            if (st.IsEmpty) return;
            Report(Gc.Session.Transfer(from.Id, slot, Gc.Session.Player.Inventory.Id, st.Quantity));
            Refresh();
        }

        private void Load(int slot, bool half)
        {
            if (!Gc.Session.World.Structures.TryGetValue(_id, out Structure s)) return;
            Inventory bag = Gc.Session.Player.Inventory;
            ItemStack st = bag.GetSlot(slot);
            if (st.IsEmpty) return;
            int qty = half ? Mathf.Max(1, st.Quantity / 2) : st.Quantity;
            Inventory target = null;
            if (s.Machine != null)
            {
                RecipeDefinition r = Gc.Session.Content.Recipe(s.Machine.RecipeId);
                bool isInput = s.Machine.Input.Accepts(st.ItemId) && (r == null || r.Inputs.Any(a => a.ItemId == st.ItemId));
                if (isInput) target = s.Machine.Input;
                else if (s.Machine.Fuel != null && s.Machine.Fuel.Accepts(st.ItemId)) target = s.Machine.Fuel;
                else if (s.Machine.Input.Accepts(st.ItemId)) target = s.Machine.Input;
            }
            else if (s.Generator != null) target = s.Generator.Fuel;
            if (target == null)
            {
                Gc.Toast($"{Name(st.ItemId)} is not used by this machine.", Severity.Warning);
                return;
            }
            Report(Gc.Session.Transfer(bag.Id, slot, target.Id, qty));
            Refresh();
        }

        public override void Refresh()
        {
            if (!Gc.Session.World.Structures.TryGetValue(_id, out Structure s))
            {
                Ui.CloseWindow();
                return;
            }
            float dist = Vector3.Distance(Gc.Player.transform.position, s.Position.ToUnity());
            if (dist > GameSessionRange(s))
            {
                Ui.CloseWindow();
                return;
            }
            BuildingDefinition def = Gc.Session.Content.Building(s.DefId);
            ItemCatalog items = Gc.Session.Content.Items;
            _toggle.text = s.Enabled ? "Switch off" : "Switch on";
            if (s.Machine != null)
            {
                MachineState m = s.Machine;
                _status.text = $"Status: {m.Status}";
                _detail.text = m.StatusDetail;
                UiKit.SetClass(_detail, "bad", m.Status != MachineStatus.Working && m.Status != MachineStatus.Idle);
                RecipeDefinition r = Gc.Session.Content.Recipe(m.RecipeId);
                _progress.Set(r == null ? 0f : m.Progress / r.Duration, r == null ? "No recipe" : $"{r.Name} — {m.Progress:0.0}/{r.Duration:0.#}s");
                string fuel = m.Fuel != null ? $" · fire burns {m.FuelBurnRemaining:0}s more" : "";
                _power.text = def.Machine.PowerWatts > 0 ? $"Needs {UiKit.Watts(def.Machine.PowerWatts)} while working · {(s.Power.Powered ? "powered" : s.Power.Reason)}{fuel}" : $"Burns fuel while working{fuel}";
                double perMin = r != null ? 60.0 / r.Duration : 0;
                _stats.text = $"Cycles completed: {m.CompletedCycles}" + (r != null ? $" · max rate {perMin:0.#} cycles/min" : "") + (def.Machine.AutoTransferRange > 0 ? $" · pulls inputs from / pushes outputs to storage crates within {def.Machine.AutoTransferRange:0.#} m" : "");
                Fill(_input, m.Input, items);
                if (_fuel != null) Fill(_fuel, m.Fuel, items);
                Fill(_output, m.Output, items);
                if (_recipeShown != m.RecipeId + Gc.Session.World.Tech.Unlocked.Count)
                {
                    _recipeShown = m.RecipeId + Gc.Session.World.Tech.Unlocked.Count;
                    _recipes.Clear();
                    foreach (RecipeDefinition rr in Gc.Session.Content.RecipesForStation(def.Machine.Station))
                    {
                        RecipeDefinition rec = rr;
                        bool unlocked = Gc.Session.World.Tech.IsUnlocked(rr.RequiredTech);
                        string label = $"{rr.Name}: {string.Join(" + ", rr.Inputs.Select(a => $"{a.Quantity} {items.NameOf(a.ItemId)}"))} -> {string.Join(", ", rr.Outputs.Select(a => $"{a.Quantity} {items.NameOf(a.ItemId)}"))} ({rr.Duration:0.#}s)";
                        Button b = UiKit.Btn(unlocked ? label : "Locked — " + Gc.Session.Tech.DescribeRequirements(rr.RequiredTech), () => { Report(Gc.Session.SetMachineRecipe(_id, rec.Id)); _recipeShown = null; }, "btn--small");
                        b.style.unityTextAlign = TextAnchor.MiddleLeft;
                        if (m.RecipeId == rr.Id) b.AddToClassList("btn--selected");
                        if (!unlocked) b.AddToClassList("btn--disabled");
                        _recipes.Add(b);
                    }
                }
            }
            else if (s.Generator != null)
            {
                GeneratorState g = s.Generator;
                _status.text = g.Running ? "Status: Running" : "Status: Stopped";
                _detail.text = g.Status;
                UiKit.SetClass(_detail, "bad", !g.Running);
                double unit = 0;
                for (int i = 0; i < g.Fuel.SlotCount; i++)
                {
                    ItemStack st = g.Fuel.GetSlot(i);
                    if (!st.IsEmpty) unit += items.Get(st.ItemId).EnergyKJ * st.Quantity;
                }
                _progress.Set(Mathf.Clamp01((float)(g.BufferJoules / 600000.0)), $"Burner {g.BufferJoules / 1000:0} kJ · stock {unit:0} kJ");
                PowerNetworkInfo net = Gc.Session.Power.NetworkOf(s.Id);
                _power.text = net == null ? "Not connected." : $"Grid {net.Id}: output {UiKit.Watts(g.OutputWatts)} of {UiKit.Watts(def.Generator.CapacityWatts)} · demand {UiKit.Watts(net.DemandWatts)} · {net.Status}";
                double seconds = g.OutputWatts > 1f ? (g.BufferJoules + unit * 1000) / g.OutputWatts : 0;
                _stats.text = g.OutputWatts > 1f ? $"Fuel lasts about {seconds / 60:0.#} min at the current load. Generators are loud — the infected hear them." : "Idle generators burn no fuel.";
                Fill(_fuel, g.Fuel, items);
            }
            else if (s.Battery != null)
            {
                _status.text = "Battery bank";
                double cap = def.Battery.CapacityKJ * 1000.0;
                _progress.Set((float)(s.Battery.StoredJoules / cap), $"{s.Battery.StoredJoules / 1000:0}/{def.Battery.CapacityKJ:0} kJ");
                _detail.text = s.Battery.FlowWatts > 1 ? $"Charging at {UiKit.Watts(s.Battery.FlowWatts)}" : s.Battery.FlowWatts < -1 ? $"Discharging at {UiKit.Watts(-s.Battery.FlowWatts)}" : "Idle";
                _power.text = $"Grid {s.Power?.NetworkId}";
                _stats.text = "";
            }
            Fill(_bag, Gc.Session.Player.Inventory, items);
        }

        private float GameSessionRange(Structure s) => Afterfall.Services.GameSession.ContainerAccessRange + Gc.Session.FootprintRadius(s) + 0.5f;
    }

    /// <summary>Construction menu: categories, costs, technology locks.</summary>
    public sealed class BuildMenuWindow : WindowBase
    {
        private readonly VisualElement _list;
        private BuildingCategory? _filter;
        private readonly Label _info;

        public BuildMenuWindow(UiManager ui, GameController gc) : base(ui, gc, "Construction")
        {
            Root.Add(UiKit.Text("Pick a blueprint. Blueprints can be placed without materials; deliver materials and hold E to build. Unlock more by discovering knowledge and building infrastructure.", "muted"));
            var tabs = Root.Put(UiKit.Div("row"));
            tabs.Add(UiKit.Btn("All", () => { _filter = null; Rebuild(); }, "btn--small"));
            foreach (BuildingCategory c in new[] { BuildingCategory.Structure, BuildingCategory.Storage, BuildingCategory.Crafting, BuildingCategory.Industry, BuildingCategory.Power, BuildingCategory.Lighting, BuildingCategory.Defense, BuildingCategory.Furniture })
            {
                BuildingCategory cc = c;
                tabs.Add(UiKit.Btn(c.ToString(), () => { _filter = cc; Rebuild(); }, "btn--small"));
            }
            var scroll = Root.Put(new ScrollView());
            scroll.style.flexGrow = 1;
            _list = scroll;
            _info = Root.Put(UiKit.Text("", "muted"));
            Rebuild();
        }

        private void Rebuild()
        {
            _list.Clear();
            Inventory bag = Gc.Session.Player.Inventory;
            foreach (BuildingDefinition d in Gc.Session.Content.Buildings.Values.Where(b => b.Buildable).OrderBy(b => b.Category).ThenBy(b => b.Name))
            {
                if (_filter.HasValue && d.Category != _filter.Value) continue;
                BuildingDefinition def = d;
                bool unlocked = Gc.Session.World.Tech.IsUnlocked(d.RequiredTech);
                var row = _list.Put(UiKit.Div("list-item"));
                var col = row.Put(UiKit.Div("col", "grow"));
                col.Add(UiKit.Text($"{d.Name}  [{d.Category}]", "subtitle"));
                col.Add(UiKit.Text(d.Description, "muted"));
                string cost = string.Join(", ", d.Cost.Select(a => $"{Name(a.ItemId)} {Mathf.Min(bag.Count(a.ItemId), a.Quantity)}/{a.Quantity}"));
                bool affordable = bag.HasAll(d.Cost);
                col.Add(UiKit.Text(unlocked ? "Cost: " + cost : Gc.Session.Tech.DescribeRequirements(d.RequiredTech), unlocked ? (affordable ? "good" : "warn") : "bad"));
                Button b = row.Put(UiKit.Btn(unlocked ? "Place" : "Locked", () =>
                {
                    if (!Gc.Session.World.Tech.IsUnlocked(def.RequiredTech))
                    {
                        Gc.Toast(Gc.Session.Tech.DescribeRequirements(def.RequiredTech), Severity.Warning);
                        return;
                    }
                    Ui.CloseWindow();
                    Gc.Build.Begin(def);
                }, unlocked ? "btn--primary" : "btn--disabled"));
                b.style.width = 110;
            }
        }

        public override void Refresh()
        {
            _info.text = "Tip: in the strategic view (Tab) you can plan blueprints anywhere you have explored.";
        }
    }
}
