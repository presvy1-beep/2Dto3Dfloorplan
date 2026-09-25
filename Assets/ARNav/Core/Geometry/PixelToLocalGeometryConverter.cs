using System;
using System.Collections.Generic;
using ARNav.Core.Annotation;
using ARNav.Core.Data;
using ARNav.Core.Validation;

namespace ARNav.Core.Geometry
{
    /// <summary>
    /// Regenerates metre geometry deterministically from source pixels and a valid scale.
    /// The source image uses an upper-left origin with Y downward. LocalPoint uses the image
    /// lower-left as (0,0), X right, and Z upward. Future geographic calibration must rotate
    /// this local X/Z basis into East/North; geographic north is not assumed to equal +Z.
    /// </summary>
    public static class PixelToLocalGeometryConverter
    {
        public static bool TryRegenerate(FloorData floor, ScaleCalibrationData calibration, int imageWidth, int imageHeight, MapGenerationDefaults defaults, out ValidationResult result)
        {
            result = new ValidationResult();
            if (floor == null) { result.Add("Floor is null."); return false; }
            if (calibration == null || !calibration.isValid || !Finite(calibration.metresPerPixel) || calibration.metresPerPixel <= 0) { result.Add("Scale calibration is invalid."); return false; }
            var source = floor.sourceAnnotations ?? new FloorPlanAnnotationData();
            floor.sourceAnnotations = source;
            var hasAuthoritativeWallMask = source.wallRuns != null && source.wallRuns.Count > 0;
            var sourceResult = AnnotationValidator.Validate(source, imageWidth, imageHeight);
            if (!sourceResult.IsValid) { foreach (var error in sourceResult.errors) result.Add(error); return false; }
            if (defaults == null) defaults = new MapGenerationDefaults();
            var rooms = new List<RoomData>();
            var walls = new List<WallData>();
            var doors = new List<DoorData>();
            var wallById = new Dictionary<string, WallData>();

            if (source.rooms != null) foreach (var sourceRoom in source.rooms)
            {
                var room = new RoomData { id = sourceRoom.id, displayName = sourceRoom.displayName, roomNumber = sourceRoom.roomNumber, category = sourceRoom.category, regionType = sourceRoom.regionType, walkable = sourceRoom.walkable, destinationSelectable = sourceRoom.destinationSelectable, heightMetres = defaults.defaultRoomHeightMetres, detectionConfidence = sourceRoom.detectionConfidence, preferRasterGeometry = sourceRoom.preferRasterGeometry };
                if (sourceRoom.vertices != null) foreach (var vertex in sourceRoom.vertices) room.polygon.vertices.Add(ToLocal(vertex, calibration.metresPerPixel, imageHeight));
                if (sourceRoom.holes != null) foreach (var sourceHole in sourceRoom.holes) { var hole = new LocalPolygon(); if (sourceHole != null && sourceHole.vertices != null) foreach (var vertex in sourceHole.vertices) hole.vertices.Add(ToLocal(vertex, calibration.metresPerPixel, imageHeight)); room.holes.Add(hole); }
                if (sourceRoom.rasterRuns != null) foreach (var run in sourceRoom.rasterRuns) if (run != null)
                {
                    var topLeft = ToLocal(new PixelPoint(run.minX, run.minY), calibration.metresPerPixel, imageHeight);
                    var bottomRight = ToLocal(new PixelPoint(run.maxX, run.maxY), calibration.metresPerPixel, imageHeight);
                    room.rasterRuns.Add(new LocalRasterRun { minX = topLeft.x, maxX = bottomRight.x, minZ = bottomRight.z, maxZ = topLeft.z });
                }
                rooms.Add(room);
            }
            if (!hasAuthoritativeWallMask && source.walls != null) foreach (var sourceWall in source.walls)
            {
                var wall = new WallData { id = sourceWall.id, start = ToLocal(sourceWall.start, calibration.metresPerPixel, imageHeight), end = ToLocal(sourceWall.end, calibration.metresPerPixel, imageHeight), heightMetres = defaults.defaultWallHeightMetres, thicknessMetres = defaults.defaultWallThicknessMetres };
                walls.Add(wall); wallById.Add(wall.id, wall);
            }
            var architecturalWallRuns = new List<LocalRasterRun>();
            if (source.wallRuns != null) foreach (var run in source.wallRuns) if (run != null && run.maxX > run.minX && run.maxY > run.minY)
            {
                var topLeft = ToLocal(new PixelPoint(run.minX, run.minY), calibration.metresPerPixel, imageHeight);
                var bottomRight = ToLocal(new PixelPoint(run.maxX, run.maxY), calibration.metresPerPixel, imageHeight);
                architecturalWallRuns.Add(new LocalRasterRun { minX = topLeft.x, maxX = bottomRight.x, minZ = bottomRight.z, maxZ = topLeft.z });
            }
            var architecturalWallPaths = new List<LocalWallPathData>();
            if (source.wallPaths != null) foreach (var path in source.wallPaths)
            {
                if (path == null || path.points == null || path.points.Count < 2 || !Finite(path.widthPixels) || path.widthPixels <= 0) continue;
                var pathWidth = Math.Max(WallPathGeometryConfiguration.MinimumWallThicknessMeters,
                    Math.Min(WallPathGeometryConfiguration.MaximumWallThicknessMeters, path.widthPixels * calibration.metresPerPixel));
                var localPath = new LocalWallPathData { id = path.id, widthMetres = pathWidth, closed = path.closed };
                foreach (var point in path.points)
                    if (point.x >= 0 && point.y >= 0 && point.x <= imageWidth && point.y <= imageHeight)
                        localPath.points.Add(ToLocal(point, calibration.metresPerPixel, imageHeight));
                if (localPath.points.Count >= 2) architecturalWallPaths.Add(localPath);
            }
            if (!hasAuthoritativeWallMask && source.doors != null) foreach (var sourceDoor in source.doors)
            {
                if (!wallById.TryGetValue(sourceDoor.wallId, out var wall)) { result.Add("Door " + sourceDoor.id + " references a missing wall."); continue; }
                var length = Distance(wall.start, wall.end);
                var door = new DoorData { id = sourceDoor.id, parentWallId = wall.id, distanceAlongWallMetres = length * ((sourceDoor.startT + sourceDoor.endT) * 0.5f), widthMetres = length * (sourceDoor.endT - sourceDoor.startT), connectedRegionIds = sourceDoor.connectedRoomIds == null ? new List<string>() : new List<string>(sourceDoor.connectedRoomIds) };
                if (!Finite(door.widthMetres) || door.widthMetres <= 0) { result.Add("Door " + door.id + " has invalid width."); continue; }
                doors.Add(door); wall.doorIds.Add(door.id);
                if (door.connectedRegionIds != null) foreach (var roomId in door.connectedRegionIds) foreach (var room in rooms) if (room.id == roomId && !room.doorIds.Contains(door.id)) room.doorIds.Add(door.id);
            }
            if (!result.IsValid) return false;
            floor.rooms = rooms; floor.walls = walls; floor.doors = doors; floor.architecturalWallRuns = architecturalWallRuns; floor.architecturalWallPaths = architecturalWallPaths;
            return true;
        }

