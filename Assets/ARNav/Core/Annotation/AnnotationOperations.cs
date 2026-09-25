using ARNav.Core.Data;

namespace ARNav.Core.Annotation
{
    public static class AnnotationOperations
    {
        public static void DeleteWallAndDoors(FloorPlanAnnotationData data, string wallId)
        {
            if (data == null) return;
            if (data.walls != null) data.walls.RemoveAll(wall => wall != null && wall.id == wallId);
            if (data.doors != null) data.doors.RemoveAll(door => door != null && door.wallId == wallId);
        }

        public static bool RenameRoom(FloorPlanAnnotationData data, string roomId, string displayName)
        {
            var room = AnnotationHitTester.FindRoom(data, roomId); displayName = (displayName ?? "").Trim();
            if (room == null || displayName.Length == 0) return false;
            room.displayName = displayName; return true;
        }
    }
}
