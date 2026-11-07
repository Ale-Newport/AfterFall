using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Items;
using Afterfall.Domain.World;

namespace Afterfall.Persistence
{
    /// <summary>Integrity checks run after loading (and by tests after round trips).</summary>
    public static class SaveValidator
    {
        public static List<string> Validate(WorldState w)
        {
            var problems = new List<string>();
            if (w.Player == null) problems.Add("Missing player.");
            var seen = new HashSet<EntityId>();
            void Check(EntityId id, string what)
            {
                if (!id.IsValid) problems.Add($"{what} has an invalid id.");
                else if (!seen.Add(id)) problems.Add($"Duplicate id {id} ({what}).");
                else if (id.Value >= w.Ids.NextValue) problems.Add($"Id {id} ({what}) is not below the allocator ({w.Ids.NextValue}).");
            }
            if (w.Player != null)
            {
                Check(w.Player.Id, "player");
                Check(w.Player.Inventory.Id, "player inventory");
            }
            foreach (var s in w.Structures.Values)
            {
                Check(s.Id, "structure " + s.DefId);
                if (s.Storage != null) Check(s.Storage.Id, "storage of " + s.Id);
                if (s.Machine != null)
                {
                    Check(s.Machine.Input.Id, "machine input of " + s.Id);
                    Check(s.Machine.Output.Id, "machine output of " + s.Id);
                    if (s.Machine.Fuel != null) Check(s.Machine.Fuel.Id, "machine fuel of " + s.Id);
                }
                if (s.Generator != null) Check(s.Generator.Fuel.Id, "generator fuel of " + s.Id);
            }
            foreach (var n in w.ResourceNodes.Values) Check(n.Id, "node");
            foreach (var d in w.Drops.Values)
            {
                Check(d.Id, "drop");
                Check(d.Contents.Id, "drop contents");
            }
            foreach (var z in w.Zombies.Values) Check(z.Id, "zombie");
            foreach (var c in w.Colonists.Values)
            {
                Check(c.Id, "colonist " + c.Name);
                Check(c.Carry.Id, "pack of " + c.Name);
            }
            foreach (var j in w.Jobs.Values) Check(j.Id, "job");

            foreach (Inventory inv in w.AllInventories)
            {
                for (int i = 0; i < inv.SlotCount; i++)
                {
                    ItemStack st = inv.GetSlot(i);
                    if (st.IsEmpty) continue;
                    if (st.Quantity < 0) problems.Add($"Negative quantity in {inv.Id}.");
                }
                foreach (ReservationRecord r in inv.Reservations)
                {
                    if (r.Kind == ReservationKind.Outgoing && inv.Count(r.ItemId) < inv.ReservedOutgoing(r.ItemId))
                        problems.Add($"Inventory {inv.Id} reserves more {r.ItemId} than it holds.");
                }
            }
            foreach (PowerLink l in w.PowerLinks)
                if (!w.Structures.ContainsKey(l.A) || !w.Structures.ContainsKey(l.B)) problems.Add($"Dangling power link {l.A}-{l.B}.");
            return problems;
        }
    }
}
