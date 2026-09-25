using System.Collections.Generic;
using ARNav.Core.Annotation;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using UnityEngine;
using UnityEngine.UI;

namespace ARNav.Runtime.MapEditor
{
    public sealed class FloorPlanAnnotationOverlay : Graphic
    {
        [SerializeField] private Import.FloorPlanViewer viewer;
        private FloorPlanAnnotationData data;
        private readonly List<PixelPoint> draft = new List<PixelPoint>();
        private readonly List<FloorPlanRasterRunGraphic> rasterLayers = new List<FloorPlanRasterRunGraphic>();
        private AnnotationSelection selection;
        public FloorPlanAnnotationData Data { get { return data; } }
        public void SetData(FloorPlanAnnotationData value)
        {
            value = value ?? new FloorPlanAnnotationData();
            if (!ReferenceEquals(data, value)) { data = value; RebuildRasterLayers(); }
            SetVerticesDirty();
        }
        public void RefreshRasterLayers() { RebuildRasterLayers(); }
        public void SetSelection(AnnotationSelection value) { selection = value; RefreshRasterLayerColors(); SetVerticesDirty(); }
        public void SetDraft(IList<PixelPoint> points) { draft.Clear(); if (points != null) draft.AddRange(points); SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (viewer == null || !viewer.IsLoaded) return;
            if (data != null)
            {
                if (data.rooms != null) foreach (var room in data.rooms) if (room != null)
                {
                    var roomColor = selection.id == room.id ? new Color(1, 0.85f, 0.1f, 1) : new Color(0.1f, 0.8f, 1, 1);
                    DrawPolygon(vh, room.vertices, roomColor, room.preferRasterGeometry ? 2f : 3f);
                    if (room.holes != null) foreach (var hole in room.holes) if (hole != null) DrawPolygon(vh, hole.vertices, new Color(1f, 0.35f, 0.15f, 0.95f), 3f);
                }
                var hasArchitecturalMask = data.wallRuns != null && data.wallRuns.Count > 0;
                if (!hasArchitecturalMask)
                {
                    if (data.walls != null) foreach (var wall in data.walls) if (wall != null) DrawLine(vh, wall.start, wall.end, selection.id == wall.id ? new Color(1, 0.85f, 0.1f, 1) : Color.white, 5f);
                    if (data.doors != null) foreach (var door in data.doors) if (door != null) { var wall = AnnotationHitTester.FindWall(data, door.wallId); if (wall != null) DrawLine(vh, AnnotationHitTester.Lerp(wall.start, wall.end, door.startT), AnnotationHitTester.Lerp(wall.start, wall.end, door.endT), selection.id == door.id ? Color.yellow : Color.green, 9f); }
                }
                if (data.rooms != null) foreach (var room in data.rooms) if (room != null && !room.preferRasterGeometry && room.vertices != null) for (var i = 0; i < room.vertices.Count; i++) DrawHandle(vh, room.vertices[i], selection.kind == AnnotationSelectionKind.RoomVertex && selection.id == room.id && selection.vertexIndex == i ? Color.yellow : Color.cyan, 8f);
            }
            if (draft.Count > 1) for (var i = 1; i < draft.Count; i++) DrawLine(vh, draft[i - 1], draft[i], new Color(1, 0.6f, 0.1f, 1), 4f);
            if (draft.Count > 2) DrawLine(vh, draft[draft.Count - 1], draft[0], new Color(1, 0.6f, 0.1f, 0.45f), 2f);
        }

        private void RebuildRasterLayers()
        {
            foreach (var layer in rasterLayers) if (layer != null) Destroy(layer.gameObject);
            rasterLayers.Clear();
            if (data == null || viewer == null) return;
            if (data.rooms != null) foreach (var room in data.rooms)
                if (room != null && room.preferRasterGeometry && room.rasterRuns != null && room.rasterRuns.Count > 0)
                {
                    var fill = selection.id == room.id ? new Color(1f, 0.85f, 0.1f, 0.2f) : new Color(0.1f, 0.8f, 1f, 0.12f);
                    AddRasterLayers(room.rasterRuns, fill, room.id, false);
                }
            if (data.wallRuns != null && data.wallRuns.Count > 0) AddRasterLayers(data.wallRuns, new Color(0.04f, 0.05f, 0.06f, 1f), null, true);
        }

        private void AddRasterLayers(List<PixelRasterRun> runs, Color color, string roomId, bool wallLayer)
        {
            for (var first = 0; first < runs.Count; first += FloorPlanRasterRunGraphic.MaxRunsPerChunk)
            {
                var child = new GameObject(wallLayer ? "WallMaskChunk" : "RoomMaskChunk", typeof(RectTransform), typeof(CanvasRenderer), typeof(FloorPlanRasterRunGraphic));
                var rect = (RectTransform)child.transform; rect.SetParent(transform, false); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero; rect.localScale = Vector3.one;
                var count = Mathf.Min(FloorPlanRasterRunGraphic.MaxRunsPerChunk, runs.Count - first); var slice = runs.GetRange(first, count);
                var graphic = child.GetComponent<FloorPlanRasterRunGraphic>(); graphic.Configure(viewer, slice, color, roomId, wallLayer); rasterLayers.Add(graphic);
            }
        }

