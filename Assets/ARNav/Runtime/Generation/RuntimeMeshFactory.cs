using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Generation;
using ARNav.Core.Geometry;
using UnityEngine;

namespace ARNav.Runtime.Generation
{
    public static class RuntimeMeshFactory
    {
        public static Mesh CreateFloorMesh(RoomGenerationPlan room, float elevation, float uvScale)
        {
            if (room.rasterFallback) return CreateRasterFloorMesh(room.source, elevation, uvScale);
            var vertices = new Vector3[room.source.polygon.vertices.Count]; var uvs = new Vector2[vertices.Length];
            for (var i = 0; i < vertices.Length; i++) { var p = room.source.polygon.vertices[i]; vertices[i] = new Vector3(p.x, elevation, p.z); uvs[i] = new Vector2(p.x / uvScale, p.z / uvScale); }
            var mesh = new Mesh { name = "FloorMesh_" + room.source.id }; mesh.vertices = vertices; mesh.triangles = room.triangles.ToArray(); mesh.uv = uvs; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        public static Mesh CreateRasterFloorMesh(RoomData room, float elevation, float uvScale)
        {
            var vertices = new List<Vector3>(); var uvs = new List<Vector2>(); var triangles = new List<int>();
            foreach (var run in room.rasterRuns)
            {
                var index = vertices.Count; var y = elevation;
                vertices.Add(new Vector3(run.minX, y, run.minZ)); vertices.Add(new Vector3(run.maxX, y, run.minZ));
                vertices.Add(new Vector3(run.maxX, y, run.maxZ)); vertices.Add(new Vector3(run.minX, y, run.maxZ));
                uvs.Add(new Vector2(run.minX / uvScale, run.minZ / uvScale)); uvs.Add(new Vector2(run.maxX / uvScale, run.minZ / uvScale));
                uvs.Add(new Vector2(run.maxX / uvScale, run.maxZ / uvScale)); uvs.Add(new Vector2(run.minX / uvScale, run.maxZ / uvScale));
                triangles.Add(index); triangles.Add(index + 2); triangles.Add(index + 1); triangles.Add(index); triangles.Add(index + 3); triangles.Add(index + 2);
            }
            if (vertices.Count == 0) throw new System.InvalidOperationException("Raster fallback room has no occupied runs: " + room.id);
            var mesh = new Mesh { name = "RasterFloorMesh_" + room.id };
            if (vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices); mesh.SetUVs(0, uvs); mesh.SetTriangles(triangles, 0, true); mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        public static Mesh CreateWallPieceMesh(WallData wall, WallPiecePlan piece, float elevation, float uvScale)
        {
            var a = new Vector2(wall.start.x, wall.start.z); var b = new Vector2(wall.end.x, wall.end.z); var direction = (b - a).normalized; var perpendicular = new Vector2(-direction.y, direction.x); var half = wall.thicknessMetres * 0.5f;
            var start = Vector2.Lerp(a, b, piece.startT); var end = Vector2.Lerp(a, b, piece.endT); var low = elevation + piece.bottomMetres; var high = elevation + piece.topMetres;
            var corners = new Vector2[4] { start + perpendicular * half, start - perpendicular * half, end - perpendicular * half, end + perpendicular * half };
            var vertices = new Vector3[8]; for (var i = 0; i < 4; i++) { vertices[i] = new Vector3(corners[i].x, low, corners[i].y); vertices[i + 4] = new Vector3(corners[i].x, high, corners[i].y); }
            var triangles = new[] { 0, 4, 5, 0, 5, 1, 1, 5, 6, 1, 6, 2, 2, 6, 7, 2, 7, 3, 3, 7, 4, 3, 4, 0, 4, 7, 6, 4, 6, 5, 0, 1, 2, 0, 2, 3 };
            var uvs = new Vector2[8]; for (var i = 0; i < 4; i++) { uvs[i] = new Vector2(i / uvScale, low / uvScale); uvs[i + 4] = new Vector2(i / uvScale, high / uvScale); }
            var mesh = new Mesh { name = "WallMesh_" + wall.id + "_" + piece.type }; mesh.vertices = vertices; mesh.triangles = triangles; mesh.uv = uvs; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        public static void AddMeshObject(GameObject parent, string name, Mesh mesh, Material material, bool collider)
        {
            var objectRoot = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); objectRoot.transform.SetParent(parent.transform, false); objectRoot.GetComponent<MeshFilter>().sharedMesh = mesh; objectRoot.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (collider) objectRoot.AddComponent<MeshCollider>().sharedMesh = mesh;
        }

        public static Mesh CreatePresentationBaseMesh(Bounds wallBounds, float topY, float thickness)
        {
            if (wallBounds.size.x <= 0f || wallBounds.size.z <= 0f || !(thickness > 0f)) return null;
            var min = new Vector3(wallBounds.min.x, topY - thickness, wallBounds.min.z);
            var max = new Vector3(wallBounds.max.x, topY, wallBounds.max.z);
            var v = new[]
            {
                new Vector3(min.x,min.y,min.z), new Vector3(max.x,min.y,min.z), new Vector3(max.x,min.y,max.z), new Vector3(min.x,min.y,max.z),
                new Vector3(min.x,max.y,min.z), new Vector3(max.x,max.y,min.z), new Vector3(max.x,max.y,max.z), new Vector3(min.x,max.y,max.z)
            };
            var t = new[] { 0,2,1, 0,3,2, 4,5,6, 4,6,7, 0,1,5, 0,5,4, 1,2,6, 1,6,5, 2,3,7, 2,7,6, 3,0,4, 3,4,7 };
            var mesh = new Mesh { name = "PresentationBaseMesh" }; mesh.vertices = v; mesh.triangles = t; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }
    }
}
