using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geometry;

namespace ARNav.Core.Generation
{
    public enum WallPieceType { Solid, Header }
    public sealed class WallPiecePlan { public WallPieceType type; public float startT, endT, bottomMetres, topMetres; public string doorId; }
    public sealed class WallPlan { public string wallId; public float lengthMetres, heightMetres, thicknessMetres; public readonly List<WallPiecePlan> pieces = new List<WallPiecePlan>(); public readonly List<DoorOpeningPlan> doors = new List<DoorOpeningPlan>(); }
    public sealed class DoorOpeningPlan { public string doorId; public float startT, endT, widthMetres, heightMetres; }

    public static class WallOpeningPlanner
    {
        private const float Epsilon = 0.00001f;
        public static bool TryPlan(WallData wall, IList<DoorData> allDoors, MapGenerationDefaults defaults, out WallPlan plan, out string error)
        {
            plan = null; error = null; if (wall == null) { error = "Wall is null."; return false; }
            var dx = wall.end.x - wall.start.x; var dz = wall.end.z - wall.start.z; var length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (!Finite(length) || length <= Epsilon || wall.heightMetres <= 0 || wall.thicknessMetres <= 0) { error = "Wall " + wall.id + " has invalid dimensions."; return false; }
            var height = wall.heightMetres; var output = new WallPlan { wallId = wall.id, lengthMetres = length, heightMetres = height, thicknessMetres = wall.thicknessMetres };
            var openings = new List<DoorOpeningPlan>();
            if (allDoors != null) foreach (var door in allDoors) if (door != null && door.parentWallId == wall.id)
            {
                var center = door.distanceAlongWallMetres / length; var half = door.widthMetres / length * 0.5f; var start = center - half; var end = center + half;
                var doorHeight = defaults == null ? 2.1f : defaults.defaultDoorHeightMetres;
                if (!Finite(start) || !Finite(end) || door.widthMetres <= 0 || start < -Epsilon || end > 1 + Epsilon || start >= end || doorHeight <= 0 || doorHeight >= height) { error = "Door " + door.id + " has invalid wall opening data."; return false; }
                start = Math.Max(0, start); end = Math.Min(1, end); openings.Add(new DoorOpeningPlan { doorId = door.id, startT = start, endT = end, widthMetres = (end - start) * length, heightMetres = doorHeight });
            }
            openings.Sort((a, b) => a.startT.CompareTo(b.startT));
            var cursor = 0f; foreach (var opening in openings)
            {
                if (opening.startT < cursor - Epsilon) { error = "Doors on wall " + wall.id + " overlap."; return false; }
                if (opening.startT - cursor > Epsilon) output.pieces.Add(new WallPiecePlan { type = WallPieceType.Solid, startT = cursor, endT = opening.startT, bottomMetres = 0, topMetres = height });
                output.pieces.Add(new WallPiecePlan { type = WallPieceType.Header, startT = opening.startT, endT = opening.endT, bottomMetres = opening.heightMetres, topMetres = height, doorId = opening.doorId });
                output.doors.Add(opening); cursor = opening.endT;
            }
            if (1 - cursor > Epsilon) output.pieces.Add(new WallPiecePlan { type = WallPieceType.Solid, startT = cursor, endT = 1, bottomMetres = 0, topMetres = height });
            plan = output; return true;
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