        private void RefreshRasterLayerColors()
        {
            foreach (var layer in rasterLayers) if (layer != null && !layer.WallLayer)
                layer.SetTint(selection.id == layer.RoomId ? new Color(1f, 0.85f, 0.1f, 0.2f) : new Color(0.1f, 0.8f, 1f, 0.12f));
        }
        private void DrawPolygon(VertexHelper vh, List<PixelPoint> points, Color color, float thickness) { if (points == null || points.Count < 2) return; for (var i = 0; i < points.Count; i++) DrawLine(vh, points[i], points[(i + 1) % points.Count], color, thickness); }
        private void DrawRasterRun(VertexHelper vh, PixelRasterRun run, Color color)
        {
            if (!viewer.TryImagePixelToViewportLocal(new PixelPoint(run.minX, run.minY), out var a) ||
                !viewer.TryImagePixelToViewportLocal(new PixelPoint(run.minX, run.maxY), out var b) ||
                !viewer.TryImagePixelToViewportLocal(new PixelPoint(run.maxX, run.maxY), out var c) ||
                !viewer.TryImagePixelToViewportLocal(new PixelPoint(run.maxX, run.minY), out var d)) return;
            AddQuad(vh, a, b, c, d, color);
        }
        private void DrawLine(VertexHelper vh, PixelPoint a, PixelPoint b, Color color, float thickness) { if (!viewer.TryImagePixelToViewportLocal(a, out var av) || !viewer.TryImagePixelToViewportLocal(b, out var bv)) return; var direction = (bv - av).normalized; var normal = new Vector2(-direction.y, direction.x) * thickness * 0.5f; AddQuad(vh, av - normal, av + normal, bv + normal, bv - normal, color); }
        private void DrawHandle(VertexHelper vh, PixelPoint point, Color color, float size) { if (!viewer.TryImagePixelToViewportLocal(point, out var p)) return; var half = size * 0.5f; AddQuad(vh, p + new Vector2(-half, -half), p + new Vector2(-half, half), p + new Vector2(half, half), p + new Vector2(half, -half), color); }
        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color) { var index = vh.currentVertCount; vh.AddVert(a, color, Vector2.zero); vh.AddVert(b, color, Vector2.zero); vh.AddVert(c, color, Vector2.zero); vh.AddVert(d, color, Vector2.zero); vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index + 2, index + 3, index); }
        protected override void OnDestroy() { foreach (var layer in rasterLayers) if (layer != null) Destroy(layer.gameObject); rasterLayers.Clear(); base.OnDestroy(); }
    }

    /// <summary>Draws a bounded subset of raster quads so the UI mesh never exceeds its 16-bit vertex limit.</summary>
    public sealed class FloorPlanRasterRunGraphic : Graphic
    {
        public const int MaxRunsPerChunk = 12000;
        public static int ChunkCountForRunCount(int count) { return count <= 0 ? 0 : (count + MaxRunsPerChunk - 1) / MaxRunsPerChunk; }
        private Import.FloorPlanViewer viewer;
        private List<PixelRasterRun> runs = new List<PixelRasterRun>();
        public string RoomId { get; private set; }
        public bool WallLayer { get; private set; }
        public void Configure(Import.FloorPlanViewer sourceViewer, List<PixelRasterRun> sourceRuns, Color tint, string roomId, bool wallLayer)
        { viewer = sourceViewer; runs = sourceRuns ?? new List<PixelRasterRun>(); RoomId = roomId; WallLayer = wallLayer; raycastTarget = false; SetTint(tint); }
        public void SetTint(Color tint) { color = Color.white; currentTint = tint; SetVerticesDirty(); }
        private Color currentTint = Color.white;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (viewer == null || !viewer.IsLoaded) return;
            foreach (var run in runs)
            {
                if (run == null || !viewer.TryImagePixelToViewportLocal(new PixelPoint(run.minX, run.minY), out var a) ||
                    !viewer.TryImagePixelToViewportLocal(new PixelPoint(run.minX, run.maxY), out var b) ||
                    !viewer.TryImagePixelToViewportLocal(new PixelPoint(run.maxX, run.maxY), out var c) ||
                    !viewer.TryImagePixelToViewportLocal(new PixelPoint(run.maxX, run.minY), out var d)) continue;
                AddQuad(vh, a, b, c, d, currentTint);
            }
        }
        private static void AddQuad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        { var index = vh.currentVertCount; vh.AddVert(a, tint, Vector2.zero); vh.AddVert(b, tint, Vector2.zero); vh.AddVert(c, tint, Vector2.zero); vh.AddVert(d, tint, Vector2.zero); vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index + 2, index + 3, index); }
    }
}
