using System;
using Afterfall.Domain.Items;
using UnityEngine;
using UnityEngine.UIElements;

namespace Afterfall.Presentation.UI
{
    /// <summary>Small helpers for building UI Toolkit trees in code.</summary>
    public static class UiKit
    {
        public static VisualElement Div(params string[] classes)
        {
            var e = new VisualElement();
            foreach (string c in classes) e.AddToClassList(c);
            return e;
        }

        public static Label Text(string text, params string[] classes)
        {
            var l = new Label(text);
            foreach (string c in classes) l.AddToClassList(c);
            return l;
        }

        public static Button Btn(string text, Action onClick, params string[] classes)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("btn");
            foreach (string c in classes) b.AddToClassList(c);
            return b;
        }

        public static void Show(VisualElement e, bool visible) => e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public static T Put<T>(this VisualElement parent, T child) where T : VisualElement
        {
            parent.Add(child);
            return child;
        }

        public static void SetClass(VisualElement e, string cls, bool on)
        {
            if (on) e.AddToClassList(cls);
            else e.RemoveFromClassList(cls);
        }

        public static string Watts(float w) => w >= 1000f ? $"{w / 1000f:0.0} kW" : $"{w:0} W";

        /// <summary>Player-facing name of a machine state.</summary>
        public static string Describe(Afterfall.Domain.Industry.MachineStatus status)
        {
            switch (status)
            {
                case Afterfall.Domain.Industry.MachineStatus.WaitingForInput: return "Waiting for input";
                case Afterfall.Domain.Industry.MachineStatus.OutputFull: return "Output full";
                case Afterfall.Domain.Industry.MachineStatus.MissingPower: return "No power";
                case Afterfall.Domain.Industry.MachineStatus.MissingFuel: return "No fuel";
                case Afterfall.Domain.Industry.MachineStatus.RecipeNotConfigured: return "No recipe chosen";
                case Afterfall.Domain.Industry.MachineStatus.Disabled: return "Switched off";
                case Afterfall.Domain.Industry.MachineStatus.Unbuilt: return "Not built";
                default: return status.ToString();
            }
        }

        /// <summary>"State: detail", without repeating the state when the detail already starts with it.</summary>
        public static string StatusText(Afterfall.Domain.Industry.MachineState m)
        {
            string state = Describe(m.Status);
            if (string.IsNullOrEmpty(m.StatusDetail)) return state;
            return m.StatusDetail.StartsWith(state, System.StringComparison.OrdinalIgnoreCase) ? m.StatusDetail : $"{state}: {m.StatusDetail}";
        }
    }

    /// <summary>A labelled progress bar.</summary>
    public sealed class Bar : VisualElement
    {
        private readonly VisualElement _fill;
        private readonly Label _label;

        public Bar(Color color, float height = 20f)
        {
            AddToClassList("bar");
            style.height = height;
            _fill = UiKit.Div("bar-fill");
            _fill.style.backgroundColor = color;
            Add(_fill);
            _label = UiKit.Text("", "bar-label");
            Add(_label);
        }

        public void Set(float fraction, string text)
        {
            _fill.style.width = Length.Percent(Mathf.Clamp01(fraction) * 100f);
            _label.text = text;
        }

        public void SetColor(Color c) => _fill.style.backgroundColor = c;
    }

    /// <summary>One inventory slot: procedural icon, quantity, durability, hotbar key.</summary>
    public sealed class ItemSlot : VisualElement
    {
        private readonly Label _icon, _qty, _key;
        private readonly VisualElement _dur;
        public int Index;
        public Action<ItemSlot, PointerDownEvent> Clicked;

        /// <summary>Name and description shown in the runtime hover tooltip (VisualElement.tooltip is editor-only).</summary>
        public string HoverText { get; private set; } = "";

        public ItemSlot(int index, string key = null)
        {
            Index = index;
            AddToClassList("slot");
            _icon = UiKit.Text("", "slot-icon");
            _icon.pickingMode = PickingMode.Ignore;
            Add(_icon);
            _qty = UiKit.Text("", "slot-qty");
            _qty.pickingMode = PickingMode.Ignore;
            Add(_qty);
            _dur = UiKit.Div("slot-dur");
            _dur.pickingMode = PickingMode.Ignore;
            Add(_dur);
            _key = UiKit.Text(key ?? "", "slot-key");
            _key.pickingMode = PickingMode.Ignore;
            Add(_key);
            RegisterCallback<PointerDownEvent>(e => Clicked?.Invoke(this, e));
            RegisterCallback<PointerEnterEvent>(e => UiManager.Current?.ShowTooltip(HoverText, e.position));
            RegisterCallback<PointerMoveEvent>(e => UiManager.Current?.ShowTooltip(HoverText, e.position));
            RegisterCallback<PointerLeaveEvent>(e => UiManager.Current?.HideTooltip());
            RegisterCallback<DetachFromPanelEvent>(e => UiManager.Current?.HideTooltip());
        }

        public void Set(ItemStack stack, ItemCatalog catalog)
        {
            if (stack.IsEmpty || !catalog.TryGet(stack.ItemId, out ItemDefinition def))
            {
                _icon.text = "";
                _icon.style.backgroundImage = new StyleBackground(StyleKeyword.None);
                _icon.style.backgroundColor = new Color(0, 0, 0, 0);
                _qty.text = "";
                _dur.style.display = DisplayStyle.None;
                HoverText = "";
                return;
            }
            Texture2D icon = ItemIcons.Get(def);
            _icon.text = icon != null ? "" : def.Icon;
            _icon.style.backgroundImage = icon != null ? new StyleBackground(icon) : new StyleBackground(StyleKeyword.None);
            _icon.style.backgroundColor = icon != null ? new Color(0, 0, 0, 0) : Conversions.ParseColor(def.Color, Color.gray);
            _qty.text = stack.Quantity > 1 ? stack.Quantity.ToString() : "";
            if (def.HasDurability)
            {
                float f = Mathf.Clamp01(stack.Durability / (float)def.MaxDurability);
                _dur.style.display = DisplayStyle.Flex;
                _dur.style.width = Length.Percent(f * 90f);
                _dur.style.backgroundColor = Color.Lerp(new Color(0.9f, 0.3f, 0.2f), new Color(0.4f, 0.8f, 0.35f), f);
            }
            else _dur.style.display = DisplayStyle.None;
            HoverText = $"{def.Name}{(stack.Quantity > 1 ? $" ×{stack.Quantity}" : "")}\n{def.Description}\n{def.Weight * stack.Quantity:0.##} kg" + (def.HasDurability ? $" · durability {stack.Durability}/{def.MaxDurability}" : "");
        }

        public void SetSelected(bool on) => UiKit.SetClass(this, "slot--selected", on);
        public void SetActive(bool on) => UiKit.SetClass(this, "slot--active", on);
    }
}
