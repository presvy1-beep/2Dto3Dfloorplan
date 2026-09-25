using System;
using System.Collections.Generic;
using ARNav.Core.Geometry;

namespace ARNav.Core.Navigation
{
    public static class RouteWaypointSampler
    {
        public static List<LocalPoint> Sample(IList<LocalPoint> corners, float spacingMetres)
        {
            var output = new List<LocalPoint>(); if (corners == null || corners.Count == 0) return output; if (spacingMetres <= 0 || float.IsNaN(spacingMetres) || float.IsInfinity(spacingMetres)) spacingMetres = 1.5f;
            AddIfDistinct(output, corners[0]);
            for (var i = 0; i < corners.Count - 1; i++)
            {
                var a = corners[i]; var b = corners[i + 1]; var dx = b.x - a.x; var dz = b.z - a.z; var length = Math.Sqrt(dx * dx + dz * dz); if (length <= 0.00001) continue;
                var already = Distance(output[output.Count - 1], a); if (already > 0.00001) AddIfDistinct(output, a);
                var distance = spacingMetres; while (distance < length - 0.00001) { var t = distance / length; AddIfDistinct(output, new LocalPoint((float)(a.x + dx * t), (float)(a.z + dz * t))); distance += spacingMetres; }
                AddIfDistinct(output, b);
            }
            return output;
        }
        public static double TotalDistance(IList<LocalPoint> points) { var total = 0d; if (points == null) return total; for (var i = 1; i < points.Count; i++) total += Distance(points[i - 1], points[i]); return total; }
        public static double DistanceToPolyline(LocalPoint point, IList<LocalPoint> polyline) { var best = double.MaxValue; if (polyline == null) return best; for (var i = 1; i < polyline.Count; i++) best = Math.Min(best, PointSegmentDistance(point, polyline[i - 1], polyline[i])); return best; }
        private static void AddIfDistinct(List<LocalPoint> list, LocalPoint point) { if (list.Count == 0 || Distance(list[list.Count - 1], point) > 0.00001) list.Add(point); }
        private static double Distance(LocalPoint a, LocalPoint b) { var x = a.x - b.x; var z = a.z - b.z; return Math.Sqrt(x * x + z * z); }
        private static double PointSegmentDistance(LocalPoint p, LocalPoint a, LocalPoint b) { var dx = b.x - a.x; var dz = b.z - a.z; var lengthSquared = dx * dx + dz * dz; if (lengthSquared <= 0.0000001) return Distance(p, a); var t = ((p.x - a.x) * dx + (p.z - a.z) * dz) / lengthSquared; t = Math.Max(0, Math.Min(1, t)); return Distance(p, new LocalPoint((float)(a.x + dx * t), (float)(a.z + dz * t))); }
    }
}
