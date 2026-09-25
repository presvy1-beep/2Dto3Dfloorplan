using ARNav.Core.Geometry;
using ARNav.Core.Navigation;

namespace ARNav.Core.Data
{
    public static class TestMapFactory
    {
        public static MapProjectData CreateTinyThreeRoomMap()
        {
            var map = MapProjectData.Create("test-three-room", "Tiny Three Room Map");
            map.sourceImage.fileName = "fictional-floorplan.png"; map.sourceImage.originalWidthPixels = 1100; map.sourceImage.originalHeightPixels = 400;
            var floor = new FloorData { id = "floor_0", name = "Ground Floor", level = 0 };
            floor.buildingBoundary.vertices.Add(new LocalPoint(0, 0)); floor.buildingBoundary.vertices.Add(new LocalPoint(11, 0)); floor.buildingBoundary.vertices.Add(new LocalPoint(11, 4)); floor.buildingBoundary.vertices.Add(new LocalPoint(0, 4));
            AddRoom(floor, "room_a", "Room A", 0, 4); AddRoom(floor, "corridor", "Corridor", 4, 7); floor.rooms[1].regionType = "Corridor"; floor.rooms[1].destinationSelectable = false; AddRoom(floor, "room_b", "Room B", 7, 11);
            floor.walls.Add(new WallData { id = "wall_a_corridor", start = new LocalPoint(4, 0), end = new LocalPoint(4, 4), doorIds = new System.Collections.Generic.List<string> { "door_a_corridor" } });
            floor.walls.Add(new WallData { id = "wall_corridor_b", start = new LocalPoint(7, 0), end = new LocalPoint(7, 4), doorIds = new System.Collections.Generic.List<string> { "door_corridor_b" } });
            floor.doors.Add(new DoorData { id = "door_a_corridor", parentWallId = "wall_a_corridor", distanceAlongWallMetres = 2, connectedRegionIds = new System.Collections.Generic.List<string> { "room_a", "corridor" } });
            floor.doors.Add(new DoorData { id = "door_corridor_b", parentWallId = "wall_corridor_b", distanceAlongWallMetres = 2, connectedRegionIds = new System.Collections.Generic.List<string> { "corridor", "room_b" } });
            floor.rooms[0].doorIds.Add("door_a_corridor"); floor.rooms[1].doorIds.Add("door_a_corridor"); floor.rooms[1].doorIds.Add("door_corridor_b"); floor.rooms[2].doorIds.Add("door_corridor_b");
            AddNode(floor, "node_room_a", new LocalPoint(2, 2), "room_a", NavigationNodeType.RoomEntry); AddNode(floor, "node_door_a", new LocalPoint(4, 2), "corridor", NavigationNodeType.Door); AddNode(floor, "node_door_b", new LocalPoint(7, 2), "corridor", NavigationNodeType.Door); AddNode(floor, "node_room_b", new LocalPoint(9, 2), "room_b", NavigationNodeType.RoomEntry);
            AddEdge(floor, "node_room_a", "node_door_a", 2); AddEdge(floor, "node_door_a", "node_door_b", 3); AddEdge(floor, "node_door_b", "node_room_b", 2);
            map.floors.Add(floor); return map;
        }
        private static void AddRoom(FloorData f, string id, string name, float left, float right) { var r = new RoomData { id = id, displayName = name }; r.polygon.vertices.Add(new LocalPoint(left, 0)); r.polygon.vertices.Add(new LocalPoint(right, 0)); r.polygon.vertices.Add(new LocalPoint(right, 4)); r.polygon.vertices.Add(new LocalPoint(left, 4)); f.rooms.Add(r); }
        private static void AddNode(FloorData f, string id, LocalPoint p, string region, NavigationNodeType type) { f.navigation.nodes.Add(new NavigationNodeData { id = id, localPosition = p, regionId = region, type = type }); }
        private static void AddEdge(FloorData f, string from, string to, float distance) { f.navigation.edges.Add(new NavigationEdgeData { fromNodeId = from, toNodeId = to, distanceMetres = distance, cost = distance }); }
    }
}
