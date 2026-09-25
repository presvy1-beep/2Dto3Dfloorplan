using System.Collections.Generic;
using ARNav.Core.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace ARNav.Runtime.Navigation
{
    public enum RuntimeNavMeshState { NotBuilt, Building, Ready, Failed }

    /// <summary>Builds one transient NavMesh from colliders below the generated building only.</summary>
    public sealed class RuntimeNavMeshBuilder : MonoBehaviour
    {
        [SerializeField] private NavigationSettings settings = new NavigationSettings();
        private NavMeshData navMeshData;
        private NavMeshDataInstance navMeshInstance;
        public RuntimeNavMeshState State { get; private set; } = RuntimeNavMeshState.NotBuilt;
        public NavigationSettings Settings { get { return settings; } }
        public bool Build(GameObject generatedBuilding, Bounds bounds, out string error)
        {
            error = null; Clear(); State = RuntimeNavMeshState.Building; if (generatedBuilding == null) { State = RuntimeNavMeshState.Failed; error = "Generated building is missing."; return false; }
            var sources = CollectGeneratedSources(generatedBuilding.transform);
            if (sources.Count == 0) { State = RuntimeNavMeshState.Failed; error = "No generated floor/wall colliders were found."; return false; }
            var buildSettings = NavMesh.GetSettingsCount() > 0 ? NavMesh.GetSettingsByIndex(0) : NavMesh.CreateSettings(); buildSettings.agentRadius = settings.agentRadiusMetres; buildSettings.agentHeight = settings.agentHeightMetres; buildSettings.agentSlope = settings.maxSlopeDegrees; buildSettings.agentClimb = settings.stepHeightMetres; buildSettings.minRegionArea = 0.01f; buildSettings.overrideTileSize = true; buildSettings.tileSize = 256;
            bounds.Expand(2f);
            Debug.Log("[ARNav][Navigation] NavMesh settings: agentType=" + buildSettings.agentTypeID + " radius=" + buildSettings.agentRadius + " height=" + buildSettings.agentHeight + " climb=" + buildSettings.agentClimb + " slope=" + buildSettings.agentSlope + " minRegion=" + buildSettings.minRegionArea + " bounds=" + bounds + " floorSources=" + sources.FindAll(s => s.area == 0).Count + " blockerSources=" + sources.FindAll(s => s.area != 0).Count);
            navMeshData = NavMeshBuilder.BuildNavMeshData(buildSettings, sources, bounds, Vector3.zero, Quaternion.identity); if (navMeshData == null) { State = RuntimeNavMeshState.Failed; error = "Unity could not build a NavMesh from the generated geometry."; return false; }
            navMeshInstance = NavMesh.AddNavMeshData(navMeshData); var triangulation = NavMesh.CalculateTriangulation(); Debug.Log("[ARNav][Navigation] Runtime NavMesh registered: sources=" + sources.Count + " vertices=" + triangulation.vertices.Length + " indices=" + triangulation.indices.Length); State = RuntimeNavMeshState.Ready; return true;
        }
        public void Clear() { if (navMeshInstance.valid) navMeshInstance.Remove(); navMeshInstance = default(NavMeshDataInstance); if (navMeshData != null) DestroyObject(navMeshData); navMeshData = null; State = RuntimeNavMeshState.NotBuilt; }
        public bool VerifyRegistered(out string error)
        { var triangulation = NavMesh.CalculateTriangulation(); if (triangulation.vertices == null || triangulation.vertices.Length == 0 || triangulation.indices == null || triangulation.indices.Length == 0) { error = "Unity registered the NavMesh but produced no walkable polygons for the current geometry."; State = RuntimeNavMeshState.Failed; return false; } error = null; Debug.Log("[ARNav][Navigation] Runtime NavMesh verified: vertices=" + triangulation.vertices.Length + " indices=" + triangulation.indices.Length); return true; }
        private static List<NavMeshBuildSource> CollectGeneratedSources(Transform root)
        {
            var sources = new List<NavMeshBuildSource>();
            var fallbackFloors = new List<NavMeshBuildSource>();
            var hasDedicatedFloor = false;
            var floorArea = 0;
            var wallArea = NavMesh.GetAreaFromName("Not Walkable"); if (wallArea < 0) wallArea = 1;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                // Only the dedicated, non-overlapping navigation surface is walkable.
                // Visual room floors are intentionally excluded from NavMesh sources.
                if (filter.gameObject.name.StartsWith("FloorMesh", System.StringComparison.Ordinal))
                {
                    fallbackFloors.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = filter.sharedMesh, transform = filter.transform.localToWorldMatrix, area = 0 });
                    continue;
                }
                var isFloor = filter.gameObject.name == "NavigationFloorMesh";
                if (isFloor) { hasDedicatedFloor = true; sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Mesh, sourceObject = filter.sharedMesh, transform = filter.transform.localToWorldMatrix, area = floorArea }); continue; }
                // Use each wall piece's bounded footprint as a blocker. A box avoids
                // feeding Unity noisy vertical/closed wall topology while preserving
                // the intended solid barrier footprint.
                var local = filter.sharedMesh.bounds;
                var matrix = filter.transform.localToWorldMatrix * Matrix4x4.TRS(local.center, Quaternion.identity, Vector3.one);
                sources.Add(new NavMeshBuildSource { shape = NavMeshBuildSourceShape.Box, size = local.size, transform = matrix, area = wallArea });
            }
            // Direct generator tests and older callers do not add the dedicated
            // navigation surface. Preserve that API contract without allowing
            // overlapping visual floors into a normal imported-map build.
            if (!hasDedicatedFloor) sources.AddRange(fallbackFloors);
            return sources;
        }
        private static void DestroyObject(Object value) { if (value == null) return; if (Application.isPlaying) Destroy(value); else DestroyImmediate(value); }
        private void OnDestroy() { Clear(); }
    }
}
