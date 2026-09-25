using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using UnityEngine;

namespace ARNav.Runtime.Generation
{
    /// <summary>
    /// Builds an exact-footprint solid surface from non-overlapping raster rectangles.
    /// Tops are greedily merged and only exposed vertical edges are emitted; hidden
    /// internal faces and the unseen bottom are omitted.
    /// </summary>
    public static class ArchitecturalWallMaskMeshBuilder
    {
        public static Mesh Build(IList<LocalRasterRun> runs, float baseY, float height)
        {
            if (runs == null || runs.Count == 0 || !(height > 0f)) return null;

            var xs = new List<float>(runs.Count * 2);
            var zs = new List<float>(runs.Count * 2);
            foreach (var run in runs)
            {
                if (!Valid(run)) continue;
                xs.Add(run.minX); xs.Add(run.maxX);
                zs.Add(run.minZ); zs.Add(run.maxZ);
            }
            SortUnique(xs); SortUnique(zs);
            if (xs.Count < 2 || zs.Count < 2) return null;

            var xIndex = MakeIndex(xs); var zIndex = MakeIndex(zs);
            var cellCount = checked((long)(xs.Count - 1) * (zs.Count - 1));
            if (cellCount > int.MaxValue) throw new InvalidOperationException("Wall raster is too large to mesh.");
            var occupied = new bool[zs.Count - 1, xs.Count - 1];
            foreach (var run in runs)
            {
                if (!Valid(run)) continue;
                var x0 = xIndex[run.minX]; var x1 = xIndex[run.maxX];
                var z0 = zIndex[run.minZ]; var z1 = zIndex[run.maxZ];
                for (var z = z0; z < z1; z++) for (var x = x0; x < x1; x++) occupied[z, x] = true;
            }

            var vertices = new List<Vector3>();
            var indices = new List<int>();
            // Reuse vertices across coplanar quads. Keying by both position and
            // face normal keeps architectural corners hard while removing the
            // coincident-but-unwelded edges that can show as shading seams.
            var weldedVertices = new Dictionary<VertexKey, int>();
            var topVisited = new bool[zs.Count - 1, xs.Count - 1];
            var topY = baseY + height;

            // Greedy maximal rectangles make the top surface independent of the
            // source run partition while preserving every raster cell boundary.
            for (var z = 0; z < zs.Count - 1; z++)
            for (var x = 0; x < xs.Count - 1; x++)
            {
                if (!occupied[z, x] || topVisited[z, x]) continue;
                var endX = x + 1;
                while (endX < xs.Count - 1 && occupied[z, endX] && !topVisited[z, endX]) endX++;
                var endZ = z + 1;
                while (endZ < zs.Count - 1)
                {
                    var complete = true;
                    for (var xx = x; xx < endX; xx++)
                        if (!occupied[endZ, xx] || topVisited[endZ, xx]) { complete = false; break; }
                    if (!complete) break;
                    endZ++;
                }
                for (var zz = z; zz < endZ; zz++) for (var xx = x; xx < endX; xx++) topVisited[zz, xx] = true;
                AddQuad(vertices, indices, weldedVertices,
                    new Vector3(xs[x], topY, zs[z]), new Vector3(xs[x], topY, zs[endZ]),
                    new Vector3(xs[endX], topY, zs[endZ]), new Vector3(xs[endX], topY, zs[z]));
            }

            // Emit only sides adjacent to empty space and merge contiguous spans
            // along each grid line. This removes internal faces and prevents each
            // source scanline/cell edge from becoming a separate coplanar quad.
            for (var x = 0; x < xs.Count; x++)
            {
                var z = 0;
                while (z < zs.Count - 1)
                {
                    var left = x > 0 && occupied[z, x - 1];
                    var right = x < xs.Count - 1 && occupied[z, x];
                    if (left == right) { z++; continue; }
                    var start = z; var outwardPositiveX = left;
                    while (z + 1 < zs.Count - 1)
                    {
                        var nextLeft = x > 0 && occupied[z + 1, x - 1];
                        var nextRight = x < xs.Count - 1 && occupied[z + 1, x];
                        if (nextLeft == nextRight || nextLeft != outwardPositiveX) break;
                        z++;
                    }
                    var z0 = zs[start]; var z1 = zs[z + 1]; var planeX = xs[x];
                    if (outwardPositiveX)
                        AddQuad(vertices, indices, weldedVertices, new Vector3(planeX, baseY, z0), new Vector3(planeX, topY, z0), new Vector3(planeX, topY, z1), new Vector3(planeX, baseY, z1));
                    else
                        AddQuad(vertices, indices, weldedVertices, new Vector3(planeX, baseY, z0), new Vector3(planeX, baseY, z1), new Vector3(planeX, topY, z1), new Vector3(planeX, topY, z0));
                    z++;
                }
            }

            for (var z = 0; z < zs.Count; z++)
            {
                var x = 0;
                while (x < xs.Count - 1)
                {
                    var below = z > 0 && occupied[z - 1, x];
                    var above = z < zs.Count - 1 && occupied[z, x];
                    if (below == above) { x++; continue; }
                    var start = x; var outwardPositiveZ = below;
                    while (x + 1 < xs.Count - 1)
                    {
                        var nextBelow = z > 0 && occupied[z - 1, x + 1];
                        var nextAbove = z < zs.Count - 1 && occupied[z, x + 1];
                        if (nextBelow == nextAbove || nextBelow != outwardPositiveZ) break;
                        x++;
                    }
                    var x0 = xs[start]; var x1 = xs[x + 1]; var planeZ = zs[z];
                    if (outwardPositiveZ)
                        AddQuad(vertices, indices, weldedVertices, new Vector3(x0, baseY, planeZ), new Vector3(x1, baseY, planeZ), new Vector3(x1, topY, planeZ), new Vector3(x0, topY, planeZ));
                    else
                        AddQuad(vertices, indices, weldedVertices, new Vector3(x0, baseY, planeZ), new Vector3(x0, topY, planeZ), new Vector3(x1, topY, planeZ), new Vector3(x1, baseY, planeZ));
                    x++;
                }
            }

            if (vertices.Count == 0) return null;
            var mesh = new Mesh
            {
                name = "Architectural Wall Mask Mesh",
                indexFormat = vertices.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0, true);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static bool Valid(LocalRasterRun run)
        {
            return run != null && !float.IsNaN(run.minX) && !float.IsNaN(run.maxX) &&
                   !float.IsNaN(run.minZ) && !float.IsNaN(run.maxZ) &&
                   !float.IsInfinity(run.minX) && !float.IsInfinity(run.maxX) &&
                   !float.IsInfinity(run.minZ) && !float.IsInfinity(run.maxZ) &&
                   run.maxX > run.minX && run.maxZ > run.minZ;
        }

        private static void SortUnique(List<float> values)
        {
            values.Sort();
            var write = 0;
            for (var read = 0; read < values.Count; read++)
                if (write == 0 || values[read] != values[write - 1]) values[write++] = values[read];
            if (write < values.Count) values.RemoveRange(write, values.Count - write);
        }

        private static Dictionary<float, int> MakeIndex(List<float> values)
        {
            var result = new Dictionary<float, int>(values.Count);
            for (var i = 0; i < values.Count; i++) result.Add(values[i], i);
            return result;
        }

        private static void AddQuad(List<Vector3> vertices, List<int> indices, Dictionary<VertexKey, int> weldedVertices, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            var normal = Vector3.Cross(b - a, c - a).normalized;
            var ia = GetVertex(a, normal, vertices, weldedVertices);
            var ib = GetVertex(b, normal, vertices, weldedVertices);
            var ic = GetVertex(c, normal, vertices, weldedVertices);
            var id = GetVertex(d, normal, vertices, weldedVertices);
            indices.Add(ia); indices.Add(ib); indices.Add(ic);
            indices.Add(ia); indices.Add(ic); indices.Add(id);
        }

        private static int GetVertex(Vector3 position, Vector3 normal, List<Vector3> vertices, Dictionary<VertexKey, int> weldedVertices)
        {
            var key = new VertexKey(position, normal);
            if (weldedVertices.TryGetValue(key, out var index)) return index;
            index = vertices.Count;
            vertices.Add(position);
            weldedVertices.Add(key, index);
            return index;
        }

        private struct VertexKey : IEquatable<VertexKey>
        {
            private readonly Vector3 position;
            private readonly Vector3 normal;
            public VertexKey(Vector3 position, Vector3 normal) { this.position = position; this.normal = normal; }
            public bool Equals(VertexKey other) { return position.Equals(other.position) && normal.Equals(other.normal); }
            public override bool Equals(object obj) { return obj is VertexKey other && Equals(other); }
            public override int GetHashCode() { unchecked { return (position.GetHashCode() * 397) ^ normal.GetHashCode(); } }
        }
    }
}
