using ARNav.Core.Data;
using ARNav.Core.Geometry;

namespace ARNav.Runtime.App
{
    public static class MapProjectFactory
    {
        public static MapProjectData CreateBlank(string id, string name)
        {
            var project = MapProjectData.Create(id, name);
            var floor = new FloorData { id = "floor_0", name = "Ground Floor", level = 0 };
            floor.buildingBoundary.vertices.Add(new LocalPoint(0, 0)); floor.buildingBoundary.vertices.Add(new LocalPoint(10, 0)); floor.buildingBoundary.vertices.Add(new LocalPoint(10, 10)); floor.buildingBoundary.vertices.Add(new LocalPoint(0, 10));
            project.floors.Add(floor); return project;
        }
    }
}
