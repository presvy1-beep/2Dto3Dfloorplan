using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geometry;

namespace ARNav.Core.Validation
{
    public static class AnnotationValidator
    {
        public const float MinimumWallLengthPixels = 2f;
        public static ValidationResult Validate(FloorPlanAnnotationData data, int imageWidth = 0, int imageHeight = 0)
        {
            var result = new ValidationResult();
            if (data == null) { result.Add("Annotations are missing."); return result; }
            var ids = new HashSet<string>();
            if (data.rooms != null) foreach (var room in data.rooms)
            {
                if (room == null) { result.Add("Room annotation is null."); continue; }
                CheckId(ids, room.id, "room", result);
                ValidatePolygon(room.vertices, "Room " + room.id, imageWidth, imageHeight, result);
            }
            // When a serialized architectural raster exists it is the authoritative
            // wall source. Legacy centerline guesses are retained for editing/metadata
            // but cannot invalidate raster-based model generation.
            var authoritativeWallMask = data.wallRuns != null && data.wallRuns.Count > 0;
            if (!authoritativeWallMask && data.walls != null) foreach (var wall in data.walls)
            {
                if (wall == null) { result.Add("Wall annotation is null."); continue; }
                CheckId(ids, wall.id, "wall", result);
                if (!Finite(wall.start) || !Finite(wall.end)) result.Add("Wall " + wall.id + " contains a non-finite point.");
                if (Distance(wall.start, wall.end) < MinimumWallLengthPixels) result.Add("Wall " + wall.id + " is too short.");
                if (imageWidth > 0 && (!Inside(wall.start, imageWidth, imageHeight) || !Inside(wall.end, imageWidth, imageHeight))) result.Add("Wall " + wall.id + " contains a point outside image bounds.");
            }
            if (!authoritativeWallMask && data.doors != null) foreach (var door in data.doors)
            {
                if (door == null) { result.Add("Door annotation is null."); continue; }
                CheckId(ids, door.id, "door", result);
                if (!ContainsWall(data.walls, door.wallId)) result.Add("Door " + door.id + " references a missing wall.");
                if (float.IsNaN(door.startT) || float.IsInfinity(door.startT) || float.IsNaN(door.endT) || float.IsInfinity(door.endT) || door.startT < 0 || door.endT > 1 || door.startT >= door.endT) result.Add("Door " + door.id + " has invalid wall parameters.");
            }
            return result;
        }

        public static bool IsValidPolygon(IList<PixelPoint> vertices)
        { var result = new ValidationResult(); ValidatePolygon(vertices, "Polygon", 0, 0, result); return result.IsValid; }

        private static void ValidatePolygon(IList<PixelPoint> vertices, string label, int width, int height, ValidationResult result)
        {
            if (vertices == null || vertices.Count < 3) { result.Add(label + " must have at least 3 vertices."); return; }
            for (var i = 0; i < vertices.Count; i++)
            {
                var current = vertices[i]; var next = vertices[(i + 1) % vertices.Count];
                if (NearlyEqual(current, next)) result.Add(label + " has duplicate consecutive vertices.");
                if (width > 0 && (current.x < 0 || current.x > width || current.y < 0 || current.y > height)) result.Add(label + " contains a point outside image bounds.");
            }
            if (Math.Abs(SignedArea(vertices)) < 0.001) result.Add(label + " has zero area.");
            for (var i = 0; i < vertices.Count; i++) for (var j = i + 1; j < vertices.Count; j++)
            {
                if (j == i + 1 || (i == 0 && j == vertices.Count - 1)) continue;
                if (SegmentsIntersect(vertices[i], vertices[(i + 1) % vertices.Count], vertices[j], vertices[(j + 1) % vertices.Count])) result.Add(label + " self-intersects.");
            }
        }

        private static void CheckId(HashSet<string> ids, string id, string kind, ValidationResult result) { if (string.IsNullOrWhiteSpace(id)) result.Add(kind + " annotation ID is missing."); else if (!ids.Add(id)) result.Add("Duplicate annotation ID: " + id + "."); }
        private static bool ContainsWall(List<WallAnnotationData> walls, string id) { if (walls == null) return false; foreach (var wall in walls) if (wall != null && wall.id == id) return true; return false; }
        private static float Distance(PixelPoint a, PixelPoint b) { var x = a.x - b.x; var y = a.y - b.y; return (float)Math.Sqrt(x * x + y * y); }
        private static bool Finite(PixelPoint point) { return !float.IsNaN(point.x) && !float.IsInfinity(point.x) && !float.IsNaN(point.y) && !float.IsInfinity(point.y); }
        private static bool Inside(PixelPoint point, int width, int height) { return Finite(point) && point.x >= 0 && point.x <= width && point.y >= 0 && point.y <= height; }
        private static bool NearlyEqual(PixelPoint a, PixelPoint b) { return Distance(a, b) < 0.001f; }
        private static double SignedArea(IList<PixelPoint> v) { double area = 0; for (var i = 0; i < v.Count; i++) { var n = v[(i + 1) % v.Count]; area += v[i].x * n.y - n.x * v[i].y; } return area * 0.5; }
        private static float Cross(PixelPoint a, PixelPoint b, PixelPoint c) { return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x); }
        private static bool SegmentsIntersect(PixelPoint a, PixelPoint b, PixelPoint c, PixelPoint d) { var ab1 = Cross(a, b, c); var ab2 = Cross(a, b, d); var cd1 = Cross(c, d, a); var cd2 = Cross(c, d, b); return ((ab1 > 0 && ab2 < 0) || (ab1 < 0 && ab2 > 0)) && ((cd1 > 0 && cd2 < 0) || (cd1 < 0 && cd2 > 0)); }
    }
}
