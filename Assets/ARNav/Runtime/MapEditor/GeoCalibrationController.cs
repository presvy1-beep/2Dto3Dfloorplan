using System;
using System.Collections.Generic;
using ARNav.Core.Data;
using ARNav.Core.Geo;
using ARNav.Core.Geometry;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ARNav.Runtime.MapEditor
{
    /// <summary>Collects source-pixel geographic anchors and exposes them to the setup coordinator.</summary>
    public sealed class GeoCalibrationController : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Import.FloorPlanViewer viewer;
        [SerializeField] private GeoAnchorOverlay overlay;
        private MapProjectData project;
        private bool active;
        private bool hasPending;
        private PixelPoint pending;
        private Action<string> status;
        public bool IsActive { get { return active; } }
        public bool HasPendingPoint { get { return hasPending; } }
        public PixelPoint PendingPoint { get { return pending; } }
        public IList<GeoAnchorData> Anchors { get { return project == null || project.geographicCalibration == null ? null : project.geographicCalibration.anchors; } }

        public void Initialize(MapProjectData value, Import.FloorPlanViewer floorPlanViewer, GeoAnchorOverlay anchorOverlay, Action<string> onStatus)
        { project = value; viewer = floorPlanViewer; overlay = anchorOverlay; status = onStatus; if (project != null && project.geographicCalibration == null) project.geographicCalibration = new GeoCalibrationData(); SetActive(false); Refresh(); }
        public void SetActive(bool value) { active = value; if (overlay != null) overlay.raycastTarget = value; if (!value) hasPending = false; Refresh(); }
        public void Refresh() { if (overlay != null) overlay.SetAnchors(Anchors, hasPending, pending); }
        public void OnPointerClick(PointerEventData eventData)
        { if (!active || eventData.button != PointerEventData.InputButton.Left || viewer == null || !viewer.TryScreenToImagePixel(eventData.position, out var point)) return; pending = point; hasPending = true; status?.Invoke("Geographic anchor point selected. Enter coordinates and press Add Anchor."); Refresh(); }
        public bool AddAnchor(double latitude, double longitude, string label, out string error)
        {
            error = null; if (!hasPending) { error = "Select a map point first."; return false; }
            var coordinate = new GeoCoordinate(latitude, longitude); if (!GeoProjectionService.IsValid(coordinate)) { error = "Latitude or longitude is invalid."; return false; }
            if (project == null || project.scaleCalibration == null || !project.scaleCalibration.isValid) { error = "Scale calibration is required."; return false; }
            var id = "geo_anchor_" + Guid.NewGuid().ToString("N").Substring(0, 10); var anchor = new GeoAnchorData { id = id, sourcePixel = pending, hasSourcePixel = true, localPosition = PixelToLocalGeometryConverter.ToLocal(pending, project.scaleCalibration.metresPerPixel, project.sourceImage.originalHeightPixels), coordinate = coordinate, label = string.IsNullOrWhiteSpace(label) ? id : label.Trim() };
            foreach (var existing in project.geographicCalibration.anchors) if (existing != null && existing.hasSourcePixel && Distance(existing.sourcePixel, pending) < 0.001f) { error = "An anchor already exists at this image point."; return false; }
            project.geographicCalibration.anchors.Add(anchor); project.geographicCalibration.isValid = false; project.geographicCalibration.validationMessage = "Anchors changed; recalibration required."; hasPending = false; Refresh(); return true;
        }
        public bool DeleteLastAnchor()
        { if (project == null || project.geographicCalibration == null || project.geographicCalibration.anchors.Count == 0) return false; project.geographicCalibration.anchors.RemoveAt(project.geographicCalibration.anchors.Count - 1); project.geographicCalibration.isValid = false; Refresh(); return true; }
        public bool EditLastAnchor(double latitude, double longitude, string label, out string error)
        {
            error = null; if (!hasPending || project == null || project.geographicCalibration.anchors.Count == 0) { error = "Select a map point and have an existing anchor first."; return false; }
            var coordinate = new GeoCoordinate(latitude, longitude); if (!GeoProjectionService.IsValid(coordinate)) { error = "Latitude or longitude is invalid."; return false; }
            var anchor = project.geographicCalibration.anchors[project.geographicCalibration.anchors.Count - 1]; anchor.sourcePixel = pending; anchor.hasSourcePixel = true; anchor.localPosition = PixelToLocalGeometryConverter.ToLocal(pending, project.scaleCalibration.metresPerPixel, project.sourceImage.originalHeightPixels); anchor.coordinate = coordinate; if (!string.IsNullOrWhiteSpace(label)) anchor.label = label.Trim(); project.geographicCalibration.isValid = false; project.geographicCalibration.validationMessage = "Anchors changed; recalibration required."; hasPending = false; Refresh(); return true;
        }
        public void SyncLocalPositions()
        { if (project == null || project.scaleCalibration == null || !project.scaleCalibration.isValid) return; foreach (var anchor in Anchors) if (anchor != null && anchor.hasSourcePixel) anchor.localPosition = PixelToLocalGeometryConverter.ToLocal(anchor.sourcePixel, project.scaleCalibration.metresPerPixel, project.sourceImage.originalHeightPixels); }
        private static float Distance(PixelPoint a, PixelPoint b) { var x = a.x - b.x; var y = a.y - b.y; return Mathf.Sqrt(x * x + y * y); }
    }
}
