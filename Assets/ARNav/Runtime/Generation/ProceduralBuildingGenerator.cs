using System;
using System.Collections.Generic;
using System.IO;
using ARNav.Core.Data;
using ARNav.Core.Generation;
using ARNav.Core.Geometry;
using UnityEngine;

namespace ARNav.Runtime.Generation
{
    /// <summary>Builds transient Unity geometry from canonical local metre data. Nothing generated here is persisted.</summary>
    public sealed class ProceduralBuildingGenerator : MonoBehaviour
    {
        [SerializeField] private Material floorMaterial;
        [SerializeField] private Material wallMaterial;
        [SerializeField] private float floorUvScaleMetres = 1f;
        [SerializeField] private float wallHeightMetres = WallPathGeometryConfiguration.DefaultWallHeightMeters;
        [SerializeField] private float baseThicknessMetres = WallPathGeometryConfiguration.DefaultBaseThicknessMeters;
        [SerializeField] private bool generateColliders = true;
        private GameObject generatedRoot;
        private readonly List<Mesh> ownedMeshes = new List<Mesh>();
        private Material runtimeFloorMaterial;
        private Material runtimeWallMaterial;
        private Material runtimeBaseMaterial;
        private Texture2D runtimeFloorTexture;
        private Texture2D runtimeWallTexture;
        public Bounds GeneratedBounds { get; private set; }
        public GameObject GeneratedRoot { get { return generatedRoot; } }
        public string GeneratedProjectId { get; private set; }
        public string GeneratedSourceSha256 { get; private set; }

        public bool Generate(MapProjectData project, out string error)
        {
            error = null; var plan = BuildingGenerationPlanner.Build(project); if (!plan.IsValid) { error = string.Join(" ", plan.errors.ToArray()); return false; }
            foreach (var warning in plan.warnings)
            {
                Debug.LogWarning("[ARNav][Geometry] " + warning);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                try { File.AppendAllText(Path.Combine(Application.temporaryCachePath, "arnav-geometry-diagnostics.txt"), warning + "\n"); } catch (IOException io) { Debug.LogWarning("[ARNav][Geometry] diagnostic write failed: " + io.Message); }
#endif
            }
            var next = new GameObject("GeneratedBuilding"); var nextMeshes = new List<Mesh>(); var boundsSet = false; var bounds = new Bounds();
            try
            {
                foreach (var floorPlan in plan.floors) GenerateFloor(next, floorPlan, project, nextMeshes, ref bounds, ref boundsSet);
                // The imported JSON map plane is vertically inverted. Reflect the
                // complete generated hierarchy once about its Z-centre. Navigation
                // floor meshes added later and NavMesh sources inherit this transform.
                next.transform.position = new Vector3(0f, 0f, bounds.center.z * 2f);
                next.transform.localScale = new Vector3(1f, 1f, -1f);
                ClearGeneratedBuilding(); generatedRoot = next; ownedMeshes.AddRange(nextMeshes); GeneratedBounds = bounds;
                GeneratedProjectId = project.projectId;
                GeneratedSourceSha256 = project.sourceImage == null ? null : project.sourceImage.sha256;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                var renderers = generatedRoot.GetComponentsInChildren<MeshRenderer>(true);
                var meshVertices = 0;
                foreach (var filter in generatedRoot.GetComponentsInChildren<MeshFilter>(true))
                    if (filter.sharedMesh != null) meshVertices += filter.sharedMesh.vertexCount;
                Debug.Log("[ARNav][Geometry] Generated root=" + generatedRoot.name + " active=" + generatedRoot.activeInHierarchy +
                          " meshes=" + nextMeshes.Count + " renderers=" + renderers.Length + " vertices=" + meshVertices +
                          " bounds=" + GeneratedBounds + " worldBounds=" + GetWorldRendererBounds(renderers));
#endif
                return true;
            }
            catch (Exception exception) { DestroyObject(next); foreach (var mesh in nextMeshes) DestroyObject(mesh); error = "3D generation failed: " + exception.Message; return false; }
        }

        public void ClearGeneratedBuilding()
        {
            if (generatedRoot != null) DestroyObject(generatedRoot); generatedRoot = null; foreach (var mesh in ownedMeshes) DestroyObject(mesh); ownedMeshes.Clear(); GeneratedBounds = new Bounds(); GeneratedProjectId = null; GeneratedSourceSha256 = null;
        }

