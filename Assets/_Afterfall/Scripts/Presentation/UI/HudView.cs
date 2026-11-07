using System.Collections.Generic;
using System.Text;
using Afterfall.Domain.Construction;
using Afterfall.Domain.Crafting;
using Afterfall.Domain.Items;
using Afterfall.Domain.Survival;
using Afterfall.Domain.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Afterfall.Presentation.UI
{
    /// <summary>Third-person HUD: vitals, hotbar, prompt, clock, warnings, crafting and placement info.</summary>
    public sealed class HudView
    {
        private readonly UiManager _ui;
        private readonly GameController _gc;
        private readonly Bar _health, _stamina, _hunger, _thirst;
        private readonly Label _carry, _equipped, _clock, _prompt, _mode, _warnings, _craftLabel, _placeTitle, _placeCost, _placeReasons, _caption;
        private readonly Bar _craftBar;
        private readonly VisualElement _crosshair, _craftPanel, _placePanel, _vitals, _hotbarRow;
        private readonly ItemSlot[] _hotbar = new ItemSlot[PlayerState.HotbarSize];

        public VisualElement Root { get; }

        public HudView(UiManager ui, GameController gc)
        {
            _ui = ui;
            _gc = gc;
            Root = UiKit.Div("layer");
            Root.pickingMode = PickingMode.Ignore;

            _vitals = Root.Put(UiKit.Div("panel", "hud-vitals"));
            _health = _vitals.Put(new Bar(new Color(0.78f, 0.22f, 0.2f)));
            _stamina = _vitals.Put(new Bar(new Color(0.85f, 0.72f, 0.25f)));
            _hunger = _vitals.Put(new Bar(new Color(0.78f, 0.5f, 0.22f)));
            _thirst = _vitals.Put(new Bar(new Color(0.25f, 0.52f, 0.85f)));
            _carry = _vitals.Put(UiKit.Text("", "hud-equipped"));
            _equipped = _vitals.Put(UiKit.Text("", "hud-equipped"));

            _clock = Root.Put(UiKit.Text("", "hud-clock"));
            _mode = Root.Put(UiKit.Text("", "hud-mode"));
            _warnings = Root.Put(UiKit.Text("", "hud-warning", "warn"));
            _crosshair = Root.Put(UiKit.Div("hud-crosshair"));
            _prompt = Root.Put(UiKit.Text("", "hud-prompt"));
            _caption = Root.Put(UiKit.Text("", "hud-caption"));

            _hotbarRow = Root.Put(UiKit.Div("hud-hotbar"));
            for (int i = 0; i < _hotbar.Length; i++)
            {
                var slot = new ItemSlot(i, (i + 1).ToString());
                slot.AddToClassList("slot--hotbar");
                slot.Clicked = (s, e) => _gc.Session.SelectHotbar(s.Index);
                _hotbar[i] = _hotbarRow.Put(slot);
            }

            _craftPanel = Root.Put(UiKit.Div("panel", "hud-craft"));
            _craftLabel = _craftPanel.Put(UiKit.Text("", "muted"));
            _craftBar = _craftPanel.Put(new Bar(new Color(0.75f, 0.6f, 0.3f), 10f));

            _placePanel = Root.Put(UiKit.Div("panel", "hud-placement"));
            _placeTitle = _placePanel.Put(UiKit.Text("", "subtitle"));
            _placeCost = _placePanel.Put(UiKit.Text("", "wrap"));
            _placeReasons = _placePanel.Put(UiKit.Text("", "wrap"));
            _placePanel.Add(UiKit.Text("LMB place · R / wheel rotate · RMB cancel · B change", "muted"));
            foreach (VisualElement e in new VisualElement[] { _vitals, _craftPanel, _placePanel }) e.pickingMode = PickingMode.Ignore;
        }

        public void Refresh()
        {
            PlayerState p = _gc.Session.Player;
            SurvivalState v = p.Vitals;
            ItemCatalog items = _gc.Session.Content.Items;
            _health.Set(v.Health / v.MaxHealth, $"Health {v.Health:0}/{v.MaxHealth:0}");
            _stamina.Set(v.Stamina / v.MaxStamina, $"Stamina {v.Stamina:0}");
            _hunger.Set(v.Hunger / 100f, $"Food {v.Hunger:0}%{(v.Hunger < 20 ? "  STARVING" : "")}");
            _thirst.Set(v.Thirst / 100f, $"Water {v.Thirst:0}%{(v.Thirst < 20 ? "  THIRSTY" : "")}");
            float carried = _gc.Session.CarriedWeight;
            _carry.text = $"Carrying {carried:0.0} / {PlayerState.CarryMaxKg:0} kg{(carried > PlayerState.CarryComfortKg ? "  (encumbered)" : "")}";
            UiKit.SetClass(_carry, "warn", carried > PlayerState.CarryComfortKg);
            ItemDefinition eq = _gc.Session.EquippedItem;
            _equipped.text = eq == null ? "Bare hands" : $"In hand: {eq.Name}";

            var clock = _gc.Session.World.Clock;
            _clock.text = $"{clock.FormatTime()}  {(clock.IsNight ? "(night)" : "(day)")}";

            bool tactical = _gc.CameraRig.TacticalActive;
            bool windows = _ui.AnyWindowOpen;
            UiKit.Show(_hotbarRow, !tactical);
            _gc.Ui.SetToastsBesidePanel(tactical);
            UiKit.Show(_vitals, true);
            UiKit.Show(_crosshair, !tactical && !windows && !v.IsDead);
            for (int i = 0; i < _hotbar.Length; i++)
            {
                _hotbar[i].Set(p.Inventory.GetSlot(i), items);
                _hotbar[i].SetActive(i == p.SelectedHotbar);
            }

            string prompt = windows || v.IsDead ? null : _gc.CurrentPrompt;
            _prompt.text = prompt ?? "";
            UiKit.Show(_prompt, !string.IsNullOrEmpty(prompt));

            if (_gc.Build.IsPlacing) _mode.text = (tactical ? "STRATEGIC VIEW · " : "") + "BUILD MODE";
            else if (tactical) _mode.text = "STRATEGIC VIEW — Tab to return · LMB select · WASD pan · Q/E rotate · wheel zoom";
            else _mode.text = "";
            UiKit.Show(_mode, _mode.text.Length > 0);

            _warnings.text = Warnings(v, carried);
            AudioDirector audio = _gc.Audio;
            bool caption = audio != null && Time.unscaledTime < audio.CaptionUntil && !string.IsNullOrEmpty(audio.Caption);
            _caption.text = caption ? audio.Caption : "";
            UiKit.Show(_caption, caption);

            CraftJob job = p.Crafting.Jobs.Count > 0 ? p.Crafting.Jobs[0] : null;
            UiKit.Show(_craftPanel, job != null);
            if (job != null)
            {
                RecipeDefinition r = _gc.Session.Content.Recipe(job.RecipeId);
                _craftLabel.text = $"Crafting {r?.Name}{(job.AwaitingSpace ? " — inventory full!" : "")}{(p.Crafting.Jobs.Count > 1 ? $"  (+{p.Crafting.Jobs.Count - 1} queued)" : "")}";
                _craftBar.Set(job.Duration > 0 ? job.Progress / job.Duration : 1f, "");
            }

            UiKit.Show(_placePanel, _gc.Build.IsPlacing);
            if (_gc.Build.IsPlacing) RefreshPlacement();
        }

        private void RefreshPlacement()
        {
            BuildingDefinition def = _gc.Build.Current;
            _placeTitle.text = def.Name;
            var sb = new StringBuilder("Cost: ");
            bool first = true;
            foreach (ItemAmount a in def.Cost)
            {
                int have = _gc.Session.Player.Inventory.Count(a.ItemId);
                if (!first) sb.Append(", ");
                sb.Append($"{_gc.Session.Content.Items.NameOf(a.ItemId)} {Mathf.Min(have, a.Quantity)}/{a.Quantity}");
                first = false;
            }
            _placeCost.text = sb.ToString();
            PlacementResult r = _gc.Build.LastResult;
            if (r == null)
            {
                _placeReasons.text = "Aim at the ground.";
                SetTone(_placeReasons, "warn");
            }
            else if (r.Valid)
            {
                _placeReasons.text = r.MissingMaterials.Count > 0 ? "OK — Valid placement — materials can be delivered later." : "OK — Valid placement";
                SetTone(_placeReasons, "good");
            }
            else
            {
                _placeReasons.text = "x " + string.Join("\nx ", r.Reasons);
                SetTone(_placeReasons, "bad");
            }
        }

        private static void SetTone(VisualElement e, string tone)
        {
            foreach (string c in new[] { "good", "bad", "warn" }) e.RemoveFromClassList(c);
            e.AddToClassList(tone);
        }

        private string Warnings(SurvivalState v, float carried)
        {
            var lines = new List<string>();
            if (v.Hunger <= 0f) lines.Add("! Starving — you are losing health. Eat now.");
            else if (v.Hunger < 20f) lines.Add("! Hungry");
            if (v.Thirst <= 0f) lines.Add("! Dehydrated — you are losing health. Drink now.");
            else if (v.Thirst < 20f) lines.Add("! Thirsty");
            if (v.Health < 30f && !v.IsDead) lines.Add("! Badly hurt — use a bandage");
            if (carried > PlayerState.CarryComfortKg) lines.Add("! Over-encumbered — moving slower");
            float heat = _gc.Session.World.Threat.Heat;
            if (heat > 25f) lines.Add($"! Your base is attracting attention (noise {heat:0}%)");
            if (_gc.Session.World.Threat.PendingRaidAt > _gc.Session.Now) lines.Add("!! A group of infected is approaching your base!");
            return string.Join("\n", lines);
        }
    }
}
