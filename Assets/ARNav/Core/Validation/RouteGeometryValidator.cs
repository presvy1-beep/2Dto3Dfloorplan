using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geometry;

namespace ARNav.Core.Validation
{
    /// <summary>Geometry-only sanity checks for routes produced by NavMesh.</summary>
    public static class RouteGeometryValidator
    {
        public static float DistanceToSolidWallFootprints(LocalPoint point, FloorData floor)
        {
            var nearest = float.PositiveInfinity;
            if (floor == null || floor.walls == null) return float.PositiveInfinity;
            foreach (var wall in floor.walls) if (wall != null)
            {
                var dx = wall.end.x - wall.start.x; var dz = wall.end.z - wall.start.z; var lengthSquared = dx * dx + dz * dz;
                if (lengthSquared <= .000001f) continue;
                var length = (float)Math.Sqrt(lengthSquared); var tx = dx / length; var tz = dz / length; var nx = -tz; var nz = tx;
                var offsetX = point.x - wall.start.x; var offsetZ = point.z - wall.start.z;
                var localX = offsetX * tx + offsetZ * tz; var localZ = offsetX * nx + offsetZ * nz;
                if (floor.doors != null)
                {
                    var inOpening = false;
                    foreach (var door in floor.doors) if (door != null && door.parentWallId == wall.id && localX >= door.distanceAlongWallMetres - door.widthMetres * .5f && localX <= door.distanceAlongWallMetres + door.widthMetres * .5f) { inOpening = true; break; }
                    if (inOpening) continue;
                }
                var outsideX = Math.Max(0, Math.Max(-localX, localX - length)); var outsideZ = Math.Max(0, Math.Abs(localZ) - wall.thicknessMetres * .5f);
                var distance = (float)Math.Sqrt(outsideX * outsideX + outsideZ * outsideZ);
                if (distance < nearest) nearest = distance;
            }
            return nearest;
        }

        public static bool IsPointClearOfSolidWalls(LocalPoint point, FloorData floor, float clearance, out string wallId)
        {
            wallId = null; if (floor == null || floor.walls == null) return true;
            foreach (var wall in floor.walls) if (wall != null)
            {
                var dx = wall.end.x - wall.start.x; var dz = wall.end.z - wall.start.z; var length = Length(wall); if (length <= .00001f) continue;
                var nx = -dz / length; var nz = dx / length; var local = ToWallSpace(point, wall.start, dx / length, dz / length, nx, nz);
                if (local.x < -clearance || local.x > length + clearance || Math.Abs(local.z) > wall.thicknessMetres * .5f + clearance) continue;
                var inOpening = false; if (floor.doors != null) foreach (var door in floor.doors) if (door != null && door.parentWallId == wall.id && local.x >= door.distanceAlongWallMetres - door.widthMetres * .5f && local.x <= door.distanceAlongWallMetres + door.widthMetres * .5f) { inOpening = true; break; }
                if (!inOpening) { wallId = wall.id; return false; }
            }
            return true;
        }

        public static bool CrossesSolidWall(IList<LocalPoint> route, FloorData floor, out string error)
        {
            error = null;
            if (route == null || floor == null || floor.walls == null) return false;
            for (var i = 0; i + 1 < route.Count; i++) foreach (var wall in floor.walls)
            {
                if (wall == null || !SegmentIntersectsWallFootprint(route[i], route[i + 1], wall, out var routeT, out var wallT)) continue;
                var allowed = false;
                if (floor.doors != null) foreach (var door in floor.doors)
                    if (door != null && door.parentWallId == wall.id && wallT >= (door.distanceAlongWallMetres / Length(wall)) - .001f && wallT <= ((door.distanceAlongWallMetres + door.widthMetres) / Length(wall)) + .001f) { allowed = true; break; }
                if (!allowed) { error = "Route segment " + i + " crosses solid wall " + wall.id + " at t=" + routeT.ToString("0.###"); return true; }
            }
            return false;
        }

        /// <summary>Checks the authoritative wall footprint, including wall thickness.</summary>
        public static bool SegmentIntersectsWallFootprint(LocalPoint a, LocalPoint b, WallData wall, out float routeT, out float wallT)
        {
            routeT = wallT = 0; if (wall == null) return false;
            var dx = wall.end.x - wall.start.x; var dz = wall.end.z - wall.start.z; var length = Length(wall);
            if (length <= .00001f) return false;
            var nx = -dz / length; var nz = dx / length; var half = Math.Max(.001f, wall.thicknessMetres * .5f);
            var p0 = ToWallSpace(a, wall.start, dx / length, dz / length, nx, nz); var p1 = ToWallSpace(b, wall.start, dx / length, dz / length, nx, nz);
            var minX = Math.Min(p0.x, p1.x); var maxX = Math.Max(p0.x, p1.x); var minZ = Math.Min(p0.z, p1.z); var maxZ = Math.Max(p0.z, p1.z);
            if (maxX < 0 || minX > length || maxZ < -half || minZ > half) return false;
            // Segment/rectangle clipping in wall-local coordinates.
            var t0 = 0f; var t1 = 1f; var sx = p1.x - p0.x; var sz = p1.z - p0.z;
            if (!Clip(-sx, p0.x, ref t0, ref t1) || !Clip(sx, length - p0.x, ref t0, ref t1) || !Clip(-sz, p0.z + half, ref t0, ref t1) || !Clip(sz, half - p0.z, ref t0, ref t1)) return false;
            routeT = t0; wallT = Math.Max(0, Math.Min(1, (p0.x + sx * t0) / length)); return true;
        }