        private void GenerateFloor(GameObject root, FloorGenerationPlan floorPlan, MapProjectData project, List<Mesh> meshes, ref Bounds bounds, ref bool boundsSet)
        {
            var floorRoot = new GameObject("Floor_" + floorPlan.source.id); floorRoot.transform.SetParent(root.transform, false); var roomsRoot = new GameObject("Rooms"); roomsRoot.transform.SetParent(floorRoot.transform, false); var wallsRoot = new GameObject("Walls"); wallsRoot.transform.SetParent(floorRoot.transform, false);
            foreach (var roomPlan in floorPlan.rooms)
            {
                try
                {
                    var roomRoot = new GameObject("Room_" + roomPlan.source.id); roomRoot.transform.SetParent(roomsRoot.transform, false); var metadata = roomRoot.AddComponent<GeneratedRoomMetadata>(); metadata.floorId = floorPlan.source.id; metadata.roomId = roomPlan.source.id; metadata.displayName = roomPlan.source.displayName;
                    var mesh = RuntimeMeshFactory.CreateFloorMesh(roomPlan, floorPlan.source.elevationMetres, floorUvScaleMetres); meshes.Add(mesh); RuntimeMeshFactory.AddMeshObject(roomRoot, roomPlan.rasterFallback ? "RasterFloorFallback" : "FloorMesh", mesh, GetFloorMaterial(), generateColliders); UpdateBounds(mesh.bounds, roomRoot.transform.localToWorldMatrix, ref bounds, ref boundsSet);
                }
                catch (Exception exception) { Debug.LogError("[ARNav][Geometry] Room generation failed independently for " + roomPlan.source.id + ": " + exception.Message); }
            }
            if (floorPlan.usesWallPaths)
            {
                var wallMesh = WallPathMeshBuilder.Build(floorPlan.source.architecturalWallPaths,
                    floorPlan.source.elevationMetres, Mathf.Max(0.1f, wallHeightMetres), out var diagnostic);
                if (wallMesh != null)
                {
                    ApplyArchitecturalWallUvs(wallMesh, floorUvScaleMetres);
                    meshes.Add(wallMesh); RuntimeMeshFactory.AddMeshObject(wallsRoot, "VectorWallMass", wallMesh, GetWallMaterial(), generateColliders); UpdateBounds(wallMesh.bounds, wallsRoot.transform.localToWorldMatrix, ref bounds, ref boundsSet);
                    // A wall-bounds rectangle fills courtyards, corridors, and exterior
                    // voids of irregular plans. Room meshes already provide the floor.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    var rawPixels = floorPlan.source.sourceAnnotations == null || floorPlan.source.sourceAnnotations.wallPathDiagnostics == null ? 0 : floorPlan.source.sourceAnnotations.wallPathDiagnostics.rawMaskPixels;
                    var scale = project == null || project.scaleCalibration == null ? 0 : project.scaleCalibration.metresPerPixel;
                    var rawArea = rawPixels * scale * scale;
                    var areaChange = rawArea <= 0 ? 0 : (diagnostic.footprintAreaSquareMetres - rawArea) * 100f / rawArea;
                    Debug.Log("[ARNav][WallPaths] paths=" + diagnostic.pathCount + " points=" + diagnostic.inputPoints +
                              " width=" + FormatWidthRange(floorPlan.source.architecturalWallPaths) +
                              " meshGrid=" + diagnostic.gridWidth + "x" + diagnostic.gridHeight + " cell=" + diagnostic.cellSizeMetres.ToString("0.####") +
                              "m area=" + diagnostic.footprintAreaSquareMetres.ToString("0.###") + "m2 rawArea=" + rawArea.ToString("0.###") + "m2 delta=" + areaChange.ToString("0.0") +
                              "% vertices=" + wallMesh.vertexCount + " triangles=" + wallMesh.triangles.Length / 3);
#endif
                }
                else Debug.LogWarning("[ARNav][WallPaths] Serialized paths were invalid; raw wallRuns fallback used for floor " + floorPlan.source.id + ".");
                if (wallMesh == null) AddRasterFallback(floorPlan, wallsRoot, meshes, ref bounds, ref boundsSet);
            }
            else if (floorPlan.usesRasterWallMask)
            {
                Debug.Log("[ARNav][Walls] Building the saved structural wall mask for floor " + floorPlan.source.id + ".");
                AddRasterFallback(floorPlan, wallsRoot, meshes, ref bounds, ref boundsSet);
            }
            foreach (var wallPlan in floorPlan.walls)
            {
                var wall = FindWall(floorPlan.source, wallPlan.wallId); var wallRoot = new GameObject("Wall_" + wall.id); wallRoot.transform.SetParent(wallsRoot.transform, false); var metadata = wallRoot.AddComponent<GeneratedWallMetadata>(); metadata.floorId = floorPlan.source.id; metadata.wallId = wall.id;
                foreach (var piece in wallPlan.pieces)
                { var mesh = RuntimeMeshFactory.CreateWallPieceMesh(wall, piece, floorPlan.source.elevationMetres, floorUvScaleMetres); meshes.Add(mesh); RuntimeMeshFactory.AddMeshObject(wallRoot, piece.type + (string.IsNullOrEmpty(piece.doorId) ? "" : "_" + piece.doorId), mesh, GetWallMaterial(), generateColliders); UpdateBounds(mesh.bounds, wallRoot.transform.localToWorldMatrix, ref bounds, ref boundsSet); }
                var doorRoot = new GameObject("DoorMarkers"); doorRoot.transform.SetParent(wallRoot.transform, false); foreach (var door in wallPlan.doors) { var marker = new GameObject("Door_" + door.doorId); marker.transform.SetParent(doorRoot.transform, false); var center = Vector3.Lerp(new Vector3(wall.start.x, floorPlan.source.elevationMetres, wall.start.z), new Vector3(wall.end.x, floorPlan.source.elevationMetres, wall.end.z), (door.startT + door.endT) * 0.5f); marker.transform.position = center; var doorMetadata = marker.AddComponent<GeneratedDoorMetadata>(); doorMetadata.floorId = floorPlan.source.id; doorMetadata.wallId = wall.id; doorMetadata.doorId = door.doorId; doorMetadata.widthMetres = door.widthMetres; }
            }
        }

