using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geometry;

namespace ARNav.Core.Generation
{
    public sealed class FloorGenerationPlan { public FloorData source; public bool usesWallPaths; public bool usesRasterWallMask; public readonly List<RoomGenerationPlan> rooms = new List<RoomGenerationPlan>(); public readonly List<WallPlan> walls = new List<WallPlan>(); public readonly List<string> errors = new List<string>(); public readonly List<string> warnings = new List<string>(); }
    public sealed class RoomGenerationPlan { public RoomData source; public List<int> triangles; public bool rasterFallback; }
    public sealed class BuildingGenerationPlan { public readonly List<FloorGenerationPlan> floors = new List<FloorGenerationPlan>(); public readonly List<string> errors = new List<string>(); public readonly List<string> warnings = new List<string>(); public bool IsValid { get { return errors.Count == 0; } } }

    public static class BuildingGenerationPlanner
    {
        public static BuildingGenerationPlan Build(MapProjectData project, MapGenerationDefaults defaults = null)
        {
            var plan = new BuildingGenerationPlan(); if (project == null || project.floors == null) { plan.errors.Add("Project has no floors."); return plan; }
            defaults = defaults ?? new MapGenerationDefaults();
            foreach (var floor in project.floors)
            {
                var fp = new FloorGenerationPlan { source = floor }; if (floor == null) { plan.errors.Add("Null floor."); continue; }
                if (floor.elevationMetres != floor.elevationMetres || float.IsInfinity(floor.elevationMetres)) fp.errors.Add("Floor elevation is invalid.");
                if (floor.rooms != null) foreach (var room in floor.rooms)
                {
                    // Non-walkable regions describe void/background/unknown space. They
                    // remain in JSON for editing and validation, but must never become a
                    // generated floor that fills architectural holes.
                    if (room == null || !room.walkable) continue;
                    if (room.holes != null && room.holes.Count > 0)
                    {
                        if (room.rasterRuns != null && room.rasterRuns.Count > 0)
                        {
                            fp.rooms.Add(new RoomGenerationPlan { source = room, rasterFallback = true });
                            plan.warnings.Add("Floor " + floor.id + ": Room " + room.id + " has holes; using occupied raster cells to preserve them.");
                        }
                        else plan.warnings.Add("Floor " + floor.id + ": Room " + room.id + " has holes but no raster cells; floor patch skipped to avoid filling voids.");
                        continue;
                    }
                    if (room.preferRasterGeometry && room.rasterRuns != null && room.rasterRuns.Count > 0)
                    {
                        var rasterDiagnostic = room.polygon == null ? null : PolygonTriangulator.Triangulate(room.polygon.vertices);
                        fp.rooms.Add(new RoomGenerationPlan { source = room, rasterFallback = true });
                        plan.warnings.Add("Floor " + floor.id + ": Room " + room.id + " -> authoritative raster mesh; " + (rasterDiagnostic != null && rasterDiagnostic.success ? "polygon triangulates" : PolygonDiagnostic(room, rasterDiagnostic, "polygon unavailable")));
                        continue;
                    }
                    List<int> tris = null; string error = null; var triangulation = room.polygon == null ? null : PolygonTriangulator.Triangulate(room.polygon.vertices); var valid = triangulation != null && triangulation.success;
                    if (valid) tris = triangulation.triangles; else error = triangulation == null ? "TooFewVertices: missing polygon" : triangulation.failureReason + ": " + triangulation.message;
                    if (!valid)
                    {
                        // Reject only this semantic floor patch. A malformed room must
                        // not make valid walls, rooms, navigation or routes disappear.
                        if (room.rasterRuns != null && room.rasterRuns.Count > 0)
                        {
                            fp.rooms.Add(new RoomGenerationPlan { source = room, rasterFallback = true });
                            plan.warnings.Add("Floor " + floor.id + ": Room " + room.id + " -> raster fallback; " + PolygonDiagnostic(room, triangulation, error));
                        }
                        else
                        {
                            var warning = "Room " + room.id + " has no raster fallback and was skipped: " + (error ?? "invalid polygon");
                            fp.warnings.Add(warning); plan.warnings.Add("Floor " + floor.id + ": " + warning);
                        }
                    }
                    else fp.rooms.Add(new RoomGenerationPlan { source = room, triangles = tris });
                }
                fp.usesWallPaths = floor.architecturalWallPaths != null && floor.architecturalWallPaths.Count > 0;
                fp.usesRasterWallMask = !fp.usesWallPaths && floor.architecturalWallRuns != null && floor.architecturalWallRuns.Count > 0;
                if (fp.usesRasterWallMask) plan.warnings.Add("Floor " + floor.id + ": serialized wallPaths are missing; using raw wallRuns fallback.");
                if (!fp.usesWallPaths && !fp.usesRasterWallMask && floor.walls != null) foreach (var wall in floor.walls) { if (WallOpeningPlanner.TryPlan(wall, floor.doors, defaults, out var wp, out var error)) fp.walls.Add(wp); else fp.errors.Add(error); }
                plan.floors.Add(fp); foreach (var error in fp.errors) plan.errors.Add("Floor " + floor.id + ": " + error);
            }
            return plan;
        }

        private static string PolygonDiagnostic(RoomData room, TriangulationResult result, string error)
        {
            var v = room.polygon == null ? null : room.polygon.vertices; var area = v == null ? 0 : PolygonTriangulator.SignedArea(v); var consecutive = 0; var repeated = 0; var shortEdges = 0; var collinear = 0; var coords = new List<string>();
            if (v != null) for (var i = 0; i < v.Count; i++)
            {
                var a = v[i]; var b = v[(i + 1) % v.Count]; if (Math.Abs(a.x - b.x) < .00001f && Math.Abs(a.z - b.z) < .00001f) consecutive++;
                var dx = b.x - a.x; var dz = b.z - a.z; if (dx * dx + dz * dz < .000001f) shortEdges++;
                var c = v[(i + 2) % v.Count]; var cross = (b.x - a.x) * (c.z - b.z) - (b.z - a.z) * (c.x - b.x); if (Math.Abs(cross) < .00001f) collinear++;
                for (var j = i + 1; j < v.Count; j++) if (Math.Abs(a.x - v[j].x) < .00001f && Math.Abs(a.z - v[j].z) < .00001f) { repeated++; break; }
                coords.Add("[" + a.x.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "," + a.z.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "]");
            }
            return "room=" + room.id + " vertices=" + (v == null ? 0 : v.Count) + " holes=" + (room.holes == null ? 0 : room.holes.Count) + " area=" + area.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " winding=" + (area > 0 ? "CCW" : area < 0 ? "CW" : "Zero") + " duplicateAdjacent=" + consecutive + " duplicateNonAdjacent=" + Math.Max(0, repeated - consecutive) + " zeroOrNearEdges=" + shortEdges + " collinearTriplets=" + collinear + " failure=" + (result == null ? error : result.failureReason + " edgeA=" + result.failingEdgeA + " edgeB=" + result.failingEdgeB + " vertex=" + result.vertexIndex + " " + result.message) + " verticesXZ=" + string.Join(";", coords.ToArray());
        }
    }
}
