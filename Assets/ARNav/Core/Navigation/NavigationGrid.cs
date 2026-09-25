using System;
using System.Collections.Generic;
using ARNav.Core.Geometry;

namespace ARNav.Core.Navigation
{
    /// <summary>Session-bound occupancy grid with 8-connected, no-corner-cut A* routing.</summary>
    public sealed class NavigationGrid
    {
        public readonly string sessionId, sourceSha256;
        public readonly int width, height;
        public readonly float cellSize;
        public readonly LocalPoint origin;
        private readonly bool[] walkable;
        private int[] componentIds;
        private int componentCount;
        public int Count { get { return walkable.Length; } }
        public int ComponentCount { get { return componentCount; } }

        public NavigationGrid(int width, int height, float cellSize, LocalPoint origin, string sessionId, string sourceSha256)
        {
            if (width <= 0 || height <= 0 || cellSize <= 0) throw new ArgumentException("Invalid navigation grid dimensions.");
            this.width = width; this.height = height; this.cellSize = cellSize; this.origin = origin;
            this.sessionId = sessionId; this.sourceSha256 = sourceSha256; walkable = new bool[width * height];
        }
        public bool InBounds(int x, int y) { return x >= 0 && y >= 0 && x < width && y < height; }
        public bool IsWalkable(int x, int y) { return InBounds(x, y) && walkable[y * width + x]; }
        public void SetWalkable(int x, int y, bool value) { if (InBounds(x, y)) { walkable[y * width + x] = value; componentIds = null; componentCount = 0; } }
        public int GetComponentId(int x, int y)
        { if (!IsWalkable(x, y)) return -1; if (componentIds == null) BuildConnectedComponents(); return componentIds[y * width + x]; }

        /// <summary>Labels 8-connected walkable regions using the same no-corner-cut rule as A*.</summary>
        public int BuildConnectedComponents()
        {
            componentIds = new int[walkable.Length]; for (var i = 0; i < componentIds.Length; i++) componentIds[i] = -1;
            componentCount = 0;
            var queue = new Queue<int>(); var dx = new[] { -1, 1, 0, 0, -1, -1, 1, 1 }; var dy = new[] { 0, 0, -1, 1, -1, 1, -1, 1 };
            for (var seed = 0; seed < walkable.Length; seed++)
            {
                if (!walkable[seed] || componentIds[seed] >= 0) continue;
                var id = componentCount++; componentIds[seed] = id; queue.Enqueue(seed);
                while (queue.Count > 0)
                {
                    var at = queue.Dequeue(); var x = at % width; var y = at / width;
                    for (var i = 0; i < dx.Length; i++)
                    {
                        var nx = x + dx[i]; var ny = y + dy[i]; if (!IsWalkable(nx, ny)) continue;
                        if (i >= 4 && (!IsWalkable(nx, y) || !IsWalkable(x, ny))) continue;
                        var next = ny * width + nx; if (componentIds[next] >= 0) continue; componentIds[next] = id; queue.Enqueue(next);
                    }
                }
            }
            return componentCount;
        }
        public LocalPoint CellCenter(int x, int y) { return new LocalPoint(origin.x + (x + .5f) * cellSize, origin.z + (y + .5f) * cellSize); }
        public bool TryWorldToCell(LocalPoint point, out int x, out int y)
        { x = (int)Math.Floor((point.x - origin.x) / cellSize); y = (int)Math.Floor((point.z - origin.z) / cellSize); return InBounds(x, y); }

        public bool TryFindWalkable(LocalPoint point, Func<LocalPoint, bool> constraint, int maxRadiusCells, out int cellX, out int cellY)
        {
            cellX = cellY = -1;
            if (!TryWorldToCell(point, out var sx, out var sy)) return false;
            var best = double.PositiveInfinity;
            for (var radius = 0; radius <= maxRadiusCells; radius++)
            for (var y = Math.Max(0, sy - radius); y <= Math.Min(height - 1, sy + radius); y++)
            for (var x = Math.Max(0, sx - radius); x <= Math.Min(width - 1, sx + radius); x++)
            {
                if (Math.Max(Math.Abs(x - sx), Math.Abs(y - sy)) != radius || !IsWalkable(x, y)) continue;
                var p = CellCenter(x, y); if (constraint != null && !constraint(p)) continue;
                var dx = p.x - point.x; var dz = p.z - point.z; var d = dx * dx + dz * dz;
                if (d < best) { best = d; cellX = x; cellY = y; }
            }
            return cellX >= 0;
        }

