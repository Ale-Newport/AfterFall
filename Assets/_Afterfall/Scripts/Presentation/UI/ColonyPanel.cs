using System.Collections.Generic;
using Afterfall.Core;
using Afterfall.Domain.Colony;
using Afterfall.Domain.Events;
using UnityEngine;
using UnityEngine.UIElements;
using EntityId = Afterfall.Core.EntityId;

namespace Afterfall.Presentation.UI
{
    /// <summary>
    /// Strategic colony overview: each recruited colonist's status, needs, current job and recent
    /// failures, with per-job-type work priorities (off, 1 = highest … 4 = lowest).
    /// </summary>
    public sealed class ColonyPanel
    {
        private static readonly JobType[] WorkTypes = { JobType.Haul, JobType.Build };
        private readonly GameController _gc;
        private readonly VisualElement _list;
        private readonly Label _summary;
        private readonly Dictionary<EntityId, (Label Status, Label Needs, Label Failures, Dictionary<JobType, Button> Buttons)> _rows =
            new Dictionary<EntityId, (Label, Label, Label, Dictionary<JobType, Button>)>();
        private int _builtCount = -1;

        public VisualElement Root { get; }

        public ColonyPanel(GameController gc)
        {
            _gc = gc;
            Root = UiKit.Div("col");
            Root.Add(UiKit.Text("Colony", "subtitle"));
            _summary = Root.Put(UiKit.Text("", "muted"));
            _list = Root.Put(UiKit.Div("col"));
        }

        public void Refresh()
        {
            var colonists = new List<ColonistState>();
            foreach (ColonistState c in _gc.Session.World.Colonists.Values)
                if (c.Recruited) colonists.Add(c);
            colonists.Sort((a, b) => a.Id.CompareTo(b.Id));
            int openJobs = 0;
            foreach (Job j in _gc.Session.World.Jobs.Values)
                if (j.State == JobState.Open) openJobs++;
            _summary.text = colonists.Count == 0
                ? "No colonists yet. Survivors are hiding at Dawson Farm and Hartley's Workshop — talk to them once you have a spare bedroll."
                : $"{colonists.Count} colonist(s) · {openJobs} job(s) waiting. Colonists haul, build, stock machines and eat from your storage crates.";
            if (colonists.Count != _builtCount) Rebuild(colonists);
            foreach (ColonistState c in colonists)
            {
                if (!_rows.TryGetValue(c.Id, out var row)) continue;
                row.Status.text = c.Activity == ColonistActivity.Down ? c.Status : $"{c.Activity}: {c.Status}";
                row.Needs.text = $"Health {c.Vitals.Health:0} · food {c.Vitals.Hunger:0}% · water {c.Vitals.Thirst:0}% · carrying {c.Carry.TotalWeight:0.#} kg";
                row.Failures.text = c.RecentFailures.Count > 0 ? "Recent problem: " + c.RecentFailures[0] : "";
                foreach (JobType t in WorkTypes)
                {
                    int p = c.Work.PriorityFor(t);
                    row.Buttons[t].text = $"{t}: {(p == 0 ? "off" : p.ToString())}";
                    UiKit.SetClass(row.Buttons[t], "btn--disabled", p == 0);
                }
            }
        }

        private void Rebuild(List<ColonistState> colonists)
        {
            _builtCount = colonists.Count;
            _list.Clear();
            _rows.Clear();
            foreach (ColonistState c in colonists)
            {
                EntityId id = c.Id;
                var box = _list.Put(UiKit.Div("list-item", "col"));
                box.style.alignItems = Align.Stretch;
                var head = box.Put(UiKit.Div("row"));
                head.Add(UiKit.Text(c.Name, "subtitle", "grow"));
                head.Add(UiKit.Btn("Locate", () =>
                {
                    if (_gc.Session.World.Colonists.TryGetValue(id, out ColonistState cc)) _gc.CameraRig.Tactical.Focus(cc.Position.ToUnity(), 0f);
                }, "btn--small"));
                Label status = box.Put(UiKit.Text("", "wrap"));
                Label needs = box.Put(UiKit.Text("", "muted"));
                Label failures = box.Put(UiKit.Text("", "warn"));
                var buttons = new Dictionary<JobType, Button>();
                var row = box.Put(UiKit.Div("row"));
                foreach (JobType t in WorkTypes)
                {
                    JobType type = t;
                    buttons[t] = row.Put(UiKit.Btn("", () =>
                    {
                        if (!_gc.Session.World.Colonists.TryGetValue(id, out ColonistState cc)) return;
                        int p = cc.Work.PriorityFor(type);
                        int next = p == 0 ? 1 : p >= 4 ? 0 : p + 1; // off → 1 → 2 → 3 → 4 → off
                        OpResult r = _gc.Session.SetWorkPriority(id, type, next);
                        _gc.Toast(r.Message, r.Ok ? Severity.Info : Severity.Warning);
                    }, "btn--small"));
                }
                _rows[id] = (status, needs, failures, buttons);
            }
        }
    }
}
