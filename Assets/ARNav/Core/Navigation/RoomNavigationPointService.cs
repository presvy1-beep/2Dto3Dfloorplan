using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Generation;
using ARNav.Core.Geometry;
using ARNav.Core.Location;
using ARNav.Core.Validation;

namespace ARNav.Core.Navigation
{
    /// <summary>Finds an interior room candidate without assuming rooms are rectangles or convex.</summary>
    public static class RoomNavigationPointService
    {
        public static bool Contains(RoomData room, LocalPoint point)
        {
            if (room != null && room.rasterRuns != null && room.rasterRuns.Count > 0)
            {
                foreach (var run in room.rasterRuns) if (run != null && point.x >= run.minX && point.x <= run.maxX && point.z >= run.minZ && point.z <= run.maxZ) return true;
                return false;
            }
            if (room == null || !RegionLocator.Contains(point, room.polygon)) return false;
            if (room.holes != null) foreach (var hole in room.holes) if (RegionLocator.Contains(point, hole)) return false;
            return true;
        }
        public static float DistanceToBoundary(LocalPoint point, RoomData room)
        {
            if (room == null) return 0;
            if (room.rasterRuns != null && room.rasterRuns.Count > 0)
            {
                var best = float.PositiveInfinity;
                foreach (var run in room.rasterRuns) if (run != null && point.x >= run.minX && point.x <= run.maxX && point.z >= run.minZ && point.z <= run.maxZ)
                    best = Math.Min(best, Math.Min(Math.Min(point.x - run.minX, run.maxX - point.x), Math.Min(point.z - run.minZ, run.maxZ - point.z)));
                return float.IsPositiveInfinity(best) ? 0 : best;
            }
            return (float)RegionLocator.DistanceToBoundary(point, room.polygon);
        }
        /// <summary>Returns deterministic interior candidates with the preferred polygon-centroid/bounds-centre order.</summary>
        public static List<LocalPoint> GetInteriorCandidates(RoomData room)
        {
            var candidates = new List<LocalPoint>();
            if (room != null && room.rasterRuns != null && room.rasterRuns.Count > 0)
            {
                foreach (var run in room.rasterRuns) if (run != null)
                {
                    for (var z = 1; z <= 5; z++) for (var x = 1; x <= 5; x++) candidates.Add(new LocalPoint(run.minX + (run.maxX - run.minX) * x / 6f, run.minZ + (run.maxZ - run.minZ) * z / 6f));
                }
                return candidates;
            }
            if (room == null || room.polygon == null || room.polygon.vertices == null || room.polygon.vertices.Count < 3) return candidates;
            var vertices = room.polygon.vertices;
            var minX = vertices[0].x; var maxX = minX; var minZ = vertices[0].z; var maxZ = minZ;
            var area2 = 0f; var centroidX6A = 0f; var centroidZ6A = 0f;
            for (var i = 0; i < vertices.Count; i++)
            {
                var a = vertices[i]; var b = vertices[(i + 1) % vertices.Count]; var cross = a.x * b.z - b.x * a.z;
                area2 += cross; centroidX6A += (a.x + b.x) * cross; centroidZ6A += (a.z + b.z) * cross;
                minX = Math.Min(minX, a.x); maxX = Math.Max(maxX, a.x); minZ = Math.Min(minZ, a.z); maxZ = Math.Max(maxZ, a.z);
            }
            if (Math.Abs(area2) > .000001f) candidates.Add(new LocalPoint(centroidX6A / (3f * area2), centroidZ6A / (3f * area2)));
            candidates.Add(new LocalPoint((minX + maxX) * .5f, (minZ + maxZ) * .5f));
            // A 9x9 bbox grid handles concave rooms and centroid failures. Points are
            // filtered by the polygon at the call site, so no candidate can escape its room.
            for (var z = 1; z <= 19; z++) for (var x = 1; x <= 19; x++) candidates.Add(new LocalPoint(minX + (maxX - minX) * x / 20f, minZ + (maxZ - minZ) * z / 20f));
            return candidates;
        }

        public static bool TryFindInteriorPoint(RoomData room, out LocalPoint point, out string error)
        {
            point = default(LocalPoint); error = null; if (room == null || ((room.rasterRuns == null || room.rasterRuns.Count == 0) && (room.polygon == null || room.polygon.vertices == null))) { error = "Room has no polygon or raster mask."; return false; }
            if (room.rasterRuns != null && room.rasterRuns.Count > 0)
            {
                var candidatesFromMask = GetInteriorCandidates(room); var bestMask = float.MinValue; var foundMask = false;
                foreach (var candidate in candidatesFromMask) if (Contains(room, candidate)) { var d = DistanceToBoundary(candidate, room); if (d > bestMask) { bestMask = d; point = candidate; foundMask = true; } }
                if (!foundMask) error = "Raster mask has no interior cell candidate.";
                return foundMask;
            }
            var vertices = room.polygon.vertices; if (!PolygonTriangulator.TryTriangulate(vertices, out var triangles, out error)) return false;
            var candidates = GetInteriorCandidates(room);
            for (var i = 0; i < triangles.Count; i += 3)
            { var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]]; candidates.Add(new LocalPoint((a.x + b.x + c.x) / 3f, (a.z + b.z + c.z) / 3f)); }
            // Keep a true interior point with the best clearance: bbox centres and
            // centroids are only preferences, never accepted without containment.
            var bestDistance = float.MinValue; var found = false;
            foreach (var candidate in candidates) if (Contains(room, candidate)) { var distance = (float)RegionLocator.DistanceToBoundary(candidate, room.polygon); if (!found || distance > bestDistance) { bestDistance = distance; point = candidate; found = true; } }
            if (!found) { error = "Room has no interior navigation candidate."; return false; } return true;
        }

        public static bool TryFindWalkableInteriorPoint(RoomData room, FloorData floor, float wallClearance, out LocalPoint point, out string error)
        {
            point = default(LocalPoint); error = null;
            if (!TryFindInteriorPoint(room, out _, out error)) return false;
            var candidates = GetInteriorCandidates(room);
            var vertices = room.polygon == null ? null : room.polygon.vertices;
            if (vertices != null && PolygonTriangulator.TryTriangulate(vertices, out var triangles, out _))
                for (var i = 0; i < triangles.Count; i += 3)
                { var a = vertices[triangles[i]]; var b = vertices[triangles[i + 1]]; var c = vertices[triangles[i + 2]]; candidates.Add(new LocalPoint((a.x + b.x + c.x) / 3f, (a.z + b.z + c.z) / 3f)); }
            var best = float.MinValue; var found = false;
            foreach (var candidate in candidates)
            {
                if (!Contains(room, candidate) || !RouteGeometryValidator.IsPointClearOfSolidWalls(candidate, floor, wallClearance, out _)) continue;
                var clearance = Math.Min(DistanceToBoundary(candidate, room), RouteGeometryValidator.DistanceToSolidWallFootprints(candidate, floor));
                if (clearance > best) { best = clearance; point = candidate; found = true; }
            }
            if (!found) error = "No polygon-interior point clears solid wall footprints.";
            return found;
        }
        public static RoomData FindRoom(FloorData floor, string roomId) { if (floor != null && floor.rooms != null) foreach (var room in floor.rooms) if (room != null && room.id == roomId) return room; return null; }
    }
}
