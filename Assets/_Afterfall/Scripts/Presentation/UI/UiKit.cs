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

        /// <summary>Replaces any good/bad/warn tone class with <paramref name="tone"/> (null clears it).</summary>
        public static void SetTone(VisualElement e, string tone)
        {
            e.RemoveFromClassList("good");
            e.RemoveFromClassList("bad");
            e.RemoveFromClassList("warn");
            if (!string.IsNullOrEmpty(tone)) e.AddToClassList(tone);
        }

        /// <summary>An item's icon as a fixed-size element ("item-icon" plus a size class such as "icon-24").</summary>
        public static VisualElement ItemIcon(ItemDefinition def, string sizeClass = "icon-24")
        {
            var e = Div("item-icon", sizeClass);
            e.pickingMode = PickingMode.Ignore;
            SetIcon(e, def);
            return e;
        }

        /// <summary>Shows the cached icon of <paramref name="def"/> as the element's background (none for null).</summary>
        public static void SetIcon(VisualElement e, ItemDefinition def)
        {
            Texture2D tex = ItemIcons.Get(def);
            e.style.backgroundImage = tex != null ? new StyleBackground(tex) : new StyleBackground(StyleKeyword.None);
        }

        /// <summary>A UI glyph ("arrow", "lock", "hand", "clock") in the icon style.</summary>
        public static VisualElement GlyphIcon(string name, string sizeClass = "icon-20")
        {
            var e = Div("item-icon", sizeClass);
            e.pickingMode = PickingMode.Ignore;
            e.style.backgroundImage = new StyleBackground(ItemIcons.Glyph(name));
            return e;
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

    /// <summary>
    /// An inline item reference: a small icon and a short text such as "Log 2/5" or "×3", with an
    /// optional good/bad/warn tone. Update it with <see cref="Set"/> instead of rebuilding.
    /// </summary>
    public sealed class ItemChip : VisualElement
    {
        private readonly VisualElement _icon;
        private readonly Label _label;
        private string _itemId;

        public ItemChip(string sizeClass = "icon-20")
        {
            AddToClassList("chip");
            pickingMode = PickingMode.Ignore;
            _icon = this.Put(UiKit.Div("item-icon", sizeClass));
            _icon.pickingMode = PickingMode.Ignore;
            _label = this.Put(UiKit.Text("", "chip-label"));
            _label.pickingMode = PickingMode.Ignore;
        }

        public void Set(ItemDefinition def, string text, string tone = null)
        {
            string id = def?.Id;
            if (id != _itemId)
            {
                _itemId = id;
                UiKit.SetIcon(_icon, def);
            }
            _label.text = text;
            UiKit.Show(_label, !string.IsNullOrEmpty(text));
            UiKit.SetTone(_label, tone);
        }
    }

    /// <summary>One inventory slot: illustrated item icon, quantity, durability, hotbar key.</summary>
    public sealed class ItemSlot : VisualElement
    {
        private readonly VisualElement _icon, _durTrack, _dur;
        private readonly Label _qty, _key;
        private string _shownId;
        public int Index;
        public Action<ItemSlot, PointerDownEvent> Clicked;

        /// <summary>Name and description shown in the runtime hover tooltip (VisualElement.tooltip is editor-only).</summary>
        public string HoverText { get; private set; } = "";

        public ItemSlot(int index, string key = null)
        {
            Index = index;
            AddToClassList("slot");
            style.backgroundImage = new StyleBackground(ItemIcons.SlotShade);
            _icon = this.Put(UiKit.Div("slot-icon"));
            _icon.pickingMode = PickingMode.Ignore;
            _durTrack = this.Put(UiKit.Div("slot-dur-track"));
            _durTrack.pickingMode = PickingMode.Ignore;
            _dur = _durTrack.Put(UiKit.Div("slot-dur"));
            _dur.pickingMode = PickingMode.Ignore;
            _durTrack.style.display = DisplayStyle.None;
            _qty = this.Put(UiKit.Text("", "slot-qty"));
            _qty.pickingMode = PickingMode.Ignore;
            _key = this.Put(UiKit.Text(key ?? "", "slot-key"));
            _key.pickingMode = PickingMode.Ignore;
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
                if (_shownId != null)
                {
                    _shownId = null;
                    _icon.style.backgroundImage = new StyleBackground(StyleKeyword.None);
                    RemoveFromClassList("slot--filled");
                }
                _qty.text = "";
                _durTrack.style.display = DisplayStyle.None;
                HoverText = "";
                return;
            }
            if (_shownId != def.Id)
            {
                // Icons are cached textures; only swap them when the slot's item changes.
                _shownId = def.Id;
                UiKit.SetIcon(_icon, def);
                AddToClassList("slot--filled");
            }
            _qty.text = stack.Quantity > 1 ? stack.Quantity.ToString() : "";
            if (def.HasDurability)
            {
                float f = Mathf.Clamp01(stack.Durability / (float)def.MaxDurability);
                _durTrack.style.display = DisplayStyle.Flex;
                _dur.style.width = Length.Percent(f * 100f);
                _dur.style.backgroundColor = Color.Lerp(new Color(0.9f, 0.3f, 0.2f), new Color(0.45f, 0.82f, 0.35f), f);
            }
            else _durTrack.style.display = DisplayStyle.None;
            HoverText = $"{def.Name}{(stack.Quantity > 1 ? $" ×{stack.Quantity}" : "")}\n{def.Description}\n{def.Weight * stack.Quantity:0.##} kg" + (def.HasDurability ? $" · durability {stack.Durability}/{def.MaxDurability}" : "");
        }

        public void SetSelected(bool on) => UiKit.SetClass(this, "slot--selected", on);
        public void SetActive(bool on) => UiKit.SetClass(this, "slot--active", on);
    }
}
