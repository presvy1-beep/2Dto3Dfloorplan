using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Navigation;

namespace ARNav.Core.Validation
{
    public sealed class ValidationResult
    {
        public readonly List<string> errors = new List<string>();
        public bool IsValid { get { return errors.Count == 0; } }
        public void Add(string message) { errors.Add(message); }
    }

    public static class MapValidator
    {
        public static ValidationResult Validate(MapProjectData project)
        {
            var result = new ValidationResult();
            if (project == null) { result.Add("Project is null."); return result; }
            if (string.IsNullOrWhiteSpace(project.projectId)) result.Add("Project ID is missing.");
            if (project.schemaVersion < 1) result.Add("Schema version is invalid.");
            if (project.floors == null || project.floors.Count == 0) { result.Add("At least one floor is required."); return result; }
            var projectIds = new HashSet<string>();
            foreach (var floor in project.floors)
            {
                if (floor == null) { result.Add("Floor is null."); continue; }
                CheckId(projectIds, floor.id, "floor", result);
                if (floor.buildingBoundary == null || floor.buildingBoundary.vertices == null || floor.buildingBoundary.vertices.Count < 3)
                    result.Add("Floor " + floor.id + " boundary must have at least 3 vertices.");
                var ids = new HashSet<string>();
                if (floor.rooms != null) foreach (var room in floor.rooms)
                {
                    if (room == null) { result.Add("Room is null."); continue; }
                    CheckId(ids, room.id, "room", result);
                    if (room.polygon == null || room.polygon.vertices == null || room.polygon.vertices.Count < 3) result.Add("Room " + room.id + " polygon must have at least 3 vertices.");
                    if (room.heightMetres <= 0) result.Add("Room " + room.id + " height must be positive.");
                }
                if (floor.walls != null) foreach (var wall in floor.walls)
                {
                    if (wall == null) { result.Add("Wall is null."); continue; }
                    CheckId(ids, wall.id, "wall", result);
                    if (wall.heightMetres <= 0 || wall.thicknessMetres <= 0) result.Add("Wall " + wall.id + " dimensions must be positive.");
                }
                if (floor.doors != null) foreach (var door in floor.doors)
                {
                    if (door == null) { result.Add("Door is null."); continue; }
                    CheckId(ids, door.id, "door", result);
                    if (string.IsNullOrWhiteSpace(door.parentWallId) || !ContainsId(floor.walls, door.parentWallId)) result.Add("Door " + door.id + " references a missing wall.");
                    if (door.widthMetres <= 0) result.Add("Door " + door.id + " width must be positive.");
                }
                if (floor.navigation != null)
                {
                    var nodeIds = new HashSet<string>();
                    if (floor.navigation.nodes != null) foreach (var node in floor.navigation.nodes) { if (node == null) { result.Add("Navigation node is null."); continue; } CheckId(nodeIds, node.id, "navigation node", result); }
                    if (floor.navigation.edges != null) foreach (var edge in floor.navigation.edges) if (edge == null || !ContainsNode(floor.navigation.nodes, edge.fromNodeId) || !ContainsNode(floor.navigation.nodes, edge.toNodeId)) result.Add("Navigation edge references a missing node.");
                }
                var annotations = AnnotationValidator.Validate(floor.sourceAnnotations ?? new FloorPlanAnnotationData());
                foreach (var error in annotations.errors) result.Add("Floor " + floor.id + ": " + error);
            }
            return result;
        }

        private static void CheckId(HashSet<string> ids, string id, string kind, ValidationResult result) { if (string.IsNullOrWhiteSpace(id)) result.Add(kind + " ID is missing."); else if (!ids.Add(id)) result.Add("Duplicate " + kind + " ID: " + id + "."); }
        private static bool ContainsId(List<WallData> walls, string id) { if (walls == null) return false; foreach (var w in walls) if (w != null && w.id == id) return true; return false; }
        private static bool ContainsNode(List<NavigationNodeData> nodes, string id) { if (nodes == null) return false; foreach (var n in nodes) if (n != null && n.id == id) return true; return false; }
    }
}
