using System;
using System.Collections.Generic;
using Afterfall.Core;

namespace Afterfall.Domain.World
{
    /// <summary>Which way a route goes at a junction (or where it ends).</summary>
    public enum TurnKind
    {
        Straight,
        SlightLeft,
        Left,
        SharpLeft,
        SlightRight,
        Right,
        SharpRight,

        /// <summary>The route leaves the road for the last stretch across country.</summary>
        LeaveRoad,
        Arrive
    }

    /// <summary>A manoeuvre on a route: how far along it is and which way it goes.</summary>
    public struct RouteTurn
    {
        public float Along;
        public Vec3 Position;
        public TurnKind Kind;

        public static string Describe(TurnKind kind)
        {
            switch (kind)
            {
                case TurnKind.SlightLeft: return "Bear left";
                case TurnKind.Left: return "Turn left";
                case TurnKind.SharpLeft: return "Sharp left";
                case TurnKind.SlightRight: return "Bear right";
                case TurnKind.Right: return "Turn right";
                case TurnKind.SharpRight: return "Sharp right";
                case TurnKind.LeaveRoad: return "Leave the road";
                case TurnKind.Arrive: return "Arrive";
                default: return "Straight on";
            }
        }
    }

    /// <summary>
    /// A way to a destination: from the start to the nearest road, along the road network, and from
    /// the road to the destination, with the turns at junctions. Points are on the ground plan (XZ).
    /// </summary>
    public sealed class RoadRoute
    {
        public List<Vec3> Points { get; } = new List<Vec3>();

        /// <summary>Distance from the start to each point.</summary>
        public List<float> Along { get; } = new List<float>();

        public List<RouteTurn> Turns { get; } = new List<RouteTurn>();

        public float Length => Along.Count > 0 ? Along[Along.Count - 1] : 0f;

        /// <summary>Metres of the route away from roads (to the first road and from the last).</summary>
        public float OffRoad { get; internal set; }

        /// <summary>True when no road helps and the route is a straight line.</summary>
        public bool Direct { get; internal set; }

        public Vec3 Destination => Points.Count > 0 ? Points[Points.Count - 1] : Vec3.Zero;

        internal void Add(Vec3 p)
        {
            if (Points.Count > 0 && Vec3.DistanceXZ(Points[Points.Count - 1], p) < 0.05f) return;
            Along.Add(Points.Count == 0 ? 0f : Along[Along.Count - 1] + Vec3.DistanceXZ(Points[Points.Count - 1], p));
            Points.Add(p);
        }

        /// <summary>
        /// Where <paramref name="p"/> is on the route: the distance from the start of the closest point
        /// on it, and how far off the route <paramref name="p"/> is. <paramref name="hint"/> carries the
        /// segment found last time, so progress is followed forward rather than jumping to a parallel stretch.
        /// </summary>
        public float Locate(Vec3 p, ref int hint, out float offRoute)
        {
            offRoute = Points.Count > 0 ? Vec3.DistanceXZ(p, Points[0]) : 0f;
            if (Points.Count < 2) return 0f;
            int last = Points.Count - 2;
            hint = Math.Max(0, Math.Min(hint, last));
            float along = Search(p, Math.Max(0, hint - 12), Math.Min(last, hint + 400), ref hint, out offRoute);
            if (offRoute > 60f)
            {
                int full = hint;
                float a = Search(p, 0, last, ref full, out float off);
                if (off < offRoute - 1f)
                {
                    hint = full;
                    offRoute = off;
                    along = a;
                }
            }
            return along;
        }

        private float Search(Vec3 p, int from, int to, ref int hint, out float offRoute)
        {
            offRoute = float.MaxValue;
            float along = 0f;
            for (int i = from; i <= to; i++)
            {
                float d = WorldLayout.DistanceToSegmentXZ(p.X, p.Z, Points[i], Points[i + 1], out float t);
                if (d >= offRoute) continue;
                offRoute = d;
                hint = i;
                along = Along[i] + (Along[i + 1] - Along[i]) * t;
            }
            return along;
        }

        /// <summary>The first turn more than a few metres beyond <paramref name="along"/>.</summary>
        public bool NextTurn(float along, out RouteTurn turn)
        {
            foreach (RouteTurn t in Turns)
                if (t.Along > along + 3f)
                {
                    turn = t;
                    return true;
                }
            turn = default;
            return false;
        }
    }

