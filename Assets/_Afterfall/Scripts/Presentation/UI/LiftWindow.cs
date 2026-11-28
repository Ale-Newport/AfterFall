using Afterfall.Domain.World;
using UnityEngine.UIElements;

namespace Afterfall.Presentation.UI
{
    /// <summary>A lift's floor buttons: every floor of the building, the current one marked.</summary>
    public sealed class LiftWindow : WindowBase
    {
        public LiftWindow(UiManager ui, GameController gc, CityBuildingViews views, string buildingId, PlacedPlan building, int current)
            : base(ui, gc, building.Plan.Name + " · lift")
        {
            Root.Add(UiKit.Text($"{building.Plan.Floors} floors. You are on {(current == 0 ? "the ground floor" : "floor " + current)}.", "muted"));
            var grid = Root.Put(UiKit.Div("row", "wrap-row"));
            for (int f = building.Plan.Floors - 1; f >= 0; f--)
            {
                int floor = f;
                string label = f == 0 ? "Ground" : f.ToString();
                PlanRoom room = building.Plan.Rooms.Find(r => r.Floor == f && !r.Name.Contains("stair hall") && !r.Name.Contains("Entrance hall"));
                Button b = grid.Put(UiKit.Btn(label, () =>
                {
                    Ui.CloseWindow();
                    views.TakeLift(buildingId, floor);
                }, "btn--small"));
                if (room != null) b.tooltip = room.Name;
                if (f == current) b.AddToClassList("btn--selected");
                b.style.minWidth = 64;
            }
        }

        public override void Refresh() { }
    }
}