        private static LocalPoint ToWallSpace(LocalPoint p, LocalPoint origin, float tx, float tz, float nx, float nz)
        { var x = p.x - origin.x; var z = p.z - origin.z; return new LocalPoint(x * tx + z * tz, x * nx + z * nz); }
        private static bool Clip(float p, float q, ref float u1, ref float u2)
        { if (Math.Abs(p) < .000001f) return q >= 0; var r = q / p; if (p < 0) { if (r > u2) return false; if (r > u1) u1 = r; } else { if (r < u1) return false; if (r < u2) u2 = r; } return true; }

        public static bool EntersNonWalkableRegion(IList<LocalPoint> route, FloorData floor, out string error)
        {
            error = null;
            if (route == null || floor == null || floor.rooms == null) return false;
            foreach (var room in floor.rooms) if (room != null && !room.walkable && !IsOuterBackground(room, floor))
            {
                foreach (var point in route) if (Contains(point, room.polygon)) { error = "Route enters non-walkable region " + room.id; return true; }
                for (var i = 0; i + 1 < route.Count; i++) if (SegmentHitsPolygon(route[i], route[i + 1], room.polygon)) { error = "Route enters non-walkable region " + room.id; return true; }
            }
            return false;
        }

        private static bool IsOuterBackground(RoomData candidate, FloorData floor)
        {
            if (candidate.polygon == null || candidate.polygon.vertices == null || candidate.polygon.vertices.Count < 3) return false;
            var candidateArea = Math.Abs(SignedArea(candidate.polygon)); var walkableCount = 0; var enclosed = 0; var walkableArea = 0f;
            foreach (var room in floor.rooms) if (room != null && room.walkable && room.polygon != null && room.polygon.vertices.Count >= 3)
            {
                walkableCount++; walkableArea += Math.Abs(SignedArea(room.polygon)); var center = new LocalPoint(0, 0); foreach (var p in room.polygon.vertices) { center.x += p.x; center.z += p.z; } center.x /= room.polygon.vertices.Count; center.z /= room.polygon.vertices.Count; if (Contains(center, candidate.polygon)) enclosed++;
            }
            return walkableCount >= 4 && enclosed >= Math.Max(4, (int)Math.Ceiling(walkableCount * .5f)) && walkableArea > 0 && candidateArea > walkableArea * .6f;
        }

        private static float SignedArea(LocalPolygon polygon) { var area = 0f; for (var i = 0; i < polygon.vertices.Count; i++) { var a = polygon.vertices[i]; var b = polygon.vertices[(i + 1) % polygon.vertices.Count]; area += a.x * b.z - b.x * a.z; } return area * .5f; }

        private static float Length(WallData wall) { var x = wall.end.x - wall.start.x; var z = wall.end.z - wall.start.z; return (float)Math.Sqrt(x * x + z * z); }
        private static bool Intersects(LocalPoint a, LocalPoint b, LocalPoint c, LocalPoint d, out float routeT, out float wallT)
        {
            var rx = b.x - a.x; var rz = b.z - a.z; var sx = d.x - c.x; var sz = d.z - c.z; var cross = rx * sz - rz * sx; routeT = wallT = 0;
            if (Math.Abs(cross) < .000001f) return false;
            var qpx = c.x - a.x; var qpz = c.z - a.z; routeT = (qpx * sz - qpz * sx) / cross; wallT = (qpx * rz - qpz * rx) / cross;
            return routeT > .0001f && routeT < .9999f && wallT > .0001f && wallT < .9999f;
        }
        private static bool Contains(LocalPoint point, LocalPolygon polygon)
        {
            if (polygon == null || polygon.vertices == null || polygon.vertices.Count < 3) return false;
            var inside = false; for (var i = 0; i < polygon.vertices.Count; i++) { var a = polygon.vertices[i]; var b = polygon.vertices[(i + polygon.vertices.Count - 1) % polygon.vertices.Count]; if ((a.z > point.z) != (b.z > point.z) && point.x < (b.x - a.x) * (point.z - a.z) / ((b.z - a.z) + .0000001f) + a.x) inside = !inside; } return inside;
        }
        private static bool SegmentHitsPolygon(LocalPoint start, LocalPoint end, LocalPolygon polygon)
        {
            if (polygon == null || polygon.vertices == null || polygon.vertices.Count < 3) return false;
            for (var i = 0; i < polygon.vertices.Count; i++) if (Intersects(start, end, polygon.vertices[i], polygon.vertices[(i + 1) % polygon.vertices.Count], out _, out _)) return true;
            return Contains(new LocalPoint((start.x + end.x) * .5f, (start.z + end.z) * .5f), polygon);
        }
    }
}