    /// <summary>
    /// The drivable road network as a graph: every road (not the railways) split where roads cross or
    /// where one ends against another, with paved roads preferred over tracks. Routes go from the
    /// nearest road to the start, along the network, to the nearest road to the destination, unless
    /// going straight across country is quicker. Built once from the roads known; pure and thread-safe
    /// to query.
    /// </summary>
    public sealed class RoadNetwork
    {
        /// <summary>Going across country counts as this many metres of road per metre.</summary>
        public const float OffRoadFactor = 3f;

        public const float UnpavedFactor = 1.3f;

        /// <summary>How far from a road the start or the destination may be for the road to be used.</summary>
        public const float SnapRange = 6000f;

        private const float Cell = 128f;
        private const float Merge = 0.35f;

        private struct Edge
        {
            public int A, B;
            public float Length, Cost;

            /// <summary>Part of the world's fixed road network (not a street of a generated chunk).</summary>
            public bool Major;
        }

        private struct Seg
        {
            public Vec3 A, B;
            public int Road, Index;
            public float Half, Factor;
            public bool First, Last, Major;
        }

        private readonly List<Vec3> _nodes = new List<Vec3>();
        private readonly List<List<int>> _adjacent = new List<List<int>>();
        private readonly List<Edge> _edges = new List<Edge>();
        private readonly Dictionary<long, List<int>> _edgeGrid = new Dictionary<long, List<int>>();
        private readonly Dictionary<long, List<int>> _nodeGrid = new Dictionary<long, List<int>>();

        public int NodeCount => _nodes.Count;
        public int EdgeCount => _edges.Count;

        /// <summary>How many roads meet at the network node nearest <paramref name="p"/> (within 1 m), or 0.</summary>
        public int DegreeAt(Vec3 p)
        {
            int n = FindNode(p, 1f);
            return n < 0 ? 0 : _adjacent[n].Count;
        }

        /// <summary>
        /// Builds the network from <paramref name="roads"/>; the first <paramref name="major"/> of them are the
        /// world's fixed roads, which a route may always cross country to reach (the streets of generated
        /// chunks come after them and may stop short of the rest).
        /// </summary>
        public static RoadNetwork Build(IReadOnlyList<RoadPath> roads, int major = int.MaxValue)
        {
            var net = new RoadNetwork();
            var segs = new List<Seg>();
            var grid = new Dictionary<long, List<int>>();
            for (int r = 0; r < roads.Count; r++)
            {
                RoadPath road = roads[r];
                if (road.Rail || road.Points.Count < 2) continue;
                for (int i = 0; i + 1 < road.Points.Count; i++)
                {
                    Vec3 a = road.Points[i], b = road.Points[i + 1];
                    if (Vec3.DistanceXZ(a, b) < 0.01f) continue;
                    Insert(grid, a, b, 0f, segs.Count);
                    segs.Add(new Seg
                    {
                        A = a, B = b, Road = r, Index = i, Half = road.Width * 0.5f, Factor = road.Paved ? 1f : UnpavedFactor,
                        First = i == 0, Last = i + 2 == road.Points.Count, Major = r < major
                    });
                }
            }

            var splits = new List<(float T, Vec3 P)>[segs.Count];
            void Split(int s, float t, Vec3 p) => (splits[s] ?? (splits[s] = new List<(float, Vec3)>())).Add((t, p));
            var links = new List<(Vec3 From, Vec3 To)>();
            var near = new HashSet<int>();

            // Crossings between roads (and between distant parts of one road).
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                near.Clear();
                Query(grid, s.A, s.B, 0f, near);
                foreach (int j in near)
                {
                    if (j <= i) continue;
                    Seg o = segs[j];
                    if (o.Road == s.Road && Math.Abs(o.Index - s.Index) <= 1) continue;
                    if (!WorldGenerator.IntersectTU(s.A, s.B, o.A, o.B, out float t, out float u)) continue;
                    t = Math.Max(0f, Math.Min(1f, t));
                    u = Math.Max(0f, Math.Min(1f, u));
                    // Use an end point exactly when the crossing is at one, so both sides meet at one node.
                    Vec3 p = t < 1e-3f ? s.A : t > 1f - 1e-3f ? s.B : u < 1e-3f ? o.A : u > 1f - 1e-3f ? o.B : Vec3.Lerp(s.A, s.B, t);
                    Split(i, t, p);
                    Split(j, u, p);
                }
            }