        public bool HasLineOfSight(int x0, int y0, int x1, int y1)
        {
            var dx = Math.Abs(x1 - x0); var dy = Math.Abs(y1 - y0); var sx = x0 < x1 ? 1 : -1; var sy = y0 < y1 ? 1 : -1; var err = dx - dy; var x = x0; var y = y0;
            while (true)
            {
                if (!IsWalkable(x, y)) return false;
                if (x == x1 && y == y1) return true;
                var e2 = 2 * err; var nx = x; var ny = y;
                if (e2 > -dy) { err -= dy; nx += sx; }
                if (e2 < dx) { err += dx; ny += sy; }
                if (nx != x && ny != y && (!IsWalkable(nx, y) || !IsWalkable(x, ny))) return false;
                x = nx; y = ny;
            }
        }

        public bool TryFindPath(int sx, int sy, int gx, int gy, out List<LocalPoint> simplified, out int explored, out int rawLength)
        {
            simplified = new List<LocalPoint>(); explored = rawLength = 0;
            if (!IsWalkable(sx, sy) || !IsWalkable(gx, gy)) return false;
            var size = width * height; var g = new float[size]; var parent = new int[size]; var closed = new bool[size];
            for (var i = 0; i < size; i++) { g[i] = float.PositiveInfinity; parent[i] = -1; }
            var start = sy * width + sx; var goal = gy * width + gx; g[start] = 0;
            var open = new MinHeap(); open.Push(start, Heuristic(sx, sy, gx, gy));
            var ox = new[] { -1, 1, 0, 0, -1, -1, 1, 1 }; var oy = new[] { 0, 0, -1, 1, -1, 1, -1, 1 };
            while (open.Count > 0)
            {
                var current = open.Pop(); if (closed[current]) continue; closed[current] = true; explored++;
                if (current == goal) break;
                var x = current % width; var y = current / width;
                for (var i = 0; i < 8; i++)
                {
                    var nx = x + ox[i]; var ny = y + oy[i]; if (!IsWalkable(nx, ny)) continue;
                    if (i >= 4 && (!IsWalkable(nx, y) || !IsWalkable(x, ny))) continue;
                    var ni = ny * width + nx; if (closed[ni]) continue;
                    var tentative = g[current] + (i < 4 ? 1f : 1.41421356f);
                    if (tentative >= g[ni]) continue;
                    g[ni] = tentative; parent[ni] = current; open.Push(ni, tentative + Heuristic(nx, ny, gx, gy));
                }
            }
            if (!closed[goal]) return false;
            var cells = new List<int>(); for (var at = goal; at >= 0; at = parent[at]) { cells.Add(at); if (at == start) break; }
            if (cells[cells.Count - 1] != start) return false;
            cells.Reverse(); rawLength = cells.Count;
            var kept = new List<int> { cells[0] }; var anchor = 0;
            while (anchor < cells.Count - 1)
            {
                var furthest = anchor + 1;
                for (var test = cells.Count - 1; test > anchor + 1; test--)
                    if (HasLineOfSight(cells[anchor] % width, cells[anchor] / width, cells[test] % width, cells[test] / width)) { furthest = test; break; }
                kept.Add(cells[furthest]); anchor = furthest;
            }
            foreach (var cell in kept) simplified.Add(CellCenter(cell % width, cell / width));
            return simplified.Count >= 2;
        }

        private static float Heuristic(int x, int y, int gx, int gy)
        { var dx = Math.Abs(gx - x); var dy = Math.Abs(gy - y); return Math.Max(dx, dy) + (1.41421356f - 1f) * Math.Min(dx, dy); }

        private sealed class MinHeap
        {
            private readonly List<Entry> items = new List<Entry>(); public int Count { get { return items.Count; } }
            public void Push(int node, float priority) { var e = new Entry(node, priority); items.Add(e); var i = items.Count - 1; while (i > 0) { var p = (i - 1) / 2; if (items[p].priority <= priority) break; items[i] = items[p]; i = p; } items[i] = e; }
            public int Pop() { var result = items[0].node; var last = items[items.Count - 1]; items.RemoveAt(items.Count - 1); if (items.Count == 0) return result; var i = 0; while (true) { var l = i * 2 + 1; if (l >= items.Count) break; var r = l + 1; var c = r < items.Count && items[r].priority < items[l].priority ? r : l; if (items[c].priority >= last.priority) break; items[i] = items[c]; i = c; } items[i] = last; return result; }
            private struct Entry { public int node; public float priority; public Entry(int n, float p) { node = n; priority = p; } }
        }
    }
}
