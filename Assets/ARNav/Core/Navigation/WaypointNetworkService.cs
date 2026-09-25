using System;
using System.Collections.Generic;
using ARNav.Core.Geometry;

namespace ARNav.Core.Navigation
{
    public sealed class WaypointGridNode
    {
        public string roomId;
        public int cellX;
        public int cellY;
        public int componentId;
    }

    public sealed class WaypointNetworkEdge
    {
        public string fromRoomId;
        public string toRoomId;
        public int componentId;
        public int exploredNodes;
        public int rawCellCount;
        public readonly List<LocalPoint> path = new List<LocalPoint>();
    }

    public sealed class WaypointNetworkResult
    {
        public readonly List<WaypointNetworkEdge> edges = new List<WaypointNetworkEdge>();
        public int componentCount;
        public int waypointCount;
        public int sameComponentPathFailures;
    }

    /// <summary>Builds a deterministic minimum spanning forest of actual grid-A* routes.</summary>
    public static class WaypointNetworkService
    {
        public static WaypointNetworkResult Build(NavigationGrid grid, IList<WaypointGridNode> nodes)
        {
            if (grid == null) throw new ArgumentNullException("grid");
            var result = new WaypointNetworkResult { componentCount = grid.ComponentCount, waypointCount = nodes == null ? 0 : nodes.Count };
            if (nodes == null || nodes.Count < 2) return result;

            var parent = new int[nodes.Count];
            for (var i = 0; i < parent.Length; i++) parent[i] = i;
            var candidates = new List<Candidate>();
            for (var i = 0; i < nodes.Count; i++)
            {
                var a = nodes[i];
                if (a == null || a.componentId < 0 || grid.GetComponentId(a.cellX, a.cellY) != a.componentId) continue;
                for (var j = i + 1; j < nodes.Count; j++)
                {
                    var b = nodes[j];
                    if (b == null || a.componentId != b.componentId || grid.GetComponentId(b.cellX, b.cellY) != b.componentId) continue;
                    if (!grid.TryFindPath(a.cellX, a.cellY, b.cellX, b.cellY, out var path, out var explored, out var rawCount))
                    { result.sameComponentPathFailures++; continue; }
                    var edge = new WaypointNetworkEdge { fromRoomId = a.roomId, toRoomId = b.roomId, componentId = a.componentId, exploredNodes = explored, rawCellCount = rawCount };
                    edge.path.AddRange(path);
                    candidates.Add(new Candidate(i, j, rawCount, edge));
                }
            }

            candidates.Sort((a, b) => { var c = a.cost.CompareTo(b.cost); if (c != 0) return c; c = a.a.CompareTo(b.a); return c != 0 ? c : a.b.CompareTo(b.b); });
            foreach (var candidate in candidates)
            {
                var ra = Find(parent, candidate.a); var rb = Find(parent, candidate.b);
                if (ra == rb) continue;
                parent[ra] = rb;
                result.edges.Add(candidate.edge);
            }
            return result;
        }

        private static int Find(int[] parent, int value)
        {
            while (parent[value] != value) { parent[value] = parent[parent[value]]; value = parent[value]; }
            return value;
        }

        private sealed class Candidate
        {
            public readonly int a, b, cost;
            public readonly WaypointNetworkEdge edge;
            public Candidate(int a, int b, int cost, WaypointNetworkEdge edge) { this.a = a; this.b = b; this.cost = cost; this.edge = edge; }
        }
    }
}
