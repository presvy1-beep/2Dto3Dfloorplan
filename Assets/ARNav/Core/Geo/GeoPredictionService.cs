using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geometry;

namespace ARNav.Core.Geo
{
    public sealed class GeoPrediction
    {
        public string entityId; public string pointId; public LocalPoint localPoint; public GeoCoordinate coordinate;
        public GeoPrediction(string entityId, string pointId, LocalPoint localPoint, GeoCoordinate coordinate) { this.entityId = entityId; this.pointId = pointId; this.localPoint = localPoint; this.coordinate = coordinate; }
    }

    /// <summary>Produces disposable coordinate views from canonical local geometry.</summary>
    public static class GeoPredictionService
    {
        public static List<GeoPrediction> PredictFloor(FloorData floor, GeoCoordinateTransformer transformer)
        {
            var output = new List<GeoPrediction>(); if (floor == null || transformer == null) return output;
            if (floor.buildingBoundary != null && floor.buildingBoundary.vertices != null) for (var i = 0; i < floor.buildingBoundary.vertices.Count; i++) Add(output, "boundary", i.ToString(), floor.buildingBoundary.vertices[i], transformer);
            if (floor.rooms != null) foreach (var room in floor.rooms) if (room != null && room.polygon != null && room.polygon.vertices != null) for (var i = 0; i < room.polygon.vertices.Count; i++) Add(output, room.id, "vertex_" + i, room.polygon.vertices[i], transformer);
            if (floor.walls != null) foreach (var wall in floor.walls) if (wall != null) { Add(output, wall.id, "start", wall.start, transformer); Add(output, wall.id, "end", wall.end, transformer); }
            if (floor.doors != null) foreach (var door in floor.doors) if (door != null) { var wall = FindWall(floor, door.parentWallId); if (wall == null) continue; var length = Distance(wall.start, wall.end); var center = length <= 0 ? 0 : door.distanceAlongWallMetres / length; Add(output, door.id, "centre", Lerp(wall.start, wall.end, center), transformer); Add(output, door.id, "start", Lerp(wall.start, wall.end, center - door.widthMetres / length / 2), transformer); Add(output, door.id, "end", Lerp(wall.start, wall.end, center + door.widthMetres / length / 2), transformer); }
            return output;
        }
        private static void Add(List<GeoPrediction> output, string id, string pointId, LocalPoint point, GeoCoordinateTransformer transformer) { output.Add(new GeoPrediction(id, pointId, point, transformer.LocalToGeo(point))); }
        private static WallData FindWall(FloorData floor, string id) { if (floor.walls != null) foreach (var wall in floor.walls) if (wall != null && wall.id == id) return wall; return null; }
        private static LocalPoint Lerp(LocalPoint a, LocalPoint b, double t) { return new LocalPoint((float)(a.x + (b.x - a.x) * t), (float)(a.z + (b.z - a.z) * t)); }
        private static double Distance(LocalPoint a, LocalPoint b) { var x = b.x - a.x; var z = b.z - a.z; return System.Math.Sqrt(x * x + z * z); }
    }
}
