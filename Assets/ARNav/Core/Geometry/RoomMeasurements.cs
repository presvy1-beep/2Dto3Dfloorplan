using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Generation;

namespace ARNav.Core.Geometry
{
    /// <summary>Dimensions derived from the canonical room polygon; no cached JSON values.</summary>
    public sealed class RoomMeasurements
    {
        public float boundingLengthMetres;
        public float boundingBreadthMetres;
        public float areaSquareMetres;
        public LocalPoint centroid;

        public static bool TryFromRoom(RoomData room, out RoomMeasurements result)
        {
            result = null;
            var vertices = room == null || room.polygon == null ? null : room.polygon.vertices;
            if (vertices == null || vertices.Count < 3 || !PolygonIsValid(vertices)) return false;
            var minX = double.PositiveInfinity; var maxX = double.NegativeInfinity;
            var minZ = double.PositiveInfinity; var maxZ = double.NegativeInfinity;
            foreach (var point in vertices)
            {
                minX = Math.Min(minX, point.x); maxX = Math.Max(maxX, point.x);
                minZ = Math.Min(minZ, point.z); maxZ = Math.Max(maxZ, point.z);
            }
            if (!TryAreaAndCentroid(vertices, out var area, out var cx, out var cz)) return false;
            if (room.holes != null) foreach (var hole in room.holes)
            {
                if (hole == null || hole.vertices == null || !PolygonIsValid(hole.vertices) ||
                    !TryAreaAndCentroid(hole.vertices, out var holeArea, out var hx, out var hz)) return false;
                if (holeArea >= area) return false;
                cx = (cx * area - hx * holeArea) / (area - holeArea);
                cz = (cz * area - hz * holeArea) / (area - holeArea);
                area -= holeArea;
            }
            result = new RoomMeasurements
            {
                boundingLengthMetres = (float)(maxX - minX),
                boundingBreadthMetres = (float)(maxZ - minZ),
                areaSquareMetres = (float)area,
                centroid = new LocalPoint((float)cx, (float)cz)
            };
            return true;
        }

        private static bool PolygonIsValid(IList<LocalPoint> vertices)
        { return PolygonTriangulator.Triangulate(vertices).success; }

        private static bool TryAreaAndCentroid(IList<LocalPoint> vertices, out double area, out double cx, out double cz)
        {
            var signedTwiceArea = 0d; var weightedX = 0d; var weightedZ = 0d;
            for (var i = 0; i < vertices.Count; i++)
            {
                var a = vertices[i]; var b = vertices[(i + 1) % vertices.Count];
                var cross = (double)a.x * b.z - (double)b.x * a.z;
                signedTwiceArea += cross; weightedX += (a.x + b.x) * cross; weightedZ += (a.z + b.z) * cross;
            }
            area = Math.Abs(signedTwiceArea) * 0.5;
            cx = signedTwiceArea == 0 ? 0 : weightedX / (3 * signedTwiceArea);
            cz = signedTwiceArea == 0 ? 0 : weightedZ / (3 * signedTwiceArea);
            return area > 0 && !double.IsNaN(cx) && !double.IsNaN(cz);
        }
    }
}
