using System;
using ARNav.Core.Data;
using ARNav.Core.Geometry;

namespace ARNav.Core.Annotation
{
    public enum AnnotationSelectionKind { None, Room, RoomVertex, Wall, WallEndpoint, Door }

    public struct AnnotationSelection
    {
        public AnnotationSelectionKind kind;
        public string id;
        public int vertexIndex;
        public AnnotationSelection(AnnotationSelectionKind kind, string id, int vertexIndex = -1) { this.kind = kind; this.id = id; this.vertexIndex = vertexIndex; }
    }

    public static class AnnotationHitTester
    {
        public static AnnotationSelection Find(FloorPlanAnnotationData data, PixelPoint point, float tolerance)
        {
            if (data == null) return new AnnotationSelection();
            if (data.rooms != null) foreach (var room in data.rooms) if (room != null && room.vertices != null) for (var i = 0; i < room.vertices.Count; i++) if (Distance(room.vertices[i], point) <= tolerance) return new AnnotationSelection(AnnotationSelectionKind.RoomVertex, room.id, i);
            if (data.doors != null) foreach (var door in data.doors) if (door != null)
            {
                var wall = FindWall(data, door.wallId); if (wall == null) continue;
                var start = Lerp(wall.start, wall.end, door.startT); var end = Lerp(wall.start, wall.end, door.endT);
                if (DistanceToSegment(point, start, end) <= tolerance) return new AnnotationSelection(AnnotationSelectionKind.Door, door.id);
            }
            if (data.walls != null) foreach (var wall in data.walls) if (wall != null)
            {
                if (Distance(wall.start, point) <= tolerance) return new AnnotationSelection(AnnotationSelectionKind.WallEndpoint, wall.id, 0);
                if (Distance(wall.end, point) <= tolerance) return new AnnotationSelection(AnnotationSelectionKind.WallEndpoint, wall.id, 1);
            }
            if (data.walls != null) foreach (var wall in data.walls) if (wall != null && DistanceToSegment(point, wall.start, wall.end) <= tolerance) return new AnnotationSelection(AnnotationSelectionKind.Wall, wall.id);
            if (data.rooms != null) foreach (var room in data.rooms) if (room != null && Contains(room.vertices, point)) return new AnnotationSelection(AnnotationSelectionKind.Room, room.id);
            return new AnnotationSelection();
        }

        public static bool TryProjectToWall(WallAnnotationData wall, PixelPoint point, out float t, out PixelPoint projected)
        {
            t = 0; projected = wall == null ? new PixelPoint() : wall.start; if (wall == null) return false;
            var dx = wall.end.x - wall.start.x; var dy = wall.end.y - wall.start.y; var lengthSquared = dx * dx + dy * dy; if (lengthSquared <= 0) return false;
            t = ((point.x - wall.start.x) * dx + (point.y - wall.start.y) * dy) / lengthSquared; t = Math.Max(0, Math.Min(1, t)); projected = Lerp(wall.start, wall.end, t); return true;
        }

        public static WallAnnotationData FindWall(FloorPlanAnnotationData data, string id) { if (data != null && data.walls != null) foreach (var wall in data.walls) if (wall != null && wall.id == id) return wall; return null; }
        public static DoorAnnotationData FindDoor(FloorPlanAnnotationData data, string id) { if (data != null && data.doors != null) foreach (var door in data.doors) if (door != null && door.id == id) return door; return null; }
        public static RoomAnnotationData FindRoom(FloorPlanAnnotationData data, string id) { if (data != null && data.rooms != null) foreach (var room in data.rooms) if (room != null && room.id == id) return room; return null; }
        public static PixelPoint Lerp(PixelPoint a, PixelPoint b, float t) { return new PixelPoint(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t); }
        public static float Distance(PixelPoint a, PixelPoint b) { var x = a.x - b.x; var y = a.y - b.y; return (float)Math.Sqrt(x * x + y * y); }
        public static float DistanceToSegment(PixelPoint p, PixelPoint a, PixelPoint b) { var dx = b.x - a.x; var dy = b.y - a.y; var len = dx * dx + dy * dy; if (len <= 0) return Distance(p, a); var t = ((p.x - a.x) * dx + (p.y - a.y) * dy) / len; return Distance(p, Lerp(a, b, Math.Max(0, Math.Min(1, t)))); }
        private static bool Contains(System.Collections.Generic.List<PixelPoint> polygon, PixelPoint point) { if (polygon == null || polygon.Count < 3) return false; var inside = false; for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++) { if (((polygon[i].y > point.y) != (polygon[j].y > point.y)) && point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x) inside = !inside; } return inside; }
    }
}
