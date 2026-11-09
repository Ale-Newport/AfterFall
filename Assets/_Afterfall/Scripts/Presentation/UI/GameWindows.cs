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

        protected ItemDefinition Def(string itemId) => Gc.Session.Content.Items.TryGet(itemId, out ItemDefinition d) ? d : null;

        /// <summary>
        /// A recipe as icon chips: inputs joined by "+", an arrow, then outputs, each chip showing its quantity.
        /// </summary>
        protected static VisualElement Formula(RecipeDefinition r, ItemCatalog items)
        {
            var row = UiKit.Div("formula");
            row.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < r.Inputs.Count; i++)
            {
                if (i > 0) row.Add(UiKit.Text("+", "formula-op"));
                row.Put(new ItemChip("icon-28")).Set(items.TryGet(r.Inputs[i].ItemId, out ItemDefinition d) ? d : null, "×" + r.Inputs[i].Quantity);
            }
            row.Add(UiKit.GlyphIcon("arrow", "icon-20"));
            foreach (ItemAmount o in r.Outputs)
                row.Put(new ItemChip("icon-28")).Set(items.TryGet(o.ItemId, out ItemDefinition d) ? d : null, "×" + o.Quantity);
            foreach (VisualElement e in row.Children()) e.pickingMode = PickingMode.Ignore;
            return row;
        }

        /// <summary>The output item of a recipe (its first output), for the recipe's icon.</summary>
        internal static ItemDefinition OutputOf(RecipeDefinition r, ItemCatalog items) =>
            r.Outputs.Count > 0 && items.TryGet(r.Outputs[0].ItemId, out ItemDefinition d) ? d : null;
    }

    /// <summary>A recipe in a list: output icon, name and a short status tag; the whole row is a button.</summary>
    internal sealed class RecipeRow
    {
        public readonly Button Button;
        private readonly Label _tag;

        public RecipeRow(RecipeDefinition r, ItemCatalog items, System.Action onClick)
        {
            Button = UiKit.Btn("", onClick, "btn--small", "recipe-row");
            Button.Add(UiKit.ItemIcon(WindowBase.OutputOf(r, items), "icon-36"));
            int qty = r.Outputs.Count > 0 ? r.Outputs[0].Quantity : 1;
            Label name = Button.Put(UiKit.Text(qty > 1 ? $"{r.Name}  ×{qty}" : r.Name, "recipe-name"));
            name.pickingMode = PickingMode.Ignore;
            _tag = Button.Put(UiKit.Text("", "recipe-tag"));
            _tag.pickingMode = PickingMode.Ignore;
        }

        public void Set(string tag, string tone, bool selected, bool available)
        {
            _tag.text = tag;
            UiKit.SetTone(_tag, tone);
            UiKit.SetClass(Button, "btn--selected", selected);
            UiKit.SetClass(Button, "btn--disabled", !available);
        }
    }

    /// <summary>Backpack, item details and crafting.</summary>
    public sealed class InventoryWindow : WindowBase
    {
        private readonly ItemSlot[] _slots;
        private readonly Label _weight, _detailTitle, _detailText;
        private readonly VisualElement _detailIcon, _detailActions, _recipeList, _queue;
        private readonly VisualElement _recipeIcon, _ingredients;
        private readonly Label _recipeName, _recipeMeta, _recipeStatus;
        private readonly List<ItemChip> _ingredientChips = new List<ItemChip>();
        private readonly Button _craftButton;
        private int _selected = -1;
        private string _recipe, _recipeShown, _detailShown;
        private readonly Dictionary<string, RecipeRow> _recipeRows = new Dictionary<string, RecipeRow>();
        private int _queueCount = -1;

        public InventoryWindow(UiManager ui, GameController gc, bool craftingFocus) : base(ui, gc, "Backpack & Crafting")
        {
            ItemCatalog items = gc.Session.Content.Items;
            var body = Root.Put(UiKit.Div("row", "grow"));
            body.style.alignItems = Align.FlexStart;

            var left = body.Put(UiKit.Div("col"));
            left.style.width = 600;
            left.Add(UiKit.Text("Click to select · click another slot to move · right-click to use/equip · shift-click to split · first row = hotbar", "muted"));
            _slots = Grid(left, PlayerState.BackpackSlots, PlayerState.HotbarSize, OnSlot);
            _weight = left.Put(UiKit.Text("", "muted"));
            var detail = left.Put(UiKit.Div("row", "item-detail"));
            detail.style.alignItems = Align.FlexStart;
            _detailIcon = detail.Put(UiKit.ItemIcon(null, "icon-64"));
            var detailText = detail.Put(UiKit.Div("col", "grow"));
            _detailTitle = detailText.Put(UiKit.Text("", "subtitle"));
            _detailText = detailText.Put(UiKit.Text("", "wrap"));
            _detailActions = detailText.Put(UiKit.Div("row"));

            var right = body.Put(UiKit.Div("col", "grow"));
            right.style.marginLeft = 16;
            right.Add(UiKit.Text("Crafting", "subtitle"));
            right.Add(UiKit.Text(craftingFocus ? "You are at a crafting station: its recipes are available here." : "Hand recipes work anywhere; station recipes need the station nearby.", "muted"));
            var scroll = right.Put(new ScrollView());
            scroll.style.height = 260;
            _recipeList = scroll;
            foreach (RecipeDefinition r in gc.Session.PlayerRecipes())
            {
                RecipeDefinition rr = r;
                var row = new RecipeRow(rr, items, () => { _recipe = rr.Id; Refresh(); });
                _recipeRows[r.Id] = row;
                _recipeList.Add(row.Button);
                // Open on something useful: the first recipe that can be crafted right now, else the first one.
                if (_recipe == null || (!gc.Session.CheckRecipe(_recipe).CanCraft && gc.Session.CheckRecipe(r.Id).CanCraft)) _recipe = r.Id;
            }

            var panel = right.Put(UiKit.Div("recipe-panel"));
            var head = panel.Put(UiKit.Div("row"));
            _recipeIcon = head.Put(UiKit.ItemIcon(null, "icon-64"));
            var headText = head.Put(UiKit.Div("col", "grow"));
            headText.style.marginLeft = 8;
            _recipeName = headText.Put(UiKit.Text("Select a recipe.", "subtitle"));
            _recipeMeta = headText.Put(UiKit.Text("", "muted"));
            _ingredients = panel.Put(UiKit.Div("formula"));
            _recipeStatus = panel.Put(UiKit.Text("", "wrap"));
            _craftButton = right.Put(UiKit.Btn("Craft", () =>
            {
                if (_recipe != null) Report(Gc.Session.Craft(_recipe));
            }, "btn--primary", "btn--disabled"));
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
                if (_detailShown != null)
                {
                    _detailShown = null;
                    UiKit.SetIcon(_detailIcon, null);
                }
                UiKit.Show(_detailIcon, false);
                return;
            }
            ItemDefinition d = items.Get(st.ItemId);
            if (_detailShown != d.Id)
            {
                _detailShown = d.Id;
                UiKit.SetIcon(_detailIcon, d);
            }
            UiKit.Show(_detailIcon, true);
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
            ItemCatalog items = Gc.Session.Content.Items;
            foreach (KeyValuePair<string, RecipeRow> kv in _recipeRows)
            {
                RecipeDefinition r = Gc.Session.Content.Recipe(kv.Key);
                CraftCheck c = Gc.Session.CheckRecipe(kv.Key);
                string where = r.IsHandRecipe ? "By hand" : CraftingRules.StationName(r.Station);
                bool selected = kv.Key == _recipe;
                if (c.Locked) kv.Value.Set("Locked", "bad", selected, false);
                else if (c.CanCraft) kv.Value.Set(where, "good", selected, true);
                else if (c.StationMissing) kv.Value.Set("Needs " + where, "warn", selected, false);
                else kv.Value.Set(where, null, selected, false);
            }
            if (_recipe != null)
            {
                RecipeDefinition r = Gc.Session.Content.Recipe(_recipe);
                CraftCheck c = Gc.Session.CheckRecipe(_recipe);
                if (_recipeShown != _recipe)
                {
                    // Rebuild the ingredient chips only when another recipe is picked; counts update below.
                    _recipeShown = _recipe;
                    UiKit.SetIcon(_recipeIcon, OutputOf(r, items));
                    _recipeName.text = r.Name;
                    string outs = string.Join(", ", r.Outputs.Select(a => $"{a.Quantity} × {Name(a.ItemId)}"));
                    _recipeMeta.text = $"Makes {outs} · {r.Duration:0.#} s · {(r.IsHandRecipe ? "by hand" : "at a " + CraftingRules.StationName(r.Station))}";
                    _ingredients.Clear();
                    _ingredientChips.Clear();
                    foreach (ItemAmount a in r.Inputs) _ingredientChips.Add(_ingredients.Put(new ItemChip("icon-32")));
                }
                for (int i = 0; i < r.Inputs.Count && i < _ingredientChips.Count; i++)
                {
                    ItemAmount a = r.Inputs[i];
                    int have = inv.Available(a.ItemId);
                    _ingredientChips[i].Set(Def(a.ItemId), $"{Name(a.ItemId)} {Mathf.Min(have, a.Quantity)}/{a.Quantity}", have >= a.Quantity ? "good" : "bad");
                }
                _recipeStatus.text = c.CanCraft ? "Ready to craft." : c.Reason;
                UiKit.SetTone(_recipeStatus, c.CanCraft ? "good" : "bad");
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
                    RecipeDefinition r = Gc.Session.Content.Recipe(jobs[i].RecipeId);
                    var row = _queue.Put(UiKit.Div("row", "queue-row"));
                    if (r != null) row.Add(UiKit.ItemIcon(OutputOf(r, items), "icon-28"));
                    row.Add(UiKit.Text(r?.Name ?? jobs[i].RecipeId, "grow", "queue-name"));
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
            statusRow.Add(UiKit.Btn("Repair", () => Report(Gc.Session.Repair(_id)), "btn--small"));
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
                    ItemCatalog items = gc.Session.Content.Items;
                    left.Add(FuelHeader(items.All.Where(i => i.BurnSeconds > 0).OrderByDescending(i => i.BurnSeconds).Take(3), i => $"{i.BurnSeconds:0} s"));
                    _fuel = Grid(left, s.Machine.Fuel.SlotCount, 0, (sl, e) => Take(s.Machine.Fuel, sl.Index));
                }
                var outHead = left.Put(UiKit.Div("row"));
                outHead.Add(UiKit.Text("Output", "subtitle"));
                outHead.Add(UiKit.Btn("Collect all", () => Report(Gc.Session.TakeAll(s.Machine.Output.Id)), "btn--small", "btn--primary"));
                _output = Grid(left, s.Machine.Output.SlotCount, 0, (sl, e) => Take(s.Machine.Output, sl.Index));
            }
            else if (s.Generator != null)
            {
                ItemCatalog items = gc.Session.Content.Items;
                left.Add(FuelHeader(items.All.Where(i => i.EnergyKJ > 0).OrderByDescending(i => i.EnergyKJ).Take(3), i => $"{i.EnergyKJ:0} kJ"));
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

        /// <summary>"Fuel" with the accepted fuels as icon chips and how long or how much each one gives.</summary>
        private static VisualElement FuelHeader(IEnumerable<ItemDefinition> fuels, System.Func<ItemDefinition, string> value)
        {
            var row = UiKit.Div("row", "fuel-head");
            row.Add(UiKit.Text("Fuel", "subtitle"));
            foreach (ItemDefinition f in fuels) row.Put(new ItemChip("icon-28")).Set(f, $"{f.Name} {value(f)}");
            return row;
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
                _status.text = $"Status: {UiKit.Describe(m.Status)}";
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
                        Button b = UiKit.Btn("", () => { Report(Gc.Session.SetMachineRecipe(_id, rec.Id)); _recipeShown = null; }, "btn--small", "btn--list", "recipe-row");
                        b.Add(UiKit.ItemIcon(OutputOf(rr, items), "icon-32"));
                        var col = b.Put(UiKit.Div("col", "grow"));
                        col.pickingMode = PickingMode.Ignore;
                        col.style.marginLeft = 6;
                        col.Add(UiKit.Text(rr.Name, "recipe-title"));
                        if (unlocked) col.Add(Formula(rr, items));
                        else col.Add(UiKit.Text("Locked — " + Gc.Session.Tech.DescribeRequirements(rr.RequiredTech), "recipe-tag", "bad"));
                        b.Add(UiKit.Text($"{rr.Duration:0.#} s", "recipe-tag"));
                        foreach (VisualElement e in col.Children()) e.pickingMode = PickingMode.Ignore;
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
            foreach (BuildingCategory c in new[] { BuildingCategory.Structure, BuildingCategory.Storage, BuildingCategory.Crafting, BuildingCategory.Industry, BuildingCategory.Power, BuildingCategory.Lighting, BuildingCategory.Defense, BuildingCategory.Furniture, BuildingCategory.Farming })
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
                if (unlocked)
                {
                    // Cost as icon chips; each one green when the backpack already holds enough.
                    var cost = col.Put(UiKit.Div("formula", "cost-row"));
                    cost.Add(UiKit.Text(d.Cost.Count > 0 ? "Cost" : "Free", "muted", "cost-label"));
                    foreach (ItemAmount a in d.Cost)
                    {
                        int have = bag.Count(a.ItemId);
                        cost.Put(new ItemChip("icon-28")).Set(Def(a.ItemId), $"{Name(a.ItemId)} {Mathf.Min(have, a.Quantity)}/{a.Quantity}", have >= a.Quantity ? "good" : "warn");
                    }
                }
                else
                {
                    var locked = col.Put(UiKit.Div("row"));
                    locked.Add(UiKit.GlyphIcon("lock", "icon-20"));
                    locked.Add(UiKit.Text(Gc.Session.Tech.DescribeRequirements(d.RequiredTech), "bad", "grow"));
                }
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
