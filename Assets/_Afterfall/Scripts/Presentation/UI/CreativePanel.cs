using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Afterfall.Core;
using Afterfall.Domain.Events;
using Afterfall.Domain.Items;
using Afterfall.Domain.Weather;
using Afterfall.Domain.World;
using Afterfall.Services;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation.UI
{
    /// <summary>
    /// The creative/developer panel (F6). Items: a searchable, virtualised browser over the real
    /// item catalogue with favourites and recent items, spawning into the backpack, the world or a
    /// container. World: mode, free flight, teleports, bookmarks, time and weather. Debug: which
    /// diagnostics the F3 overlay shows, and test spawns. Nothing here pauses the game.
    /// </summary>
    public sealed class CreativePanel : IWindow
    {
        private enum Tab
        {
            Items,
            Vehicles,
            World,
            Debug
        }

        private const string FavoritesFilter = "★ Favourites";
        private const string RecentFilter = "Recent";
        private const int MaxRecent = 12;

        private readonly UiManager _ui;
        private readonly GameController _gc;
        private readonly Dictionary<Tab, Button> _tabButtons = new Dictionary<Tab, Button>();
        private readonly Dictionary<Tab, VisualElement> _pages = new Dictionary<Tab, VisualElement>();
        private readonly Label _modeLabel, _lockedNote;
        private readonly Button _enableTools;
        private Tab _tab = Tab.Items;

        // Items tab
        private readonly TextField _search;
        private readonly ListView _list;
        private readonly Dictionary<string, Button> _filterButtons = new Dictionary<string, Button>();
        private readonly List<ItemDefinition> _shown = new List<ItemDefinition>();
        private readonly Label _count, _detailName, _detailInfo, _detailDesc, _targetInfo;
        private readonly VisualElement _detailIcon;
        private readonly IntegerField _quantity;
        private readonly Button _favorite;
        private string _filter = "All";
        private ItemDefinition _selected;

        // Vehicles tab
        private readonly TextField _vehicleSearch;
        private readonly VisualElement _vehicleList;
        private readonly Label _vehicleOptions, _nearestInfo;
        private readonly Toggle _lockedToggle, _keyToggle;
        private readonly Dictionary<float, Button> _fuelButtons = new Dictionary<float, Button>();
        private readonly Dictionary<float, Button> _conditionButtons = new Dictionary<float, Button>();
        private float _spawnFuel = 1f, _spawnCondition = 100f;
        private int _spawnPaint;

        // World tab
        private readonly Label _flightInfo, _positionInfo;
        private readonly VisualElement _destinations;
        private readonly TextField _bookmarkName;
        private readonly TextField _coordX, _coordZ;
        private readonly Button _creativeButton, _survivalButton;
        private int _markersVersion = -1;

        public CreativePanel(UiManager ui, GameController gc)
        {
            _ui = ui;
            _gc = gc;
            Root = UiKit.Div("window", "window-center", "creative");
            var header = Root.Put(UiKit.Div("row"));
            header.Add(UiKit.Text("Creative & Developer", "title"));
            _modeLabel = header.Put(UiKit.Text("", "creative-mode"));
            // Tabs sit on the left, clear of the toasts in the top-right corner.
            foreach (Tab t in new[] { Tab.Items, Tab.Vehicles, Tab.World, Tab.Debug })
            {
                Tab tab = t;
                Button b = header.Put(UiKit.Btn(t.ToString(), () => Show(tab), "creative-tab"));
                _tabButtons[t] = b;
            }
            header.Add(UiKit.Btn("Close (F6)", () => _ui.CloseWindow(), "btn--small"));
            header.Add(UiKit.Div("spacer"));

            var lockedRow = Root.Put(UiKit.Div("row", "creative-locked"));
            _lockedNote = lockedRow.Put(UiKit.Text("Developer tools are off in this Survival game. Enabling them marks this save as using developer tools for good.", "warn", "grow"));
            _enableTools = lockedRow.Put(UiKit.Btn("Enable developer tools", () => Report(_gc.Session.Creative.EnableDeveloperTools()), "btn--primary"));

            // ------------------------------------------------------------ items
            VisualElement items = Page(Tab.Items);
            var searchRow = items.Put(UiKit.Div("row"));
            _search = searchRow.Put(new TextField { value = "" });
            _search.AddToClassList("grow");
            _search.RegisterValueChangedCallback(_ => Filter());
            _count = searchRow.Put(UiKit.Text("", "muted"));
            _count.style.marginLeft = 8;
            var filters = items.Put(UiKit.Div("row", "wrap-row"));
            var filterNames = new List<string> { "All", FavoritesFilter, RecentFilter };
            filterNames.AddRange(Enum.GetNames(typeof(ItemCategory)));
            foreach (string f in filterNames)
            {
                string name = f;
                _filterButtons[f] = filters.Put(UiKit.Btn(Pretty(f), () => SetFilter(name), "btn--small"));
            }
            var body = items.Put(UiKit.Div("row", "grow"));
            body.style.alignItems = Align.Stretch;
            _list = body.Put(new ListView(_shown, 34, MakeRow, BindRow) { selectionType = SelectionType.Single });
            _list.AddToClassList("creative-list");
            _list.selectionChanged += sel => Select(sel.OfType<ItemDefinition>().FirstOrDefault());
            _list.itemsChosen += chosen => SpawnSelected(SpawnTarget.Backpack);

            var detail = body.Put(UiKit.Div("panel", "creative-detail"));
            var dHead = detail.Put(UiKit.Div("row"));
            _detailIcon = dHead.Put(UiKit.ItemIcon(null, "icon-48"));
            _detailName = dHead.Put(UiKit.Text("Choose an item", "subtitle", "grow"));
            _detailName.style.marginLeft = 8;
            _favorite = dHead.Put(UiKit.Btn("☆", ToggleFavorite, "btn--small"));
            _detailInfo = detail.Put(UiKit.Text("", "muted"));
            _detailDesc = detail.Put(UiKit.Text("", "wrap"));
            var qRow = detail.Put(UiKit.Div("row"));
            qRow.Add(UiKit.Text("Quantity", "muted"));
            _quantity = qRow.Put(new IntegerField { value = 1 });
            _quantity.style.width = 90;
            _quantity.RegisterValueChangedCallback(e => _quantity.SetValueWithoutNotify(Mathf.Clamp(e.newValue, 1, CreativeTools.MaxSpawnQuantity)));
            qRow.Add(UiKit.Btn("1", () => _quantity.value = 1, "btn--small"));
            qRow.Add(UiKit.Btn("10", () => _quantity.value = 10, "btn--small"));
            qRow.Add(UiKit.Btn("Stack", () => _quantity.value = Mathf.Max(1, _selected?.MaxStack ?? 1), "btn--small"));
            var actions = detail.Put(UiKit.Div("col"));
            actions.Add(UiKit.Btn("Add to backpack", () => SpawnSelected(SpawnTarget.Backpack), "btn--primary"));
            actions.Add(UiKit.Btn("Drop in front of me", () => SpawnSelected(SpawnTarget.World)));
            actions.Add(UiKit.Btn("Put into container", () => SpawnSelected(SpawnTarget.Container)));
            _targetInfo = detail.Put(UiKit.Text("", "muted"));

            // ------------------------------------------------------------ vehicles
            VisualElement vehicles = Page(Tab.Vehicles);
            var vSearchRow = vehicles.Put(UiKit.Div("row"));
            vSearchRow.Add(UiKit.Text("Search", "muted"));
            _vehicleSearch = vSearchRow.Put(new TextField { value = "" });
            _vehicleSearch.AddToClassList("grow");
            _vehicleSearch.RegisterValueChangedCallback(_ => RebuildVehicleList());
            var vOptions = vehicles.Put(UiKit.Div("row", "wrap-row"));
            vOptions.Add(UiKit.Text("Fuel", "muted"));
            foreach (float f in new[] { 0f, 0.25f, 1f })
            {
                float fuel = f;
                _fuelButtons[f] = vOptions.Put(UiKit.Btn($"{f * 100:0}%", () => { _spawnFuel = fuel; RefreshVehicleOptions(); }, "btn--small"));
            }
            vOptions.Add(UiKit.Text("Condition", "muted"));
            foreach (float c in new[] { 25f, 60f, 100f })
            {
                float cond = c;
                _conditionButtons[c] = vOptions.Put(UiKit.Btn($"{c:0}%", () => { _spawnCondition = cond; RefreshVehicleOptions(); }, "btn--small"));
            }
            _lockedToggle = vOptions.Put(new Toggle("Locked") { value = false });
            _keyToggle = vOptions.Put(new Toggle("Give me its key") { value = true });
            vOptions.Add(UiKit.Btn("Next paint", () => { _spawnPaint++; RefreshVehicleOptions(); }, "btn--small"));
            _vehicleOptions = vehicles.Put(UiKit.Text("", "muted"));
            var vScroll = vehicles.Put(new ScrollView());
            vScroll.AddToClassList("creative-destinations");
            _vehicleList = vScroll.contentContainer;
            vehicles.Add(UiKit.Text("The vehicle you spawned nearest to you", "subtitle"));
            var nearRow = vehicles.Put(UiKit.Div("row"));
            _nearestInfo = nearRow.Put(UiKit.Text("", "muted", "grow"));
            nearRow.Add(UiKit.Btn("Refuel & repair", () => WithNearest(id => Report(_gc.Session.Creative.ResetVehicle(id))), "btn--small"));
            nearRow.Add(UiKit.Btn("Move in front of me", MoveNearest, "btn--small"));
            nearRow.Add(UiKit.Btn("Delete", () => WithNearest(id => Report(_gc.Session.Creative.DeleteVehicle(id))), "btn--small", "btn--danger"));
            RebuildVehicleList();

            // ------------------------------------------------------------ world
            VisualElement world = Page(Tab.World);
            var cols = world.Put(UiKit.Div("row", "grow"));
            cols.style.alignItems = Align.FlexStart;
            var left = cols.Put(UiKit.Div("col", "creative-col"));
            left.Add(UiKit.Text("Mode", "subtitle"));
            var modeRow = left.Put(UiKit.Div("row"));
            _survivalButton = modeRow.Put(UiKit.Btn("Survival", () => Report(_gc.Session.Creative.SetMode(GameMode.Survival)), "btn--small"));
            _creativeButton = modeRow.Put(UiKit.Btn("Creative", () => Report(_gc.Session.Creative.SetMode(GameMode.Creative)), "btn--small"));
            left.Add(UiKit.Text("Creative: free, instant building with every blueprint; the survivor is never harmed. Placement rules still apply.", "muted"));
            left.Add(UiKit.Text("Free flight", "subtitle"));
            var flightRow = left.Put(UiKit.Div("row"));
            flightRow.Add(UiKit.Btn("Toggle flight (F7)", () =>
            {
                _ui.CloseWindow();
                _gc.ToggleFlight();
            }, "btn--small"));
            flightRow.Add(UiKit.Btn("Slower", () => _gc.Player.Flight.SetSpeed(_gc.Player.Flight.Speed * 0.5f), "btn--small"));
            flightRow.Add(UiKit.Btn("Faster", () => _gc.Player.Flight.SetSpeed(_gc.Player.Flight.Speed * 2f), "btn--small"));
            _flightInfo = left.Put(UiKit.Text("", "muted"));
            left.Add(UiKit.Text("Coordinates", "subtitle"));
            _positionInfo = left.Put(UiKit.Text("", "muted"));
            var coordRow = left.Put(UiKit.Div("row"));
            coordRow.Add(UiKit.Text("X", "muted"));
            _coordX = coordRow.Put(new TextField { value = "256" });
            coordRow.Add(UiKit.Text("Z", "muted"));
            _coordZ = coordRow.Put(new TextField { value = "256" });
            _coordX.style.width = 110;
            _coordZ.style.width = 110;
            coordRow.Add(UiKit.Btn("Go", () =>
            {
                if (!float.TryParse(_coordX.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(_coordZ.value, NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                {
                    _gc.Toast("Type the X and Z coordinates in metres (for example 300 and 250).", Severity.Warning);
                    return;
                }
                Teleport(new Vector3(x, 0f, z), string.Format(CultureInfo.InvariantCulture, "({0:0}, {1:0})", x, z));
            }, "btn--small", "btn--primary"));
            var backRow = left.Put(UiKit.Div("row"));
            backRow.Add(UiKit.Btn("Back to previous location", () => { _ui.CloseWindow(); _gc.TeleportBack(); }, "btn--small"));
            var waypointGo = backRow.Put(UiKit.Btn("Go to waypoint", () =>
            {
                MapNavigation nav = _gc.Session.World.Navigation;
                if (!nav.HasWaypoint) _gc.Toast("No waypoint set. Set one on the map (M).", Severity.Warning);
                else Teleport(nav.Waypoint.ToUnity(), nav.WaypointLabel);
            }, "btn--small"));
            left.Add(UiKit.Text("Bookmark this spot", "subtitle"));
            var bmRow = left.Put(UiKit.Div("row"));
            _bookmarkName = bmRow.Put(new TextField { value = "" });
            _bookmarkName.AddToClassList("grow");
            bmRow.Add(UiKit.Btn("Save", () =>
            {
                Report(_gc.Session.Creative.AddBookmark(_bookmarkName.value, _gc.Session.Player.Position));
                _bookmarkName.value = "";
            }, "btn--small"));
            left.Add(UiKit.Text("Time and weather", "subtitle"));
            var timeRow = left.Put(UiKit.Div("row"));
            foreach ((string label, double hour) in new[] { ("Dawn", 6.0), ("Noon", 12.0), ("Dusk", 19.0), ("Night", 23.0) })
            {
                double h = hour;
                timeRow.Add(UiKit.Btn(label, () => Report(_gc.Session.Creative.SkipToHour(h)), "btn--small"));
            }
            var weatherRow = left.Put(UiKit.Div("row"));
            foreach (WeatherKind k in new[] { WeatherKind.Clear, WeatherKind.Overcast, WeatherKind.Rain, WeatherKind.Storm })
            {
                WeatherKind kind = k;
                weatherRow.Add(UiKit.Btn(k.ToString(), () => Report(_gc.Session.Creative.HoldWeather(kind)), "btn--small"));
            }
            weatherRow.Add(UiKit.Btn("Natural", () => Report(_gc.Session.Creative.ResumeWeather()), "btn--small"));

            var right = cols.Put(UiKit.Div("col", "creative-col", "grow"));
            right.Add(UiKit.Text("Teleport", "subtitle"));
            var scroll = right.Put(new ScrollView());
            scroll.AddToClassList("creative-destinations");
            _destinations = scroll.contentContainer;

            // ------------------------------------------------------------ debug
            VisualElement debug = Page(Tab.Debug);
            debug.Add(UiKit.Text("Diagnostics overlay (F3) sections", "subtitle"));
            var toggles = debug.Put(UiKit.Div("row", "wrap-row"));
            foreach (DiagnosticSection s in new[] { DiagnosticSection.Performance, DiagnosticSection.Simulation, DiagnosticSection.Entities, DiagnosticSection.Position, DiagnosticSection.Navigation, DiagnosticSection.Power, DiagnosticSection.Economy, DiagnosticSection.Saves })
            {
                DiagnosticSection section = s;
                var t = toggles.Put(new Toggle(s.ToString()) { value = (_gc.Settings.DiagnosticSections & (int)s) != 0 });
                t.RegisterValueChangedCallback(e =>
                {
                    _gc.Settings.DiagnosticSections = e.newValue ? _gc.Settings.DiagnosticSections | (int)section : _gc.Settings.DiagnosticSections & ~(int)section;
                    _gc.Settings.Save();
                });
                t.style.marginRight = 14;
            }
            var overlayRow = debug.Put(UiKit.Div("row"));
            overlayRow.Add(UiKit.Btn("Show / hide overlay (F3)", () =>
            {
                _gc.Settings.ShowFps = !_gc.Settings.ShowFps;
                _gc.Settings.Save();
            }, "btn--small"));
            debug.Add(UiKit.Text("Test spawns", "subtitle"));
            var testRow = debug.Put(UiKit.Div("row"));
            testRow.Add(UiKit.Btn("Wanderer 12 m ahead", () => Report(_gc.Session.Creative.SpawnInfected(Ahead(12f).ToSim(), "wanderer")), "btn--small"));
            testRow.Add(UiKit.Btn("Runner 20 m ahead", () => Report(_gc.Session.Creative.SpawnInfected(Ahead(20f).ToSim(), "runner")), "btn--small"));
            testRow.Add(UiKit.Btn("Remove all infected", () => Report(_gc.Session.Creative.RemoveAllInfected()), "btn--small"));
            debug.Add(UiKit.Text("Spawned infected use the real perception and NavMesh: crouch, sprint and line of sight change when they notice you.", "muted"));

            SetFilter("All");
            Show(Tab.Items);
        }

        public VisualElement Root { get; }

        public void OnClose() => _gc.Settings.Save();

        public void Refresh()
        {
            CreativeTools tools = _gc.Session.Creative;
            GameSettings settings = _gc.Session.World.Settings;
            _modeLabel.text = settings.ModeLabel();
            UiKit.Show(_lockedNote.parent, !tools.Enabled);
            UiKit.SetClass(_creativeButton, "btn--selected", settings.IsCreative());
            UiKit.SetClass(_survivalButton, "btn--selected", !settings.IsCreative());
            FreeFlight flight = _gc.Player.Flight;
            _flightInfo.text = flight.Active ? $"Flying at {flight.CurrentSpeed:0} m/s (cruise {flight.Speed:0} m/s, ×{FreeFlight.BoostFactor:0} with Shift)" : $"Landed. Cruise speed {flight.Speed:0} m/s; the mouse wheel changes it in flight.";
            Vec3 p = _gc.Session.Player.Position;
            _positionInfo.text = string.Format(CultureInfo.InvariantCulture, "You are at X {0:0.0}, Y {1:0.0}, Z {2:0.0} (map {3:0} m across)", p.X, p.Y, p.Z, _gc.Session.World.Layout.Size);
            _targetInfo.text = ContainerTarget(out string label) != EntityId.None ? $"Container target: {label}" : "Container target: none — open a container first.";
            if (_markersVersion != _gc.Session.World.Navigation.Version) RebuildDestinations();
            Afterfall.Domain.Vehicles.VehicleState near = NearestSpawned();
            _nearestInfo.text = near == null ? "None spawned yet." : string.Format(CultureInfo.InvariantCulture, "{0} {1:0} m away: {2}",
                _gc.Session.Content.Vehicle(near.DefId)?.Name, Vector3.Distance(near.Position.ToUnity(), _gc.Player.transform.position), Afterfall.Domain.Vehicles.VehicleRules.Summary(near, _gc.Session.Content.Vehicle(near.DefId)));
            RefreshVehicleOptions();
        }

        // ------------------------------------------------------------ vehicles

        public void ShowVehicles() => Show(Tab.Vehicles);

        private void RebuildVehicleList()
        {
            _vehicleList.Clear();
            foreach (Afterfall.Domain.Vehicles.VehicleDefinition d in _gc.Session.Creative.VehicleTypes(_vehicleSearch.value))
            {
                Afterfall.Domain.Vehicles.VehicleDefinition def = d;
                var row = _vehicleList.Put(UiKit.Div("list-item"));
                var col = row.Put(UiKit.Div("col", "grow"));
                col.Add(UiKit.Text($"{d.Name} · {d.Category}", "subtitle"));
                col.Add(UiKit.Text($"{d.Description} {d.Seats} seats, {d.Doors} doors, {d.FuelCapacity:0} L tank, {d.MaxSpeedKmh:0} km/h, {(d.FourWheelDrive ? "4x4" : "2WD")}, trunk {d.StorageSlots} slots.", "muted"));
                row.Add(UiKit.Btn("Spawn in front of me", () => SpawnVehicle(def.Id), "btn--small", "btn--primary"));
            }
        }

        private void RefreshVehicleOptions()
        {
            foreach (var kv in _fuelButtons) UiKit.SetClass(kv.Value, "btn--selected", Mathf.Approximately(kv.Key, _spawnFuel));
            foreach (var kv in _conditionButtons) UiKit.SetClass(kv.Value, "btn--selected", Mathf.Approximately(kv.Key, _spawnCondition));
            _vehicleOptions.text = $"Spawns with {_spawnFuel * 100:0}% fuel, {_spawnCondition:0}% condition, paint {_spawnPaint % 4 + 1}, {(_lockedToggle.value ? "locked" : "unlocked")}{(_keyToggle.value ? ", key in your backpack" : ", no key (hotwire it)")}. It is placed on clear, level ground ahead.";
        }

        /// <summary>Spawns a vehicle on clear ground ahead (public so the tour drives the same path as a click).</summary>
        public OpResult SpawnVehicle(string defId)
        {
            var def = _gc.Session.Content.Vehicle(defId);
            if (def == null) return OpResult.Fail("unknown", "Unknown vehicle.");
            if (!_gc.FindVehicleSpot(def, 7f, out Vector3 spot, out float yaw))
            {
                _gc.Toast("No clear, level ground ahead for it.", Severity.Warning);
                return OpResult.Fail("blocked", "No clear ground.");
            }
            var options = new VehicleSpawnOptions { FuelFraction = _spawnFuel, Condition = _spawnCondition, Locked = _lockedToggle.value, GiveKey = _keyToggle.value, Paint = _spawnPaint };
            OpResult r = _gc.Session.Creative.SpawnVehicle(defId, spot.ToSim(), yaw, options, out _);
            Report(r);
            return r;
        }

        private Afterfall.Domain.Vehicles.VehicleState NearestSpawned()
        {
            Afterfall.Domain.Vehicles.VehicleState best = null;
            float bestD = float.MaxValue;
            foreach (var v in _gc.Session.World.Vehicles.Values)
            {
                if (!v.DebugSpawned) continue;
                float d = Vector3.Distance(v.Position.ToUnity(), _gc.Player.transform.position);
                if (d < bestD)
                {
                    bestD = d;
                    best = v;
                }
            }
            return best;
        }

        private void WithNearest(Action<EntityId> action)
        {
            var v = NearestSpawned();
            if (v == null) _gc.Toast("You have not spawned a vehicle.", Severity.Warning);
            else action(v.Id);
        }

        private void MoveNearest()
        {
            var v = NearestSpawned();
            if (v == null)
            {
                _gc.Toast("You have not spawned a vehicle.", Severity.Warning);
                return;
            }
            var def = _gc.Session.Content.Vehicle(v.DefId);
            if (!_gc.FindVehicleSpot(def, 7f, out Vector3 spot, out float yaw)) _gc.Toast("No clear, level ground ahead for it.", Severity.Warning);
            else Report(_gc.Session.Creative.MoveVehicle(v.Id, spot.ToSim(), yaw));
        }

        // ------------------------------------------------------------ tabs

        private VisualElement Page(Tab tab)
        {
            var page = Root.Put(UiKit.Div("col", "grow", "creative-page"));
            _pages[tab] = page;
            return page;
        }

        private void Show(Tab tab)
        {
            _tab = tab;
            foreach (var kv in _pages) UiKit.Show(kv.Value, kv.Key == tab);
            foreach (var kv in _tabButtons) UiKit.SetClass(kv.Value, "btn--selected", kv.Key == tab);
            if (tab == Tab.Items) _search.Focus();
        }

        /// <summary>Selects a tab and filters the item list (the scripted tour uses it).</summary>
        public void ShowItems(string query)
        {
            Show(Tab.Items);
            _search.value = query ?? "";
            Filter();
        }

        public void ShowWorld() => Show(Tab.World);
        public int ShownCount => _shown.Count;

        // ------------------------------------------------------------ item browser

        private static string Pretty(string name) => name == "FoodMedicine" ? "Food & medicine" : name;

        private void SetFilter(string name)
        {
            _filter = name;
            foreach (var kv in _filterButtons) UiKit.SetClass(kv.Value, "btn--selected", kv.Key == name);
            Filter();
        }

        private void Filter()
        {
            ItemCategory? category = Enum.TryParse(_filter, out ItemCategory c) ? c : (ItemCategory?)null;
            List<ItemDefinition> found = _gc.Session.Creative.SearchItems(_search.value, category);
            if (_filter == FavoritesFilter)
            {
                HashSet<string> fav = Ids(_gc.Settings.CreativeFavorites);
                found = found.Where(d => fav.Contains(d.Id)).ToList();
            }
            else if (_filter == RecentFilter)
            {
                List<string> recent = IdList(_gc.Settings.CreativeRecent);
                found = found.Where(d => recent.Contains(d.Id)).OrderBy(d => recent.IndexOf(d.Id)).ToList();
            }
            _shown.Clear();
            _shown.AddRange(found);
            _list.RefreshItems();
            _count.text = $"{_shown.Count} of {_gc.Session.Content.Items.Count} items";
            if (_selected == null || !_shown.Contains(_selected)) Select(_shown.Count > 0 ? _shown[0] : null);
        }

        private VisualElement MakeRow()
        {
            var row = UiKit.Div("row", "creative-row");
            row.Add(UiKit.ItemIcon(null, "icon-28"));
            var name = row.Put(UiKit.Text("", "grow"));
            name.style.marginLeft = 6;
            row.Add(UiKit.Text("", "muted", "creative-id"));
            row.Add(UiKit.Text("", "muted", "creative-cat"));
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            if (index < 0 || index >= _shown.Count) return;
            ItemDefinition d = _shown[index];
            UiKit.SetIcon(row[0], d);
            ((Label)row[1]).text = (Ids(_gc.Settings.CreativeFavorites).Contains(d.Id) ? "★ " : "") + d.Name;
            ((Label)row[2]).text = d.Id;
            ((Label)row[3]).text = $"{Pretty(d.Category.ToString())} · stack {d.MaxStack}";
        }

        private void Select(ItemDefinition d)
        {
            _selected = d;
            UiKit.SetIcon(_detailIcon, d);
            _detailName.text = d?.Name ?? "No item matches";
            _detailInfo.text = d == null ? "" : string.Format(CultureInfo.InvariantCulture, "Id {0} · {1} · stack {2} · {3:0.##} kg{4}{5}",
                d.Id, Pretty(d.Category.ToString()), d.MaxStack, d.Weight,
                d.HasDurability ? $" · durability {d.MaxDurability}" : "",
                d.Tags != null && d.Tags.Length > 0 ? " · tags " + string.Join(", ", d.Tags) : "");
            _detailDesc.text = d?.Description ?? "";
            _favorite.text = d != null && Ids(_gc.Settings.CreativeFavorites).Contains(d.Id) ? "★" : "☆";
        }

        /// <summary>Spawns the selected item (public so the scripted tour drives the same path as a click).</summary>
        public OpResult SpawnSelected(SpawnTarget target)
        {
            if (_selected == null) return OpResult.Fail("none", "Choose an item first.");
            int q = Mathf.Clamp(_quantity.value, 1, CreativeTools.MaxSpawnQuantity);
            EntityId container = ContainerTarget(out _);
            Vector3 ahead = Ahead(1.6f);
            OpResult r = _gc.Session.Creative.SpawnItem(_selected.Id, q, target, ahead.ToSim(), container);
            Report(r);
            if (r.Ok) Remember(_selected.Id);
            return r;
        }

        public void SetQuantity(int q) => _quantity.value = q;

        public bool SelectById(string id)
        {
            int i = _shown.FindIndex(d => d.Id == id);
            if (i < 0) return false;
            _list.SetSelection(i);
            Select(_shown[i]);
            return true;
        }

        private EntityId ContainerTarget(out string label)
        {
            label = "";
            EntityId id = _gc.LastContainer;
            Inventory inv = id != EntityId.None ? _gc.Session.World.FindInventory(id) : null;
            if (inv == null) return EntityId.None;
            label = string.IsNullOrEmpty(inv.Label) ? "container" : inv.Label;
            return id;
        }

        private void Remember(string id)
        {
            List<string> recent = IdList(_gc.Settings.CreativeRecent);
            recent.Remove(id);
            recent.Insert(0, id);
            if (recent.Count > MaxRecent) recent.RemoveRange(MaxRecent, recent.Count - MaxRecent);
            _gc.Settings.CreativeRecent = string.Join(",", recent);
        }

        private void ToggleFavorite()
        {
            if (_selected == null) return;
            List<string> fav = IdList(_gc.Settings.CreativeFavorites);
            if (!fav.Remove(_selected.Id)) fav.Add(_selected.Id);
            _gc.Settings.CreativeFavorites = string.Join(",", fav);
            _gc.Settings.Save();
            Select(_selected);
            _list.RefreshItems();
        }

        private static List<string> IdList(string csv) => (csv ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList();
        private static HashSet<string> Ids(string csv) => new HashSet<string>(IdList(csv), StringComparer.Ordinal);

        // ------------------------------------------------------------ world

        private void RebuildDestinations()
        {
            _markersVersion = _gc.Session.World.Navigation.Version;
            _destinations.Clear();
            foreach (TeleportDestination d in _gc.Session.Creative.Destinations())
            {
                TeleportDestination dest = d;
                var row = _destinations.Put(UiKit.Div("list-item"));
                row.Add(UiKit.Text(d.Name, "grow"));
                row.Add(UiKit.Text(string.Format(CultureInfo.InvariantCulture, "{0} · ({1:0}, {2:0})", d.Kind, d.Position.X, d.Position.Z), "muted"));
                row.Add(UiKit.Btn("Go", () => Teleport(dest.Position.ToUnity(), dest.Name), "btn--small", "btn--primary"));
                if (d.MarkerId != EntityId.None)
                    row.Add(UiKit.Btn("Delete", () => Report(_gc.Session.RemoveMapMarker(dest.MarkerId)), "btn--small", "btn--danger"));
            }
        }

        private void Teleport(Vector3 target, string label)
        {
            if (!_gc.Session.Creative.Enabled)
            {
                _gc.Toast("Enable developer tools first.", Severity.Warning);
                return;
            }
            _ui.CloseWindow();
            _gc.TeleportTo(target, label);
        }

        // ------------------------------------------------------------ debug

        private Vector3 Ahead(float distance)
        {
            Transform t = _gc.Player.transform;
            Vector3 p = t.position + t.forward * distance;
            p.y = _gc.GroundHeight(p);
            return p;
        }

        private void Report(OpResult r)
        {
            if (!string.IsNullOrEmpty(r.Message)) _gc.Toast(r.Message, r.Ok ? Severity.Info : Severity.Warning);
            Refresh();
        }
    }
}
