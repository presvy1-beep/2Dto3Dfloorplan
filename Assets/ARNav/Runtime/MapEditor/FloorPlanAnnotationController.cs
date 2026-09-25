using System;
using System.Collections.Generic;
using ARNav.Core.Annotation;
using ARNav.Core.Data;
using ARNav.Core.Geometry;
using ARNav.Core.Serialization;
using ARNav.Core.Validation;
using ARNav.Runtime.Import;
using ARNav.Runtime.App;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ARNav.Runtime.MapEditor
{
    public enum AnnotationTool { PanView, AddRoom, AddWall, AddDoor, SelectEdit }

    public sealed class FloorPlanAnnotationController : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private FloorPlanViewer viewer;
        [SerializeField] private FloorPlanAnnotationOverlay overlay;
        private MapProjectData project;
        private FloorData floor;
        private LocalFileMapRepository repository;
        private readonly AnnotationHistory history = new AnnotationHistory();
        private readonly List<PixelPoint> draft = new List<PixelPoint>();
        private AnnotationSelection selection;
        private AnnotationTool tool = AnnotationTool.PanView;
        private string pendingDoorWallId;
        private float pendingDoorStart = -1;
        private FloorPlanAnnotationData dragBefore;
        private bool dragging;
        private bool dragged;
        private bool dragDoor;
        private float dragDoorWidth;
        private float dragDoorCenter;
        private Action annotationsCommitted;

        public AnnotationTool Tool { get { return tool; } }
        public AnnotationSelection Selection { get { return selection; } }
        public bool CanUndo { get { return history.CanUndo; } }
        public bool CanRedo { get { return history.CanRedo; } }

        public void Initialize(MapProjectData map, LocalFileMapRepository mapRepository, Action onAnnotationsCommitted = null)
        {
            project = map; repository = mapRepository; annotationsCommitted = onAnnotationsCommitted; floor = project != null && project.floors != null && project.floors.Count > 0 ? project.floors[0] : null;
            if (floor != null && floor.sourceAnnotations == null) floor.sourceAnnotations = new FloorPlanAnnotationData();
            history.Clear(); selection = new AnnotationSelection(); SetTool(AnnotationTool.PanView); RefreshOverlay();
        }

        /// <summary>Refreshes the overlay after the viewer texture or viewport changes.</summary>
        public void Refresh()
        {
            RefreshOverlay();
        }

        public void SetTool(AnnotationTool value)
        {
            CancelDraft(); tool = value; if (viewer != null) viewer.NavigationEnabled = value == AnnotationTool.PanView; if (overlay != null) overlay.raycastTarget = value != AnnotationTool.PanView; SetStatus(value.ToString());
        }

        public void FinishDraft()
        {
            if (floor == null) return;
            if (tool == AnnotationTool.AddRoom)
            {
                var validation = AnnotationValidator.Validate(new FloorPlanAnnotationData { rooms = new List<RoomAnnotationData> { new RoomAnnotationData { id = "draft", vertices = new List<PixelPoint>(draft) } } }, viewer == null ? 0 : viewer.SourceWidthPixels, viewer == null ? 0 : viewer.SourceHeightPixels);
                if (!validation.IsValid) { SetStatus(string.Join(" ", validation.errors.ToArray())); return; }
                Commit(() => floor.sourceAnnotations.rooms.Add(new RoomAnnotationData { id = NewId("room"), displayName = "Room " + (floor.sourceAnnotations.rooms.Count + 1), vertices = new List<PixelPoint>(draft) }));
                SetTool(AnnotationTool.SelectEdit); return;
            }
            if (tool == AnnotationTool.AddWall && draft.Count == 2)
            {
                if (AnnotationHitTester.Distance(draft[0], draft[1]) < AnnotationValidator.MinimumWallLengthPixels) { SetStatus("Wall is too short."); return; }
                Commit(() => floor.sourceAnnotations.walls.Add(new WallAnnotationData { id = NewId("wall"), start = draft[0], end = draft[1] })); SetTool(AnnotationTool.SelectEdit); return;
            }
            if (tool == AnnotationTool.AddDoor) SetStatus("Choose a wall, then tap two points along it.");
        }

        public void CancelDraft() { draft.Clear(); pendingDoorWallId = null; pendingDoorStart = -1; if (overlay != null) overlay.SetDraft(draft); }
        public void Undo() { if (!history.CanUndo || floor == null) return; floor.sourceAnnotations = history.Undo(floor.sourceAnnotations); selection = new AnnotationSelection(); SaveAndRefresh(); }
        public void Redo() { if (!history.CanRedo || floor == null) return; floor.sourceAnnotations = history.Redo(floor.sourceAnnotations); selection = new AnnotationSelection(); SaveAndRefresh(); }
        public void DeleteSelected()
        {
            if (floor == null || selection.kind == AnnotationSelectionKind.None) return;
            Commit(() => { if (selection.kind == AnnotationSelectionKind.Room || selection.kind == AnnotationSelectionKind.RoomVertex) floor.sourceAnnotations.rooms.RemoveAll(room => room != null && room.id == selection.id); else if (selection.kind == AnnotationSelectionKind.Wall || selection.kind == AnnotationSelectionKind.WallEndpoint) AnnotationOperations.DeleteWallAndDoors(floor.sourceAnnotations, selection.id); else if (selection.kind == AnnotationSelectionKind.Door) floor.sourceAnnotations.doors.RemoveAll(door => door != null && door.id == selection.id); }); selection = new AnnotationSelection();
        }
        public void RenameSelectedRoom(string name)
        {
            var room = selection.kind == AnnotationSelectionKind.Room || selection.kind == AnnotationSelectionKind.RoomVertex ? AnnotationHitTester.FindRoom(floor == null ? null : floor.sourceAnnotations, selection.id) : null;
            name = (name ?? "").Trim(); if (room == null || name.Length == 0) { SetStatus("Select a room and enter a name."); return; } Commit(() => AnnotationOperations.RenameRoom(floor.sourceAnnotations, room.id, name));
        }
        public ValidationResult ValidateCurrent()
        {
            var result = AnnotationValidator.Validate(floor == null ? null : floor.sourceAnnotations, viewer == null ? 0 : viewer.SourceWidthPixels, viewer == null ? 0 : viewer.SourceHeightPixels);
            if (floor == null || floor.sourceAnnotations.rooms.Count == 0) result.Add("At least one room is required."); return result;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || dragged || floor == null || !TryGetPixel(eventData.position, out var pixel)) return;
            if (tool == AnnotationTool.AddRoom) { draft.Add(pixel); RefreshOverlay(); SetStatus("Room vertex " + draft.Count + ". Press Finish Room when complete."); }
            else if (tool == AnnotationTool.AddWall) { if (draft.Count == 0) { draft.Add(pixel); SetStatus("Wall start selected."); } else if (draft.Count == 1) { draft.Add(pixel); SetStatus("Wall endpoint selected. Press Finish Wall."); } RefreshOverlay(); }
            else if (tool == AnnotationTool.AddDoor) AddDoorPoint(pixel);
            else if (tool == AnnotationTool.SelectEdit) { selection = AnnotationHitTester.Find(floor.sourceAnnotations, pixel, PixelTolerance()); if (overlay != null) overlay.SetSelection(selection); SetStatus(selection.kind == AnnotationSelectionKind.None ? "Nothing selected." : selection.kind + " selected."); }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            dragged = false; dragging = false; dragDoor = false; if (tool != AnnotationTool.SelectEdit || floor == null || !TryGetPixel(eventData.position, out var pixel)) return;
            selection = AnnotationHitTester.Find(floor.sourceAnnotations, pixel, PixelTolerance());
            if (selection.kind == AnnotationSelectionKind.RoomVertex || selection.kind == AnnotationSelectionKind.WallEndpoint) { dragBefore = AnnotationDataUtility.Clone(floor.sourceAnnotations); dragging = true; }
            else if (selection.kind == AnnotationSelectionKind.Door) { var door = AnnotationHitTester.FindDoor(floor.sourceAnnotations, selection.id); var wall = AnnotationHitTester.FindWall(floor.sourceAnnotations, door == null ? null : door.wallId); if (door != null && wall != null && AnnotationHitTester.TryProjectToWall(wall, pixel, out _, out _)) { dragBefore = AnnotationDataUtility.Clone(floor.sourceAnnotations); dragDoor = true; dragDoorWidth = door.endT - door.startT; dragDoorCenter = (door.startT + door.endT) * 0.5f; dragging = true; } }
            if (overlay != null) overlay.SetSelection(selection);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || floor == null || !TryGetPixel(eventData.position, out var pixel)) return; dragged = true;
            if (selection.kind == AnnotationSelectionKind.RoomVertex) { var room = AnnotationHitTester.FindRoom(floor.sourceAnnotations, selection.id); if (room != null && selection.vertexIndex >= 0 && selection.vertexIndex < room.vertices.Count) room.vertices[selection.vertexIndex] = Clamp(pixel); }
            else if (selection.kind == AnnotationSelectionKind.WallEndpoint) { var wall = AnnotationHitTester.FindWall(floor.sourceAnnotations, selection.id); if (wall != null) { if (selection.vertexIndex == 0) wall.start = Clamp(pixel); else wall.end = Clamp(pixel); } }
            else if (dragDoor) { var door = AnnotationHitTester.FindDoor(floor.sourceAnnotations, selection.id); var wall = AnnotationHitTester.FindWall(floor.sourceAnnotations, door == null ? null : door.wallId); if (door != null && wall != null && AnnotationHitTester.TryProjectToWall(wall, pixel, out var t, out _)) { var half = dragDoorWidth * 0.5f; door.startT = Mathf.Clamp01(t - half); door.endT = Mathf.Clamp01(t + half); if (door.endT <= door.startT) { door.startT = Mathf.Clamp01(t); door.endT = Mathf.Clamp01(t + 0.01f); } } }
            RefreshOverlay();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!dragging) return; dragging = false; if (!dragged) return; history.Record(dragBefore, floor.sourceAnnotations); SaveAndRefresh();
        }

        private void AddDoorPoint(PixelPoint pixel)
        {
            if (string.IsNullOrEmpty(pendingDoorWallId)) { var hit = AnnotationHitTester.Find(floor.sourceAnnotations, pixel, PixelTolerance()); if (hit.kind != AnnotationSelectionKind.Wall && hit.kind != AnnotationSelectionKind.WallEndpoint) { SetStatus("Tap an existing wall first."); return; } pendingDoorWallId = hit.id; if (hit.kind == AnnotationSelectionKind.WallEndpoint) pendingDoorWallId = hit.id; SetStatus("Wall selected. Tap door start."); return; }
            var wall = AnnotationHitTester.FindWall(floor.sourceAnnotations, pendingDoorWallId); if (wall == null || !AnnotationHitTester.TryProjectToWall(wall, pixel, out var t, out _)) { SetStatus("Could not project point onto wall."); return; }
            if (pendingDoorStart < 0) { pendingDoorStart = t; SetStatus("Door start selected. Tap door end."); return; }
            var start = Mathf.Min(pendingDoorStart, t); var end = Mathf.Max(pendingDoorStart, t); if (end - start <= 0.001f) { SetStatus("Door must have nonzero width."); return; }
            Commit(() => floor.sourceAnnotations.doors.Add(new DoorAnnotationData { id = NewId("door"), wallId = pendingDoorWallId, startT = start, endT = end })); SetTool(AnnotationTool.SelectEdit);
        }

        private void Commit(Action mutation) { var before = AnnotationDataUtility.Clone(floor.sourceAnnotations); mutation(); history.Record(before, floor.sourceAnnotations); SaveAndRefresh(); }
        private void SaveAndRefresh() { if (repository != null && project != null) try { repository.SaveProject(project); annotationsCommitted?.Invoke(); } catch (Exception exception) { Debug.LogException(exception); SetStatus("Could not save annotations."); } RefreshOverlay(true); }
        private void RefreshOverlay(bool refreshRasterLayers = false) { if (overlay != null) { overlay.SetData(floor == null ? new FloorPlanAnnotationData() : floor.sourceAnnotations); if (refreshRasterLayers) overlay.RefreshRasterLayers(); overlay.SetSelection(selection); overlay.SetDraft(draft); } }
        private bool TryGetPixel(Vector2 screen, out PixelPoint pixel) { if (viewer != null && viewer.TryScreenToImagePixel(screen, out pixel)) return true; pixel = new PixelPoint(); SetStatus("Point is outside the floor-plan image."); return false; }
        private float PixelTolerance() { return viewer == null || viewer.ImagePixelsPerViewerUnit <= 0 ? 12f : 12f * viewer.ImagePixelsPerViewerUnit; }
        private PixelPoint Clamp(PixelPoint pixel) { return new PixelPoint(Mathf.Clamp(pixel.x, 0, viewer.SourceWidthPixels), Mathf.Clamp(pixel.y, 0, viewer.SourceHeightPixels)); }
        private static string NewId(string prefix) { return prefix + "_" + Guid.NewGuid().ToString("N").Substring(0, 10); }
        private void SetStatus(string message) { var setup = FindAnyObjectByType<MapSetupController>(); if (setup != null) setup.SetExternalStatus(message); }
    }
}
