using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Events;
using Afterfall.Domain.Items;
using Afterfall.Domain.Vehicles;
using UnityEngine.UIElements;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation.UI
{
    /// <summary>
    /// A vehicle's condition and trunk (R at or inside a vehicle): fuel and component condition,
    /// how it starts, refuelling with a fuel can, repairs, locking with the key, claiming it for the
    /// settlement, opening its doors, and the trunk's contents beside the backpack.
    /// </summary>
    public sealed class VehicleWindow : WindowBase
    {
        private readonly EntityId _id;
        private readonly Label _summary, _starts, _status;
        private readonly ItemSlot[] _trunk, _bag;
        private readonly Button _lock, _claim;
        private readonly Dictionary<VehicleComponent, Button> _repair = new Dictionary<VehicleComponent, Button>();

        public VehicleWindow(UiManager ui, GameController gc, EntityId id) : base(ui, gc, Title(gc, id))
        {
            _id = id;
            VehicleState v = gc.Session.Vehicles.Get(id);
            _summary = Root.Put(UiKit.Text("", "subtitle"));
            _starts = Root.Put(UiKit.Text("", "muted"));
            var actions = Root.Put(UiKit.Div("row", "wrap-row"));
            actions.Add(UiKit.Btn("Pour in a fuel can", () => Report(Gc.Session.Vehicles.Refuel(_id)), "btn--small", "btn--primary"));
            foreach (VehicleComponent c in new[] { VehicleComponent.Engine, VehicleComponent.Body, VehicleComponent.Tyres, VehicleComponent.Battery })
            {
                VehicleComponent comp = c;
                _repair[c] = actions.Put(UiKit.Btn("", () => Report(Gc.Session.Vehicles.Repair(_id, comp)), "btn--small"));
            }
            _lock = actions.Put(UiKit.Btn("", ToggleLock, "btn--small"));
            _claim = actions.Put(UiKit.Btn("", () =>
            {
                VehicleState cur = Gc.Session.Vehicles.Get(_id);
                if (cur != null) Report(Gc.Session.Vehicles.Claim(_id, !cur.Claimed));
            }, "btn--small"));
            actions.Add(UiKit.Btn("Open / close the doors", ToggleDoors, "btn--small"));

            var body = Root.Put(UiKit.Div("row"));
            body.style.alignItems = Align.FlexStart;
            var left = body.Put(UiKit.Div("col"));
            left.style.width = 600;
            left.Add(UiKit.Text(v?.Storage?.Label ?? "Trunk", "subtitle"));
            _trunk = Grid(left, v?.Storage?.SlotCount ?? 0, 0, (s, e) => Move(v.Storage.Id, Gc.Session.Player.Inventory.Id, s.Index, e.shiftKey));
            var right = body.Put(UiKit.Div("col"));
            right.style.marginLeft = 24;
            right.style.width = 600;
            right.Add(UiKit.Text("Backpack", "subtitle"));
            _bag = Grid(right, Afterfall.Domain.World.PlayerState.BackpackSlots, Afterfall.Domain.World.PlayerState.HotbarSize, (s, e) => Move(Gc.Session.Player.Inventory.Id, v.Storage.Id, s.Index, e.shiftKey));
            _status = Root.Put(UiKit.Text("", "muted"));
        }

        private static string Title(GameController gc, EntityId id)
        {
            VehicleState v = gc.Session.Vehicles.Get(id);
            VehicleDefinition def = gc.Session.Vehicles.Def(v);
            return def == null ? "Vehicle" : string.IsNullOrEmpty(v.LockId) ? def.Name : $"{def.Name} — {v.LockId}";
        }

        public override void Refresh()
        {
            VehicleState v = Gc.Session.Vehicles.Get(_id);
            VehicleDefinition def = Gc.Session.Vehicles.Def(v);
            if (v == null || def == null)
            {
                Ui.CloseWindow();
                return;
            }
            _summary.text = VehicleRules.Summary(v, def) + (v.WindowBroken ? " · smashed window" : "") + $" · {v.OdometerMetres / 1000.0:0.0} km driven here";
            string problem = VehicleRules.StartProblem(v, def, Gc.Session.Player.Inventory);
            string how = v.KeyInIgnition ? "The key is in the ignition." : v.Hotwired ? "Hotwired: it starts without a key." : VehicleRules.HasKey(Gc.Session.Player.Inventory, v) ? "You have its key." : "You do not have its key.";
            _starts.text = $"{how} {(problem.Length == 0 ? "It will start." : problem)} {(v.Locked ? "Locked." : "Unlocked.")} Seats {def.Seats}, trunk {def.StorageSlots} slots up to {def.StorageMaxWeight:0} kg.";
            ItemCatalog items = Gc.Session.Content.Items;
            foreach (var kv in _repair)
            {
                var cost = new List<string>();
                foreach (ItemAmount a in VehicleRules.RepairCost(kv.Key)) cost.Add($"{a.Quantity} {items.NameOf(a.ItemId)}");
                kv.Value.text = $"Repair {kv.Key.ToString().ToLowerInvariant()} {VehicleRules.Condition(v, kv.Key):0}% ({string.Join(", ", cost)}{(kv.Key == VehicleComponent.Battery ? "" : " + hammer")})";
            }
            _lock.text = def.Doors == 0 ? "No doors to lock" : v.Locked ? "Unlock with the key" : "Lock with the key";
            _claim.text = v.Claimed ? "Release from the settlement" : "Claim for the settlement";
            if (v.Storage != null) Fill(_trunk, v.Storage, items);
            Fill(_bag, Gc.Session.Player.Inventory, items);
            _status.text = Gc.Session.CanAccess(v.Storage.Id, out string reason) ? "" : reason;
        }

        private void ToggleLock()
        {
            VehicleState v = Gc.Session.Vehicles.Get(_id);
            if (v != null) Report(Gc.Session.Vehicles.SetLocked(_id, !v.Locked));
        }

        private void ToggleDoors()
        {
            VehicleState v = Gc.Session.Vehicles.Get(_id);
            if (v != null) Report(Gc.Session.Vehicles.SetDoorsOpen(_id, v.DoorsOpen == 0));
        }

        private void Move(EntityId from, EntityId to, int slot, bool half)
        {
            Inventory src = Gc.Session.World.FindInventory(from);
            if (src == null) return;
            ItemStack st = src.GetSlot(slot);
            if (st.IsEmpty) return;
            int amount = half ? (st.Quantity + 1) / 2 : st.Quantity;
            OpResult r = Gc.Session.Transfer(from, slot, to, amount);
            if (!r.Ok) Report(r);
            Refresh();
        }
    }
}
