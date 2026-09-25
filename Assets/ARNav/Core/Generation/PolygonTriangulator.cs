using System;
using System.Collections.Generic;
using ARNav.Core.Geometry;

namespace ARNav.Core.Generation
{
    public enum TriangulationFailureReason { None, TooFewVertices, DuplicateVertex, DegenerateEdge, ZeroArea, SelfIntersection, TouchingNonAdjacentEdges, CollinearOverlap, EarClippingStalled, NumericalPrecisionFailure }
    public sealed class TriangulationResult { public bool success; public TriangulationFailureReason failureReason; public int vertexIndex = -1, failingEdgeA = -1, failingEdgeB = -1; public List<int> triangles = new List<int>(); public string message; }
    /// <summary>Dependency-free ear clipping for simple X/Z polygons. Output triangles wind upward.</summary>
    public static class PolygonTriangulator
    {
        private const float Epsilon = 0.000001f;

        public static bool TryTriangulate(IList<LocalPoint> polygon, out List<int> triangles, out string error)
        {
            var result = Triangulate(polygon); triangles = result.triangles; error = result.success ? null : result.failureReason + ": " + result.message; return result.success;
        }

        public static TriangulationResult Triangulate(IList<LocalPoint> polygon)
        {
            var result = new TriangulationResult();
            if (polygon == null || polygon.Count < 3) return Fail(result, TriangulationFailureReason.TooFewVertices, "at least 3 vertices are required");
            var boundsMinX = float.PositiveInfinity; var boundsMaxX = float.NegativeInfinity; var boundsMinZ = float.PositiveInfinity; var boundsMaxZ = float.NegativeInfinity;
            for (var i = 0; i < polygon.Count; i++) { boundsMinX = Math.Min(boundsMinX, polygon[i].x); boundsMaxX = Math.Max(boundsMaxX, polygon[i].x); boundsMinZ = Math.Min(boundsMinZ, polygon[i].z); boundsMaxZ = Math.Max(boundsMaxZ, polygon[i].z); }
            var tolerance = Math.Max(Epsilon, Math.Max(boundsMaxX - boundsMinX, boundsMaxZ - boundsMinZ) * 0.000001f); var tolerance2 = tolerance * tolerance;
            for (var i = 0; i < polygon.Count; i++)
            {
                var next = (i + 1) % polygon.Count; var dx = polygon[next].x - polygon[i].x; var dz = polygon[next].z - polygon[i].z;
                if (dx * dx + dz * dz <= tolerance2) { result.vertexIndex = i; return Fail(result, TriangulationFailureReason.DegenerateEdge, "edge " + i + " has near-zero length"); }
                for (var j = i + 1; j < polygon.Count; j++) if (DistanceSquared(polygon[i], polygon[j]) <= tolerance2)
                { result.vertexIndex = j; return Fail(result, TriangulationFailureReason.DuplicateVertex, "vertices " + i + " and " + j + " coincide"); }
            }
            for (var i = 0; i < polygon.Count; i++) for (var j = i + 1; j < polygon.Count; j++)
            {
                var adjacent = (i + 1) % polygon.Count == j || (j + 1) % polygon.Count == i;
                if (adjacent)
                {
                    var common = (i + 1) % polygon.Count == j ? polygon[j] : polygon[i];
                    var otherA = (i + 1) % polygon.Count == j ? polygon[i] : polygon[(i + 1) % polygon.Count];
                    var otherB = (i + 1) % polygon.Count == j ? polygon[(j + 1) % polygon.Count] : polygon[j];
                    if (Math.Abs(Cross(otherA, common, otherB)) <= tolerance && Dot(otherA, common, otherB) > tolerance2) { result.failingEdgeA = i; result.failingEdgeB = j; return Fail(result, TriangulationFailureReason.CollinearOverlap, "adjacent edges backtrack/overlap"); }
                    continue;
                }
                if (SegmentsTouchOrIntersect(polygon[i], polygon[(i + 1) % polygon.Count], polygon[j], polygon[(j + 1) % polygon.Count], tolerance))
                { result.failingEdgeA = i; result.failingEdgeB = j; return Fail(result, TriangulationFailureReason.TouchingNonAdjacentEdges, "non-adjacent edges " + i + " and " + j + " cross, overlap, or touch"); }
            }
            var area = SignedArea(polygon); if (Math.Abs(area) <= tolerance2) return Fail(result, TriangulationFailureReason.ZeroArea, "signed area is " + area.ToString("R"));
            var indices = new List<int>(); for (var i = 0; i < polygon.Count; i++) indices.Add(i);
            // Ear clipping cannot progress if a collinear boundary vertex is treated as
            // lying inside every candidate ear. Remove only vertices lying between
            // their neighbors; triangles retain indices into the original polygon.
            var changed = true;
            while (changed && indices.Count > 3)
            {
                changed = false;
                for (var i = 0; i < indices.Count; i++)
                {
                    var a = polygon[indices[(i + indices.Count - 1) % indices.Count]]; var b = polygon[indices[i]]; var c = polygon[indices[(i + 1) % indices.Count]];
                    if (Math.Abs(Cross(a, b, c)) <= tolerance && Dot(a, b, c) <= tolerance2) { indices.RemoveAt(i); changed = true; break; }
                }
            }
            if (indices.Count < 3) return Fail(result, TriangulationFailureReason.NumericalPrecisionFailure, "sanitation collapsed the contour");
            // Positive shoelace area is counter-clockwise in X/Z. Reverse so mesh cross products point +Y.
            if (SignedArea(polygon) > 0) indices.Reverse();
            var guard = polygon.Count * polygon.Count;
            while (indices.Count > 3 && guard-- > 0)
            {
                var clipped = false;
                for (var i = 0; i < indices.Count; i++)
                {
                    var previous = indices[(i + indices.Count - 1) % indices.Count];
                    var current = indices[i]; var next = indices[(i + 1) % indices.Count];
                    if (Cross(polygon[previous], polygon[current], polygon[next]) >= -Epsilon) continue;
                    var ear = true;
                    for (var j = 0; j < indices.Count; j++)
                    {
                        var candidate = indices[j]; if (candidate == previous || candidate == current || candidate == next) continue;
                        if (InsideTriangle(polygon[candidate], polygon[previous], polygon[current], polygon[next])) { ear = false; break; }
                    }
                    if (!ear) continue;
                    result.triangles.Add(previous); result.triangles.Add(current); result.triangles.Add(next); indices.RemoveAt(i); clipped = true; break;
                }
                if (!clipped) { result.vertexIndex = indices[0]; result.failingEdgeA = indices[(indices.Count + indices.IndexOf(indices[0]) - 1) % indices.Count]; return Fail(result, TriangulationFailureReason.EarClippingStalled, "no valid ear; remaining contour indices=" + string.Join(",", indices)); }
            }
            if (indices.Count != 3) return Fail(result, TriangulationFailureReason.NumericalPrecisionFailure, "triangulation guard expired");
            result.triangles.Add(indices[0]); result.triangles.Add(indices[1]); result.triangles.Add(indices[2]); result.success = true; return result;
        }

