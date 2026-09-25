using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using ARNav.Core.Generation;
using ARNav.Core.Location;
using ARNav.Core.Navigation;
using UnityEngine;

namespace ARNav.Runtime.Navigation
{
    /// <summary>
    /// Creates one transient walkable surface from the union of semantic walkable
    /// regions. Room polygons remain semantic data; this rasterized union is the
    /// authoritative NavMesh floor and therefore cannot overlap itself.
    /// </summary>
    public static class NavigationFloorBuilder
    {
        public sealed class Result
        {
            public Mesh mesh;
            public int cells;
            public int vertices;
            public int triangles;
            public int rejectedCells;
            public Bounds bounds;
        }

        public static bool TryBuild(FloorData floor, float cellSizeMetres, out Result result, out string error)
        {
            result = null; error = null;
            if (floor == null || floor.rooms == null) { error = "Navigation floor has no floor data."; return false; }
            cellSizeMetres = Mathf.Clamp(cellSizeMetres, 0.025f, 0.25f);
            var rooms = new List<RoomData>();
            var bounds = new Bounds(); var hasBounds = false;
            foreach (var room in floor.rooms)
            {
                if (room == null || !room.walkable) continue;
                if ((room.rasterRuns == null || room.rasterRuns.Count == 0) && (room.polygon == null || room.polygon.vertices == null || room.polygon.vertices.Count < 3 || !PolygonTriangulator.IsSimple(room.polygon.vertices))) continue;
                rooms.Add(room);
                if (room.rasterRuns != null && room.rasterRuns.Count > 0) foreach (var run in room.rasterRuns)
                {
                    foreach (var p in new[] { new LocalPoint(run.minX, run.minZ), new LocalPoint(run.maxX, run.maxZ) })
                    { var point = new Vector3(p.x, floor.elevationMetres, p.z); if (!hasBounds) { bounds = new Bounds(point, Vector3.zero); hasBounds = true; } else bounds.Encapsulate(point); }
                }
                else foreach (var p in room.polygon.vertices) { var point = new Vector3(p.x, floor.elevationMetres, p.z); if (!hasBounds) { bounds = new Bounds(point, Vector3.zero); hasBounds = true; } else bounds.Encapsulate(point); }
            }
            if (!hasBounds || rooms.Count == 0) { error = "No valid walkable semantic regions are available."; return false; }
            var minX = Mathf.FloorToInt(bounds.min.x / cellSizeMetres) - 1;
            var maxX = Mathf.CeilToInt(bounds.max.x / cellSizeMetres) + 1;
            var minZ = Mathf.FloorToInt(bounds.min.z / cellSizeMetres) - 1;
            var maxZ = Mathf.CeilToInt(bounds.max.z / cellSizeMetres) + 1;
            var width = maxX - minX; var height = maxZ - minZ;
            if (width <= 0 || height <= 0 || (long)width * height > 1000000) { error = "Navigation floor raster bounds are invalid."; return false; }

            var vertices = new List<Vector3>(); var triangles = new List<int>(); var cellCount = 0; var rejected = 0;
            var y = floor.elevationMetres + 0.01f;
            for (var z = minZ; z < maxZ; z++) for (var x = minX; x < maxX; x++)
            {
                var px = (x + 0.5f) * cellSizeMetres; var pz = (z + 0.5f) * cellSizeMetres;
                var point = new LocalPoint(px, pz); var inside = false;
                for (var i = 0; i < rooms.Count; i++) if (RoomNavigationPointService.Contains(rooms[i], point)) { inside = true; break; }
                if (!inside) continue;
                var baseIndex = vertices.Count;
                // X/Z is the floor plane. This winding produces +Y normals.
                vertices.Add(new Vector3(x * cellSizeMetres, y, z * cellSizeMetres));
                vertices.Add(new Vector3((x + 1) * cellSizeMetres, y, z * cellSizeMetres));
                vertices.Add(new Vector3((x + 1) * cellSizeMetres, y, (z + 1) * cellSizeMetres));
                vertices.Add(new Vector3(x * cellSizeMetres, y, (z + 1) * cellSizeMetres));
                triangles.Add(baseIndex); triangles.Add(baseIndex + 2); triangles.Add(baseIndex + 1);
                triangles.Add(baseIndex); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 2);
                cellCount++;
            }
            if (triangles.Count == 0) { error = "Walkable semantic regions produced no navigation cells."; return false; }
            var mesh = new Mesh { name = "NavigationFloorMesh" };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices); mesh.SetTriangles(triangles, 0, true); mesh.RecalculateBounds(); mesh.RecalculateNormals();
            result = new Result { mesh = mesh, cells = cellCount, vertices = vertices.Count, triangles = triangles.Count / 3, rejectedCells = rejected, bounds = mesh.bounds };
            return true;
        }

        public static GameObject AddTo(GameObject generatedRoot, FloorData floor, float cellSizeMetres, out Result result, out string error)
        {
            result = null; error = null;
            if (!TryBuild(floor, cellSizeMetres, out result, out error)) return null;
            var obj = new GameObject("NavigationFloorMesh", typeof(MeshFilter), typeof(MeshRenderer));
            obj.transform.SetParent(generatedRoot.transform, false);
            obj.GetComponent<MeshFilter>().sharedMesh = result.mesh;
            obj.GetComponent<MeshRenderer>().enabled = false;
            return obj;
        }
    }
}
