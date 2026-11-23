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
            var polygonValid = vertices != null && vertices.Count >= 3 && PolygonIsValid(vertices);
            if (room != null && room.rasterRuns != null && room.rasterRuns.Count > 0 &&
                (room.preferRasterGeometry || room.holes != null && room.holes.Count > 0 || !polygonValid))
                return TryFromRasterRuns(room.rasterRuns, out result);
            if (!polygonValid) return false;
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

        private static bool TryFromRasterRuns(IList<LocalRasterRun> runs, out RoomMeasurements result)
        {
            result = null;
            var sorted = new List<LocalRasterRun>(runs);
            foreach (var run in sorted)
                if (run == null || !Finite(run.minX) || !Finite(run.maxX) || !Finite(run.minZ) || !Finite(run.maxZ) ||
                    run.maxX <= run.minX || run.maxZ <= run.minZ) return false;
            sorted.Sort((a, b) => a.minX.CompareTo(b.minX));
            var minX = double.PositiveInfinity; var maxX = double.NegativeInfinity;
            var minZ = double.PositiveInfinity; var maxZ = double.NegativeInfinity;
            var area = 0d; var weightedX = 0d; var weightedZ = 0d;
            for (var i = 0; i < sorted.Count; i++)
            {
                var run = sorted[i];
                for (var j = i + 1; j < sorted.Count && sorted[j].minX < run.maxX; j++)
                    if (sorted[j].minZ < run.maxZ && sorted[j].maxZ > run.minZ) return false;
                var cellArea = (double)(run.maxX - run.minX) * (run.maxZ - run.minZ);
                area += cellArea;
                weightedX += cellArea * ((double)run.minX + run.maxX) * 0.5;
                weightedZ += cellArea * ((double)run.minZ + run.maxZ) * 0.5;
                minX = Math.Min(minX, run.minX); maxX = Math.Max(maxX, run.maxX);
                minZ = Math.Min(minZ, run.minZ); maxZ = Math.Max(maxZ, run.maxZ);
            }
            if (area <= 0) return false;
            result = new RoomMeasurements { boundingLengthMetres = (float)(maxX - minX),
                boundingBreadthMetres = (float)(maxZ - minZ), areaSquareMetres = (float)area,
                centroid = new LocalPoint((float)(weightedX / area), (float)(weightedZ / area)) };
            return true;
        }

        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }

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