        public static float SignedArea(IList<LocalPoint> polygon)
        { var area = 0f; for (var i = 0; i < polygon.Count; i++) { var a = polygon[i]; var b = polygon[(i + 1) % polygon.Count]; area += a.x * b.z - b.x * a.z; } return area * 0.5f; }
        public static bool IsSimple(IList<LocalPoint> polygon)
        {
            if (polygon == null || polygon.Count < 3) return false;
            return Triangulate(polygon).success;
        }
        private static float Cross(LocalPoint a, LocalPoint b, LocalPoint c) { return (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x); }
        private static bool InsideTriangle(LocalPoint p, LocalPoint a, LocalPoint b, LocalPoint c) { return Cross(a, b, p) <= Epsilon && Cross(b, c, p) <= Epsilon && Cross(c, a, p) <= Epsilon; }
        private static double DistanceSquared(LocalPoint a, LocalPoint b) { var x = a.x - b.x; var z = a.z - b.z; return x * x + z * z; }
        private static float Dot(LocalPoint a, LocalPoint b, LocalPoint c) { return (a.x - b.x) * (c.x - b.x) + (a.z - b.z) * (c.z - b.z); }
        private static bool OnSegment(LocalPoint a, LocalPoint b, LocalPoint p, float eps) { return p.x >= Math.Min(a.x, b.x) - eps && p.x <= Math.Max(a.x, b.x) + eps && p.z >= Math.Min(a.z, b.z) - eps && p.z <= Math.Max(a.z, b.z) + eps; }
        private static bool SegmentsTouchOrIntersect(LocalPoint a, LocalPoint b, LocalPoint c, LocalPoint d, float eps)
        {
            var abC = Cross(a, b, c); var abD = Cross(a, b, d); var cdA = Cross(c, d, a); var cdB = Cross(c, d, b);
            if (((abC > eps && abD < -eps) || (abC < -eps && abD > eps)) && ((cdA > eps && cdB < -eps) || (cdA < -eps && cdB > eps))) return true;
            return (Math.Abs(abC) <= eps && OnSegment(a, b, c, eps)) || (Math.Abs(abD) <= eps && OnSegment(a, b, d, eps)) || (Math.Abs(cdA) <= eps && OnSegment(c, d, a, eps)) || (Math.Abs(cdB) <= eps && OnSegment(c, d, b, eps));
        }
        private static TriangulationResult Fail(TriangulationResult result, TriangulationFailureReason reason, string message) { result.failureReason = reason; result.message = message; result.success = false; result.triangles.Clear(); return result; }
    }
}