            // Road ends that stop against another road (T-junctions, streets meeting at chunk edges).
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                for (int end = 0; end < 2; end++)
                {
                    if (end == 0 ? !s.First : !s.Last) continue;
                    Vec3 e = end == 0 ? s.A : s.B;
                    near.Clear();
                    Query(grid, e, e, 8f, near);
                    int best = -1;
                    float bestD = float.MaxValue, bestT = 0f;
                    foreach (int j in near)
                    {
                        Seg o = segs[j];
                        if (o.Road == s.Road) continue;
                        float d = WorldLayout.DistanceToSegmentXZ(e.X, e.Z, o.A, o.B, out float u);
                        if (d > Math.Max(o.Half, 2f) + 2f || d >= bestD) continue;
                        best = j;
                        bestD = d;
                        bestT = u;
                    }
                    if (best < 0) continue;
                    Seg hit = segs[best];
                    Vec3 q = bestT < 1e-3f ? hit.A : bestT > 1f - 1e-3f ? hit.B : Vec3.Lerp(hit.A, hit.B, bestT);
                    Split(best, bestT, q);
                    if (bestD > Merge) links.Add((e, q));
                }
            }

            // Edges between consecutive points along every segment.
            var chain = new List<(float T, Vec3 P)>();
            for (int i = 0; i < segs.Count; i++)
            {
                Seg s = segs[i];
                chain.Clear();
                chain.Add((0f, s.A));
                if (splits[i] != null) chain.AddRange(splits[i]);
                chain.Add((1f, s.B));
                chain.Sort((x, y) => x.T.CompareTo(y.T));
                int prev = net.NodeFor(chain[0].P);
                for (int k = 1; k < chain.Count; k++)
                {
                    int n = net.NodeFor(chain[k].P);
                    if (n == prev) continue;
                    net.AddEdge(prev, n, s.Factor, s.Major);
                    prev = n;
                }
            }
            foreach ((Vec3 from, Vec3 to) in links) net.AddEdge(net.NodeFor(from), net.NodeFor(to), 1f, false);
            return net;
        }

        private void AddEdge(int a, int b, float factor, bool major)
        {
            if (a == b) return;
            foreach (int e in _adjacent[a])
                if (_edges[e].A == b || _edges[e].B == b) return;
            float length = Vec3.DistanceXZ(_nodes[a], _nodes[b]);
            int id = _edges.Count;
            _edges.Add(new Edge { A = a, B = b, Length = length, Cost = length * factor, Major = major });
            _adjacent[a].Add(id);
            _adjacent[b].Add(id);
            Insert(_edgeGrid, _nodes[a], _nodes[b], 0f, id);
        }

        private int NodeFor(Vec3 p)
        {
            int found = FindNode(p, Merge);
            if (found >= 0) return found;
            int id = _nodes.Count;
            _nodes.Add(p);
            _adjacent.Add(new List<int>(2));
            long key = Key((int)Math.Floor(p.X / 0.5f), (int)Math.Floor(p.Z / 0.5f));
            if (!_nodeGrid.TryGetValue(key, out List<int> list)) _nodeGrid[key] = list = new List<int>(1);
            list.Add(id);
            return id;
        }

        private int FindNode(Vec3 p, float within)
        {
            int kx = (int)Math.Floor(p.X / 0.5f), kz = (int)Math.Floor(p.Z / 0.5f);
            int reach = Math.Max(1, (int)Math.Ceiling(within / 0.5f));
            int best = -1;
            float bestD = within;
            for (int dz = -reach; dz <= reach; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    if (!_nodeGrid.TryGetValue(Key(kx + dx, kz + dz), out List<int> list)) continue;
                    foreach (int n in list)
                    {
                        float d = Vec3.DistanceXZ(_nodes[n], p);
                        if (d > bestD) continue;
                        bestD = d;
                        best = n;
                    }
                }
            return best;
        }

        private static long Key(int x, int z) => ((long)x << 32) ^ (uint)z;

        private static void Insert(Dictionary<long, List<int>> grid, Vec3 a, Vec3 b, float margin, int id)
        {
            int x0 = (int)Math.Floor((Math.Min(a.X, b.X) - margin) / Cell), x1 = (int)Math.Floor((Math.Max(a.X, b.X) + margin) / Cell);
            int z0 = (int)Math.Floor((Math.Min(a.Z, b.Z) - margin) / Cell), z1 = (int)Math.Floor((Math.Max(a.Z, b.Z) + margin) / Cell);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    long k = Key(x, z);
                    if (!grid.TryGetValue(k, out List<int> list)) grid[k] = list = new List<int>(4);
                    list.Add(id);
                }
        }

        private static void Query(Dictionary<long, List<int>> grid, Vec3 a, Vec3 b, float margin, HashSet<int> into)
        {
            int x0 = (int)Math.Floor((Math.Min(a.X, b.X) - margin) / Cell), x1 = (int)Math.Floor((Math.Max(a.X, b.X) + margin) / Cell);
            int z0 = (int)Math.Floor((Math.Min(a.Z, b.Z) - margin) / Cell), z1 = (int)Math.Floor((Math.Max(a.Z, b.Z) + margin) / Cell);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (grid.TryGetValue(Key(x, z), out List<int> list))
                        foreach (int id in list) into.Add(id);
        }

        /// <summary>A place where a route may join or leave the network: an edge, where along it, how far away.</summary>
        private struct Access
        {
            public int Edge;
            public float T, Distance;
            public Vec3 Point;
        }

        /// <summary>
        /// The roads near <paramref name="p"/>: the nearest within <paramref name="maxDistance"/> and the others
        /// little further away (so a route can pick the better of two roads either side), nearest first.
        /// </summary>
        private List<Access> Near(Vec3 p, float maxDistance)
        {
            List<Access> near = Near(p, maxDistance, false);
            // The local streets may not be joined to the rest yet: the fixed roads near by are a way on too.
            bool major = false;
            foreach (Access a in near)
                if (_edges[a.Edge].Major) major = true;
            if (!major) near.AddRange(Near(p, maxDistance, true));
            return near;
        }

        private List<Access> Near(Vec3 p, float maxDistance, bool majorOnly)
        {
            var found = new List<Access>();
            float nearest = float.MaxValue;
            int cx = (int)Math.Floor(p.X / Cell), cz = (int)Math.Floor(p.Z / Cell);
            int rings = (int)Math.Ceiling(maxDistance / Cell);
            for (int ring = 0; ring <= rings; ring++)
            {
                // Every road within (ring - 1) cells' distance has been seen by now.
                if (nearest + AccessSlack <= (ring - 1) * Cell) break;
                for (int dz = -ring; dz <= ring; dz++)
                    for (int dx = -ring; dx <= ring; dx++)
                    {
                        if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue;
                        if (!_edgeGrid.TryGetValue(Key(cx + dx, cz + dz), out List<int> list)) continue;
                        foreach (int e in list)
                        {
                            Edge ed = _edges[e];
                            if (majorOnly && !ed.Major) continue;
                            float d = WorldLayout.DistanceToSegmentXZ(p.X, p.Z, _nodes[ed.A], _nodes[ed.B], out float u);
                            if (d > maxDistance) continue;
                            nearest = Math.Min(nearest, d);
                            found.Add(new Access { Edge = e, T = u, Distance = d, Point = Vec3.Lerp(_nodes[ed.A], _nodes[ed.B], u) });
                        }
                    }
            }
            // An edge spanning several cells is found more than once.
            found.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance) : a.Edge.CompareTo(b.Edge));
            var unique = new List<Access>();
            var seen = new HashSet<int>();
            foreach (Access a in found)
            {
                if (a.Distance > nearest + AccessSlack || unique.Count >= MaxAccess) break;
                if (seen.Add(a.Edge)) unique.Add(a);
            }
            return unique;
        }

        private const float AccessSlack = 150f;
        private const int MaxAccess = 48;

        /// <summary>
        /// The quickest way from <paramref name="from"/> to <paramref name="to"/>, counting road metres
        /// (tracks a little dearer) and cross-country metres at <see cref="OffRoadFactor"/> times.
        /// </summary>
        public RoadRoute Route(Vec3 from, Vec3 to)
        {
            var route = new RoadRoute();
            float straight = Vec3.DistanceXZ(from, to);
            List<Access> starts = straight > 1f ? Near(from, SnapRange) : new List<Access>();
            List<Access> goals = starts.Count > 0 ? Near(to, SnapRange) : new List<Access>();
            if (starts.Count > 0 && goals.Count > 0)
            {
                float best = straight * OffRoadFactor;
                // Joining and leaving the same stretch of road.
                int sameS = -1, sameG = -1;
                for (int i = 0; i < starts.Count; i++)
                    for (int j = 0; j < goals.Count; j++)
                    {
                        if (starts[i].Edge != goals[j].Edge) continue;
                        float c = (starts[i].Distance + goals[j].Distance) * OffRoadFactor + Math.Abs(goals[j].T - starts[i].T) * _edges[starts[i].Edge].Cost;
                        if (c >= best) continue;
                        best = c;
                        sameS = i;
                        sameG = j;
                    }
                int[] came = Search(starts, goals, to, ref best, out int goalNode, out int[] seededBy, out int goalAccess);
                Access s, g;
                if (goalNode >= 0)
                {
                    var path = new List<int>();
                    int first = goalNode;
                    for (int n = goalNode; n >= 0; n = came[n])
                    {
                        path.Add(n);
                        first = n;
                    }
                    path.Reverse();
                    s = starts[seededBy[first]];
                    g = goals[goalAccess];
                    route.Add(from);
                    route.Add(s.Point);
                    var at = new List<int>(path.Count);
                    foreach (int n in path)
                    {
                        route.Add(_nodes[n]);
                        at.Add(route.Points.Count - 1);
                    }
                    route.Add(g.Point);
                    route.Add(to);
                    for (int k = 0; k < path.Count; k++)
                        if (_adjacent[path[k]].Count >= 3) AddTurn(route, at[k]);
                }
                else if (sameS >= 0)
                {
                    s = starts[sameS];
                    g = goals[sameG];
                    route.Add(from);
                    route.Add(s.Point);
                    route.Add(g.Point);
                    route.Add(to);
                }
                else return Straight(route, from, to, straight);
                route.OffRoad = s.Distance + g.Distance;
                if (g.Distance > 30f) route.Turns.Add(new RouteTurn { Along = route.Length - g.Distance, Position = g.Point, Kind = TurnKind.LeaveRoad });
                route.Turns.Add(new RouteTurn { Along = route.Length, Position = to, Kind = TurnKind.Arrive });
                return route;
            }
            return Straight(route, from, to, straight);
        }

        private static RoadRoute Straight(RoadRoute route, Vec3 from, Vec3 to, float straight)
        {
            route.Add(from);
            route.Add(to);
            route.Direct = true;
            route.OffRoad = straight;
            route.Turns.Add(new RouteTurn { Along = route.Length, Position = to, Kind = TurnKind.Arrive });
            return route;
        }

        /// <summary>
        /// A* from the ends of the roads near the start to the ends of those near the destination. Returns
        /// the predecessor of every node; <paramref name="goalNode"/> is the node the best way leaves the
        /// network from (or -1 if none beats <paramref name="best"/>), <paramref name="seededBy"/> which start
        /// access seeded each first node, and <paramref name="goalAccess"/> the destination access used.
        /// </summary>
        private int[] Search(List<Access> starts, List<Access> goals, Vec3 to, ref float best, out int goalNode, out int[] seededBy, out int goalAccess)
        {
            goalNode = -1;
            goalAccess = -1;
            int n = _nodes.Count;
            var cost = new float[n];
            var came = new int[n];
            var closed = new bool[n];
            seededBy = new int[n];
            for (int i = 0; i < n; i++)
            {
                cost[i] = float.MaxValue;
                came[i] = -1;
            }
            // Leaving the network: the cheapest finish from each node at the end of a road near the destination.
            var finish = new Dictionary<int, (float Cost, int Access)>();
            for (int j = 0; j < goals.Count; j++)
            {
                Edge g = _edges[goals[j].Edge];
                float off = goals[j].Distance * OffRoadFactor;
                void Finish(int node, float c)
                {
                    if (!finish.TryGetValue(node, out (float Cost, int Access) had) || c < had.Cost) finish[node] = (c, j);
                }
                Finish(g.A, off + goals[j].T * g.Cost);
                Finish(g.B, off + (1f - goals[j].T) * g.Cost);
            }
            var open = new MinHeap();
            int[] seeds = seededBy;
            for (int i = 0; i < starts.Count; i++)
            {
                Edge s = _edges[starts[i].Edge];
                float off = starts[i].Distance * OffRoadFactor;
                void Seed(int node, float c)
                {
                    if (c >= cost[node]) return;
                    cost[node] = c;
                    seeds[node] = i;
                    open.Push(c + Vec3.DistanceXZ(_nodes[node], to), node);
                }
                Seed(s.A, off + starts[i].T * s.Cost);
                Seed(s.B, off + (1f - starts[i].T) * s.Cost);
            }
            while (open.Count > 0)
            {
                open.Pop(out float f, out int node);
                if (f >= best) break;
                if (closed[node]) continue;
                closed[node] = true;
                float here = cost[node];
                if (finish.TryGetValue(node, out (float Cost, int Access) end) && here + end.Cost < best)
                {
                    best = here + end.Cost;
                    goalNode = node;
                    goalAccess = end.Access;
                }
                foreach (int e in _adjacent[node])
                {
                    Edge ed = _edges[e];
                    int other = ed.A == node ? ed.B : ed.A;
                    if (closed[other]) continue;
                    float c = here + ed.Cost;
                    if (c >= cost[other]) continue;
                    cost[other] = c;
                    came[other] = node;
                    open.Push(c + Vec3.DistanceXZ(_nodes[other], to), other);
                }
            }
            return came;
        }

        /// <summary>Records the turn at route point <paramref name="index"/>, judged over 20 m either side.</summary>
        private static void AddTurn(RoadRoute route, int index)
        {
            if (index <= 0 || index >= route.Points.Count - 1) return;
            Vec3 p = route.Points[index];
            Vec3 before = PointAt(route, route.Along[index] - 20f), after = PointAt(route, route.Along[index] + 20f);
            float ix = p.X - before.X, iz = p.Z - before.Z, ox = after.X - p.X, oz = after.Z - p.Z;
            if (ix * ix + iz * iz < 1f || ox * ox + oz * oz < 1f) return;
            // Positive is to the left with X east and Z north.
            float angle = (float)(Math.Atan2(ix * oz - iz * ox, ix * ox + iz * oz) * 180.0 / Math.PI);
            float a = Math.Abs(angle);
            if (a < 22f) return;
            TurnKind kind = a < 50f ? (angle > 0 ? TurnKind.SlightLeft : TurnKind.SlightRight)
                : a < 135f ? (angle > 0 ? TurnKind.Left : TurnKind.Right)
                : (angle > 0 ? TurnKind.SharpLeft : TurnKind.SharpRight);
            route.Turns.Add(new RouteTurn { Along = route.Along[index], Position = p, Kind = kind });
        }

        private static Vec3 PointAt(RoadRoute route, float along)
        {
            if (along <= 0f) return route.Points[0];
            for (int i = 0; i + 1 < route.Points.Count; i++)
            {
                if (route.Along[i + 1] < along) continue;
                float span = route.Along[i + 1] - route.Along[i];
                return span <= 0f ? route.Points[i] : Vec3.Lerp(route.Points[i], route.Points[i + 1], (along - route.Along[i]) / span);
            }
            return route.Points[route.Points.Count - 1];
        }

        /// <summary>A binary min-heap of (priority, node).</summary>
        private sealed class MinHeap
        {
            private readonly List<(float F, int N)> _items = new List<(float, int)>();

            public int Count => _items.Count;

            public void Push(float f, int n)
            {
                _items.Add((f, n));
                int i = _items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (_items[parent].F <= f) break;
                    _items[i] = _items[parent];
                    i = parent;
                }
                _items[i] = (f, n);
            }

            public void Pop(out float f, out int n)
            {
                (f, n) = _items[0];
                (float F, int N) last = _items[_items.Count - 1];
                _items.RemoveAt(_items.Count - 1);
                if (_items.Count == 0) return;
                int i = 0;
                while (true)
                {
                    int l = i * 2 + 1;
                    if (l >= _items.Count) break;
                    int c = l + 1 < _items.Count && _items[l + 1].F < _items[l].F ? l + 1 : l;
                    if (_items[c].F >= last.F) break;
                    _items[i] = _items[c];
                    i = c;
                }
                _items[i] = last;
            }
        }
    }
}
