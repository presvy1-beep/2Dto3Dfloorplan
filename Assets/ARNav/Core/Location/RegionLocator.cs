using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geometry;

namespace ARNav.Core.Location
{
    /// <summary>Performs uncertainty-aware point and polygon tests in canonical local metres.</summary>
    public static class RegionLocator
    {
        public static bool Contains(LocalPoint point, LocalPolygon polygon)
        {
            if (polygon == null || polygon.vertices == null || polygon.vertices.Count < 3) return false;
            var inside = false; for (var i = 0; i < polygon.vertices.Count; i++) { var a = polygon.vertices[i]; var b = polygon.vertices[(i + 1) % polygon.vertices.Count]; if (PointOnSegment(point, a, b)) return true; var crosses = ((a.z > point.z) != (b.z > point.z)) && point.x < (b.x - a.x) * (point.z - a.z) / (b.z - a.z) + a.x; if (crosses) inside = !inside; } return inside;
        }

        public static double DistanceToBoundary(LocalPoint point, LocalPolygon polygon)
        {
            if (polygon == null || polygon.vertices == null || polygon.vertices.Count < 2) return double.PositiveInfinity;
            var minimum = double.PositiveInfinity; for (var i = 0; i < polygon.vertices.Count; i++) { var a = polygon.vertices[i]; var b = polygon.vertices[(i + 1) % polygon.vertices.Count]; minimum = Math.Min(minimum, DistanceToSegment(point, a, b)); } return minimum;
        }

        public static UserRegionResult Locate(FloorData floor, LocalPoint point, double accuracyMetres, LocationQuality quality)
        {
            var result = new UserRegionResult { hasValidLocation = true, localPosition = point, horizontalAccuracyMetres = accuracyMetres, quality = quality, buildingStatus = BuildingPositionStatus.Unknown, roomStatus = RoomPositionStatus.Unknown };
            if (floor == null) { result.diagnostic = "No floor is available."; return result; }
            var boundary = floor.buildingBoundary; if (boundary != null && boundary.vertices != null && boundary.vertices.Count >= 3) { var inside = Contains(point, boundary); result.distanceToBuildingBoundaryMetres = DistanceToBoundary(point, boundary); result.buildingStatus = result.distanceToBuildingBoundaryMetres > accuracyMetres ? (inside ? BuildingPositionStatus.Inside : BuildingPositionStatus.Outside) : BuildingPositionStatus.Uncertain; }
            if (floor.rooms == null) { result.diagnostic = result.buildingStatus == BuildingPositionStatus.Inside ? "Inside building; no room geometry." : "Room geometry unavailable."; return result; }
            foreach (var room in floor.rooms) if (room != null && room.polygon != null) { var roomDistance = DistanceToBoundary(point, room.polygon); if (Contains(point, room.polygon) || roomDistance <= accuracyMetres) { result.possibleRoomIds.Add(room.id); if (double.IsNaN(result.distanceToRoomBoundaryMetres) || roomDistance < result.distanceToRoomBoundaryMetres) result.distanceToRoomBoundaryMetres = roomDistance; } }
            if (result.possibleRoomIds.Count == 1 && result.distanceToRoomBoundaryMetres > accuracyMetres && result.buildingStatus == BuildingPositionStatus.Inside) { result.roomStatus = RoomPositionStatus.InsideRoom; result.roomId = result.possibleRoomIds[0]; foreach (var room in floor.rooms) if (room != null && room.id == result.roomId) result.roomDisplayName = room.displayName; }
            else if (result.possibleRoomIds.Count > 1) result.roomStatus = RoomPositionStatus.MultiplePossibleRooms;
            else if (result.buildingStatus == BuildingPositionStatus.Inside) result.roomStatus = RoomPositionStatus.NotInRoom;
            else if (result.buildingStatus == BuildingPositionStatus.Uncertain) result.roomStatus = RoomPositionStatus.Uncertain;
            return result;
        }

        private static double DistanceToSegment(LocalPoint p, LocalPoint a, LocalPoint b) { var dx = b.x - a.x; var dz = b.z - a.z; var lengthSquared = dx * dx + dz * dz; if (lengthSquared <= 0) return Math.Sqrt((p.x - a.x) * (p.x - a.x) + (p.z - a.z) * (p.z - a.z)); var t = ((p.x - a.x) * dx + (p.z - a.z) * dz) / lengthSquared; t = Math.Max(0, Math.Min(1, t)); var x = a.x + t * dx; var z = a.z + t * dz; return Math.Sqrt((p.x - x) * (p.x - x) + (p.z - z) * (p.z - z)); }
        private static bool PointOnSegment(LocalPoint p, LocalPoint a, LocalPoint b) { var cross = (p.x - a.x) * (b.z - a.z) - (p.z - a.z) * (b.x - a.x); if (Math.Abs(cross) > 0.000001) return false; return p.x >= Math.Min(a.x, b.x) - 0.000001 && p.x <= Math.Max(a.x, b.x) + 0.000001 && p.z >= Math.Min(a.z, b.z) - 0.000001 && p.z <= Math.Max(a.z, b.z) + 0.000001; }
    }
}
