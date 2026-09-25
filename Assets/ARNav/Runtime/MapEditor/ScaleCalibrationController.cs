using System;
using ARNav.Core.Geometry;
using ARNav.Runtime.Import;
using UnityEngine;
using UnityEngine.EventSystems;

namespace ARNav.Runtime.MapEditor
{
    /// <summary>Collects calibration points in source pixels; calculation and persistence remain outside the UI.</summary>
    public sealed class ScaleCalibrationController : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private FloorPlanViewer viewer;
        [SerializeField] private ScaleCalibrationOverlay overlay;
        private bool active;
        private bool hasFirst;
        private bool hasSecond;
        private PixelPoint first;
        private PixelPoint second;
        private Action<string> statusCallback;

        public bool IsActive { get { return active; } }
        public bool HasFirstPoint { get { return hasFirst; } }
        public bool HasSecondPoint { get { return hasSecond; } }
        public PixelPoint FirstPoint { get { return first; } }
        public PixelPoint SecondPoint { get { return second; } }

        public void Initialize(FloorPlanViewer floorPlanViewer, ScaleCalibrationOverlay pointOverlay, Action<string> onStatus)
        { viewer = floorPlanViewer; overlay = pointOverlay; statusCallback = onStatus; SetActive(false); }

        public void SetActive(bool value)
        {
            active = value; hasFirst = false; hasSecond = false;
            if (viewer != null) viewer.NavigationEnabled = true;
            if (overlay != null) overlay.raycastTarget = value;
            RefreshOverlay();
        }

        public void ResetPoints() { hasFirst = false; hasSecond = false; RefreshOverlay(); }

        public void LoadPoints(PixelPoint firstPoint, PixelPoint secondPoint)
        { first = firstPoint; second = secondPoint; hasFirst = true; hasSecond = true; RefreshOverlay(); }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!active || eventData.button != PointerEventData.InputButton.Left || viewer == null || !viewer.TryScreenToImagePixel(eventData.position, out var pixel)) return;
            if (!hasFirst) { first = pixel; hasFirst = true; statusCallback?.Invoke("Scale point A selected."); }
            else { second = pixel; hasSecond = true; statusCallback?.Invoke("Scale point B selected."); }
            RefreshOverlay();
        }

        private void RefreshOverlay() { if (overlay != null) overlay.SetPoints(hasFirst, first, hasSecond, second); }
    }
}