        private static WallData FindWall(FloorData floor, string id) { foreach (var wall in floor.walls) if (wall != null && wall.id == id) return wall; return null; }
        private static Mesh CreateWallMaskMesh(List<LocalRasterRun> runs, float baseY, float height)
        { return ArchitecturalWallMaskMeshBuilder.Build(runs, baseY, height); }
        private static void ApplyArchitecturalWallUvs(Mesh mesh, float scale)
        {
            if (mesh == null) return;
            scale = Mathf.Max(0.001f, scale);
            var vertices = mesh.vertices; var normals = mesh.normals; var uv = new Vector2[vertices.Length];
            for (var i = 0; i < vertices.Length; i++)
            {
                var normal = i < normals.Length ? normals[i] : Vector3.up;
                if (Mathf.Abs(normal.y) > 0.5f) uv[i] = new Vector2(vertices[i].x / scale, vertices[i].z / scale);
                else if (Mathf.Abs(normal.x) > 0.5f) uv[i] = new Vector2(vertices[i].z / scale, vertices[i].y / scale);
                else uv[i] = new Vector2(vertices[i].x / scale, vertices[i].y / scale);
            }
            mesh.uv = uv;
        }
        private void AddRasterFallback(FloorGenerationPlan floorPlan, GameObject wallsRoot, List<Mesh> meshes, ref Bounds bounds, ref bool boundsSet)
        { var mesh = CreateWallMaskMesh(floorPlan.source.architecturalWallRuns, floorPlan.source.elevationMetres, Mathf.Max(0.1f, wallHeightMetres)); if (mesh != null) { ApplyArchitecturalWallUvs(mesh, floorUvScaleMetres); meshes.Add(mesh); RuntimeMeshFactory.AddMeshObject(wallsRoot, "RasterWallMaskFallback", mesh, GetWallMaterial(), generateColliders); UpdateBounds(mesh.bounds, wallsRoot.transform.localToWorldMatrix, ref bounds, ref boundsSet); } }
        private static string FormatWidthRange(List<LocalWallPathData> paths)
        { var min=float.PositiveInfinity;var max=0f;foreach(var path in paths)if(path!=null&&path.widthMetres>0){min=Mathf.Min(min,path.widthMetres);max=Mathf.Max(max,path.widthMetres);}return float.IsPositiveInfinity(min)?"none":min.ToString("0.###")+"-"+max.ToString("0.###")+"m"; }
        private static void UpdateBounds(Bounds local, Matrix4x4 matrix, ref Bounds output, ref bool set) { var corners = new[] { new Vector3(local.min.x, local.min.y, local.min.z), new Vector3(local.max.x, local.min.y, local.min.z), new Vector3(local.min.x, local.max.y, local.min.z), new Vector3(local.max.x, local.max.y, local.min.z), new Vector3(local.min.x, local.min.y, local.max.z), new Vector3(local.max.x, local.min.y, local.max.z), new Vector3(local.min.x, local.max.y, local.max.z), new Vector3(local.max.x, local.max.y, local.max.z) }; foreach (var corner in corners) { var point = matrix.MultiplyPoint3x4(corner); if (!set) { output = new Bounds(point, Vector3.zero); set = true; } else output.Encapsulate(point); } }
        private Material GetFloorMaterial() { if (runtimeFloorMaterial == null) runtimeFloorMaterial = floorMaterial != null ? CreateTexturedMaterial(floorMaterial, true) : CreateTexturedMaterial("ARNav Tiled Floor", new Color(0.86f, 0.88f, 0.90f, 1f), true); return runtimeFloorMaterial; }
        private Material GetWallMaterial() { if (runtimeWallMaterial == null) runtimeWallMaterial = wallMaterial != null ? CreateTexturedMaterial(wallMaterial, false) : CreateTexturedMaterial("ARNav Plaster Wall", new Color(0.34f, 0.43f, 0.52f, 1f), false); return runtimeWallMaterial; }
        private Material CreateTexturedMaterial(Material source, bool floor)
        {
            // The scene references legacy Standard materials. Treat them only as
            // assignment markers; the runtime architectural palette is canonical.
            var tint = floor ? new Color(0.86f, 0.88f, 0.90f, 1f) : new Color(0.34f, 0.43f, 0.52f, 1f);
            return CreateTexturedMaterial(source.name + (floor ? " (Runtime Tile)" : " (Runtime Plaster)"), tint, floor);
        }
        private Material CreateTexturedMaterial(string name, Color tint, bool floor)
        {
            // The scene's serialized materials use Unity's built-in Standard
            // shader. Preserve their tint, but create the runtime renderer material
            // with URP Unlit so Android URP cannot silently replace it with white.
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP Unlit shader is unavailable for generated architecture.");
            var material = new Material(shader) { name = name, enableInstancing = true };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
            var textureProperty = material.HasProperty("_BaseMap") ? "_BaseMap" : material.HasProperty("_MainTex") ? "_MainTex" : null;
            if (textureProperty == null) { DestroyObject(material); throw new InvalidOperationException("URP Unlit shader has no supported base texture property."); }
            var texture = floor ? (runtimeFloorTexture ?? (runtimeFloorTexture = MakeFloorTexture())) : (runtimeWallTexture ?? (runtimeWallTexture = MakeWallTexture()));
            texture.wrapMode = TextureWrapMode.Repeat;
            material.SetTexture(textureProperty, texture);
            return material;
        }
        private static Texture2D MakeFloorTexture()
        {
            const int size = 64; var texture = new Texture2D(size, size, TextureFormat.RGB24, false, true) { name = "ARNav Subtle Floor Tile", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            {
                var grout = x < 2 || y < 2;
                var tone = grout ? 0.72f : (((x / 32 + y / 32) & 1) == 0 ? 0.98f : 0.94f);
                pixels[y * size + x] = new Color(tone, tone, tone, 1f);
            }
            texture.SetPixels(pixels); texture.Apply(false, true); return texture;
        }
        private static Texture2D MakeWallTexture()
        {
            const int size = 32; var texture = new Texture2D(size, size, TextureFormat.RGB24, false, true) { name = "ARNav Fine Plaster", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
            {
                var grain = ((x * 17 + y * 29 + (x * y) % 13) % 9) * 0.004f;
                pixels[y * size + x] = new Color(0.88f + grain, 0.90f + grain, 0.92f + grain, 1f);
            }
            texture.SetPixels(pixels); texture.Apply(false, true); return texture;
        }
        private Material GetBaseMaterial() { if (runtimeBaseMaterial == null) runtimeBaseMaterial = CreateMaterial("ARNav Base Light Yellow", WallPresentationConfiguration.BaseColor); return runtimeBaseMaterial; }
        private static Material CreateMaterial(string name, Color color) { var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Universal Render Pipeline/Unlit"); if (shader == null) throw new InvalidOperationException("No URP-compatible material shader is available."); var material = new Material(shader) { name = name }; material.color = color; if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color); if (material.HasProperty("_Color")) material.SetColor("_Color", color); return material; }
        private static void DestroyObject(UnityEngine.Object value) { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private static Bounds GetWorldRendererBounds(Renderer[] renderers)
        {
            var hasBounds = false;
            var bounds = new Bounds();
            foreach (var renderer in renderers)
            {
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return hasBounds ? bounds : new Bounds();
        }
#endif
        private void OnDestroy() { ClearGeneratedBuilding(); DestroyObject(runtimeFloorMaterial); DestroyObject(runtimeWallMaterial); DestroyObject(runtimeBaseMaterial); DestroyObject(runtimeFloorTexture); DestroyObject(runtimeWallTexture); }
    }
}