        /// <summary>Converts upper-left-origin image pixels into bottom-left-origin local metres.</summary>
        public static LocalPoint ToLocal(PixelPoint pixel, float metresPerPixel, int imageHeightPixels)
        { return new LocalPoint(pixel.x * metresPerPixel, (imageHeightPixels - pixel.y) * metresPerPixel); }

        /// <summary>Converts bottom-left-origin local metres back into upper-left-origin image pixels.</summary>
        public static PixelPoint ToPixel(LocalPoint local, float metresPerPixel, int imageHeightPixels)
        { return new PixelPoint(local.x / metresPerPixel, imageHeightPixels - local.z / metresPerPixel); }

        public static ValidationResult ValidateDerived(FloorData floor)
        {
            var result = new ValidationResult();
            if (floor == null) { result.Add("Floor is null."); return result; }
            if (floor.rooms == null || floor.walls == null || floor.doors == null) { result.Add("Derived geometry collections are missing."); return result; }
            var ids = new HashSet<string>();
            foreach (var room in floor.rooms)
            {
                if (room == null || string.IsNullOrWhiteSpace(room.id) || !ids.Add(room.id) || room.polygon == null || room.polygon.vertices == null || room.polygon.vertices.Count < 3) { result.Add("Derived room geometry is invalid."); continue; }
                if (room.heightMetres <= 0 || !Finite(room.heightMetres)) result.Add("Derived room " + room.id + " height is invalid.");
            }
            var walls = new HashSet<string>();
            foreach (var wall in floor.walls)
            {
                if (wall == null || string.IsNullOrWhiteSpace(wall.id) || !ids.Add(wall.id) || !walls.Add(wall.id) || Distance(wall.start, wall.end) <= 0 || wall.heightMetres <= 0 || wall.thicknessMetres <= 0) result.Add("Derived wall geometry is invalid.");
            }
            foreach (var door in floor.doors)
            {
                if (door == null || string.IsNullOrWhiteSpace(door.id) || !ids.Add(door.id) || !walls.Contains(door.parentWallId) || door.widthMetres <= 0 || !Finite(door.widthMetres) || door.distanceAlongWallMetres < 0) result.Add("Derived door geometry is invalid.");
            }
            return result;
        }

        private static float Distance(LocalPoint a, LocalPoint b) { var x = b.x - a.x; var z = b.z - a.z; return (float)Math.Sqrt(x * x + z * z); }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
